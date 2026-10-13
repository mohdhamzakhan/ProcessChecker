import { useEffect, useState } from "react";
import { api } from "../api";
import { useUser } from "../user";
import Icon from "../designer/Icon";
import { uid } from "../designer/model";
import { ErrorBox, InstanceTable } from "../ui";

const METRICS = {
    total: { label: "Total requests", icon: "workflow", color: "#4f46e5" },
    completed: { label: "Completed", icon: "check", color: "#16a34a" },
    running: { label: "In progress", icon: "play", color: "#f59e0b" },
    rejected: { label: "Rejected", icon: "x", color: "#ef4444" },
    failed: { label: "Failed", icon: "alert", color: "#b91c1c" },
    cancelled: { label: "Cancelled", icon: "x", color: "#94a3b8" },
    completionRate: { label: "Completion rate", icon: "chart", color: "#0891b2" },
    avgHours: { label: "Average time to finish", icon: "play", color: "#7c3aed" },
};
const CHARTS = {
    status: "Status breakdown",
    perDay: "Started vs completed, by day",
    perMonth: "Started vs completed, by month",
    byStep: "Where requests are waiting",
    recent: "Recent requests",
};
const STATUS = [["Running", "#f59e0b"], ["Completed", "#16a34a"], ["Rejected", "#ef4444"], ["Failed", "#b91c1c"], ["Cancelled", "#94a3b8"]];
const RANGES = [[7, "Last 7 days"], [30, "Last 30 days"], [90, "Last 90 days"], [365, "Last year"], [0, "All time"]];

function metricValue(m, d) {
    const by = d.byStatus || {};
    switch (m) {
        case "total": return d.total;
        case "completed": return by.Completed || 0;
        case "running": return by.Running || 0;
        case "rejected": return by.Rejected || 0;
        case "failed": return by.Failed || 0;
        case "cancelled": return by.Cancelled || 0;
        case "completionRate": return d.total ? `${Math.round(((by.Completed || 0) / d.total) * 100)}%` : "–";
        case "avgHours": return d.avgHours == null ? "–" : `${d.avgHours} h`;
        default: return "–";
    }
}

function Stack({ counts, total }) {
    if (!total) return <div className="stack" />;
    return (
        <div className="stack">
            {STATUS.map(([s, c]) => (counts[s]
                ? <i key={s} title={`${s}: ${counts[s]}`} style={{ width: `${(counts[s] / total) * 100}%`, background: c }} /> : null))}
        </div>
    );
}

function Pairs({ rows }) {
    const max = Math.max(1, ...rows.flatMap((r) => [r.started, r.completed]));
    const h = (n) => (n ? Math.max(4, Math.round((n / max) * 110)) : 2);
    const step = rows.length > 14 ? Math.ceil(rows.length / 10) : 1;
    return (
        <>
            <div className="pairs">
                {rows.map((r, i) => (
                    <div key={r.key} title={`${r.label}: ${r.started} started, ${r.completed} completed`}>
                        <span><i className="s" style={{ height: h(r.started) }} /><i className="c" style={{ height: h(r.completed) }} /></span>
                        <small>{i % step === 0 ? r.label : ""}</small>
                    </div>
                ))}
            </div>
            <div className="legend">
                <span style={{ "--c": "#818cf8" }}>Started</span>
                <span style={{ "--c": "#22c55e" }}>Completed</span>
            </div>
        </>
    );
}

function Widget({ w, data }) {
    if (w.type === "kpi") {
        const m = METRICS[w.metric] || METRICS.total;
        return (
            <div className="dcell w1">
                <div className="kpi" style={{ "--c": m.color }}>
                    <span className="ki"><Icon name={m.icon} size={22} /></span>
                    <div><b>{data ? metricValue(w.metric, data) : "…"}</b><small>{w.title || m.label}</small></div>
                </div>
            </div>
        );
    }

    const title = w.title || CHARTS[w.type];
    let body = <p className="muted">Loading…</p>;
    if (data) {
        if (w.type === "status") {
            body = (
                <>
                    <Stack counts={data.byStatus} total={data.total} />
                    <div className="legend">
                        {STATUS.map(([s, c]) => <span key={s} style={{ "--c": c }}>{s} <b>{data.byStatus[s] || 0}</b></span>)}
                    </div>
                </>
            );
        } else if (w.type === "perDay") {
            body = <Pairs rows={data.perDay.map((d) => ({ key: d.date, label: d.date.slice(5), started: d.started, completed: d.completed }))} />;
        } else if (w.type === "perMonth") {
            body = <Pairs rows={data.perMonth.map((d) => ({ key: d.month, label: d.month.slice(2), started: d.started, completed: d.completed }))} />;
        } else if (w.type === "byStep") {
            const max = Math.max(1, ...data.byStep.map((r) => r.count));
            body = data.byStep.length === 0
                ? <p className="muted">Nothing is waiting.</p>
                : data.byStep.map((r) => (
                    <div key={r.step} className="hrow">
                        <span title={r.step}>{r.step}</span>
                        <div className="hbar"><i style={{ width: `${(r.count / max) * 100}%` }} /></div>
                        <b>{r.count}</b>
                    </div>
                ));
        } else if (w.type === "recent") {
            body = <InstanceTable items={data.recent} />;
        }
    }
    return (
        <div className={`dcell ${w.type === "recent" ? "w4" : "w2"}`}>
            <div className="card-box"><h3>{title}</h3>{body}</div>
        </div>
    );
}

function View({ config, data }) {
    return <div className="dgrid">{(config.widgets || []).map((w) => <Widget key={w.id} w={w} data={data} />)}</div>;
}

function Builder({ draft, setDraft, scope, onSave, onCancel, busy }) {
    const cfg = draft.config;
    const setCfg = (p) => setDraft((d) => ({ ...d, config: { ...d.config, ...p } }));
    const setWidgets = (widgets) => setCfg({ widgets });

    const toggleKey = (k) =>
        setCfg({ processKeys: cfg.processKeys.includes(k) ? cfg.processKeys.filter((x) => x !== k) : [...cfg.processKeys, k] });

    const add = (v) => {
        if (!v) return;
        const w = v.startsWith("kpi:") ? { id: uid(), type: "kpi", metric: v.slice(4) } : { id: uid(), type: v };
        setWidgets([...cfg.widgets, w]);
    };
    const move = (i, d) => {
        const j = i + d;
        if (j < 0 || j >= cfg.widgets.length) return;
        const next = [...cfg.widgets];
        [next[i], next[j]] = [next[j], next[i]];
        setWidgets(next);
    };
    const label = (w) => (w.type === "kpi" ? METRICS[w.metric]?.label : CHARTS[w.type]);

    return (
        <div className="panel dbuilder">
            <div className="form">
                <label><span>Dashboard name</span>
                    <input value={draft.name} onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))} /></label>

                <div>
                    <b>Which processes?</b>
                    <p className="muted small">Leave all unticked to include every process you can see.</p>
                    {scope.length === 0 && <p className="muted">There are no requests you can see yet.</p>}
                    {scope.map((p) => (
                        <label key={p.key} className="checkrow">
                            <input type="checkbox" checked={cfg.processKeys.includes(p.key)} onChange={() => toggleKey(p.key)} />
                            {p.name} <small className="muted">({p.count})</small>
                        </label>
                    ))}
                </div>

                <label><span>Time range (by the day a request started)</span>
                    <select value={cfg.rangeDays} onChange={(e) => setCfg({ rangeDays: Number(e.target.value) })}>
                        {RANGES.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
                    </select></label>

                <div>
                    <b>Widgets</b>
                    {cfg.widgets.map((w, i) => (
                        <div key={w.id} className="wrow">
                            <input value={w.title || ""} placeholder={label(w)}
                                onChange={(e) => setWidgets(cfg.widgets.map((x) => (x.id === w.id ? { ...x, title: e.target.value } : x)))} />
                            <button className="icon" type="button" disabled={i === 0} onClick={() => move(i, -1)}><Icon name="up" size={15} /></button>
                            <button className="icon" type="button" disabled={i === cfg.widgets.length - 1} onClick={() => move(i, 1)}><Icon name="down" size={15} /></button>
                            <button className="icon danger" type="button" onClick={() => setWidgets(cfg.widgets.filter((x) => x.id !== w.id))}><Icon name="trash" size={15} /></button>
                        </div>
                    ))}
                    <select value="" onChange={(e) => add(e.target.value)} style={{ marginTop: 8 }}>
                        <option value="">+ Add a widget…</option>
                        <optgroup label="Numbers">
                            {Object.entries(METRICS).map(([k, m]) => <option key={k} value={`kpi:${k}`}>{m.label}</option>)}
                        </optgroup>
                        <optgroup label="Charts and lists">
                            {Object.entries(CHARTS).map(([k, l]) => <option key={k} value={k}>{l}</option>)}
                        </optgroup>
                    </select>
                </div>

                <div className="actions">
                    <button disabled={busy} onClick={onSave}>{busy ? "Saving..." : "Save dashboard"}</button>
                    <button className="secondary" onClick={onCancel}>Cancel</button>
                </div>
            </div>
        </div>
    );
}

const starter = () => ({
    id: null, name: "New dashboard",
    config: {
        processKeys: [], rangeDays: 30,
        widgets: [
            { id: uid(), type: "kpi", metric: "total" }, { id: uid(), type: "kpi", metric: "completed" },
            { id: uid(), type: "kpi", metric: "running" }, { id: uid(), type: "kpi", metric: "completionRate" },
            { id: uid(), type: "status" }, { id: uid(), type: "perDay" },
        ],
    },
});

export default function Dashboards() {
    const { isAdmin } = useUser();
    const [list, setList] = useState(null);
    const [scope, setScope] = useState([]);
    const [selId, setSelId] = useState(null);
    const [draft, setDraft] = useState(null);
    const [data, setData] = useState(null);
    const [error, setError] = useState(null);
    const [busy, setBusy] = useState(false);
    const [tick, setTick] = useState(0);

    const load = async (prefer) => {
        try {
            const [l, s] = await Promise.all([api.get("/api/dashboards"), api.get("/api/dashboards/scope")]);
            setList(l); setScope(s);
            setSelId((cur) => (prefer && l.some((d) => d.id === prefer) ? prefer : l.some((d) => d.id === cur) ? cur : l[0]?.id ?? null));
            setError(null);
        } catch (e) { setList([]); setError(e); }
    };
    useEffect(() => { load(); }, []);                                    // eslint-disable-line

    useEffect(() => {
        const t = setInterval(() => setTick((n) => n + 1), 60000);
        return () => clearInterval(t);
    }, []);

    const current = draft ? draft.config : list?.find((d) => d.id === selId)?.config;
    const cfgKey = JSON.stringify(current || null);

    useEffect(() => {
        if (!current) { setData(null); return undefined; }
        let off = false;
        const t = setTimeout(() => {
            api.post("/api/dashboards/data", current)
                .then((d) => { if (!off) { setData(d); setError(null); } })
                .catch((e) => { if (!off) setError(e); });
        }, draft ? 400 : 0);
        return () => { off = true; clearTimeout(t); };
    }, [cfgKey, tick]);                                                  // eslint-disable-line

    const save = async () => {
        setBusy(true);
        try {
            const body = { name: draft.name, config: draft.config };
            const saved = draft.id ? await api.put(`/api/dashboards/${draft.id}`, body) : await api.post("/api/dashboards", body);
            setDraft(null);
            await load(saved.id);
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    const remove = async () => {
        if (!confirm("Delete this dashboard?")) return;
        try { await api.del(`/api/dashboards/${selId}`); await load(); } catch (e) { setError(e); }
    };

    const selected = list?.find((d) => d.id === selId);

    return (
        <>
            <div className="page-head">
                <div>
                    <h2>My dashboards</h2>
                    <p>
                        {isAdmin
                            ? "As an admin you see every request."
                            : "Only requests where you started, are an assignee, approver or observer are counted."}
                    </p>
                </div>
                {!draft && (
                    <div className="actions">
                        <button onClick={() => setDraft(starter())}>+ New dashboard</button>
                        {selected && <button className="secondary" onClick={() => setDraft(JSON.parse(JSON.stringify(selected)))}>Edit</button>}
                        {selected && <button className="danger" onClick={remove}>Delete</button>}
                    </div>
                )}
            </div>

            <ErrorBox error={error} />

            {!draft && list && list.length > 1 && (
                <div className="fchips" style={{ marginBottom: 14 }}>
                    {list.map((d) => (
                        <button key={d.id} className={`fchip${d.id === selId ? " active" : ""}`} onClick={() => setSelId(d.id)}>{d.name}</button>
                    ))}
                </div>
            )}

            {!draft && list && list.length === 0 && (
                <div className="empty">
                    <b>You have no dashboards yet</b>
                    Build one for a process such as Domestic Travel to see how many requests came and how many were completed.
                </div>
            )}

            {draft ? (
                <div className="dbuild">
                    <Builder draft={draft} setDraft={setDraft} scope={scope} onSave={save} onCancel={() => setDraft(null)} busy={busy} />
                    <div>
                        <h3 style={{ marginTop: 0 }}>{draft.name}</h3>
                        <View config={draft.config} data={data} />
                    </div>
                </div>
            ) : (
                current && <View config={current} data={data} />
            )}
        </>
    );
}