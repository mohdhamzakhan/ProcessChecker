import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import { useUser } from "../user";
import Icon from "../designer/Icon";
import { STEP_TYPES } from "../designer/model";
import { ErrorBox, InstanceTable, ago } from "../ui";

const STATUS = [
    ["Running", "#f59e0b"], ["Completed", "#16a34a"], ["Rejected", "#ef4444"],
    ["Failed", "#b91c1c"], ["Cancelled", "#94a3b8"],
];

function Kpi({ icon, color, value, label, to }) {
    const body = (
        <>
            <span className="ki"><Icon name={icon} size={22} /></span>
            <div><b>{value}</b><small>{label}</small></div>
        </>
    );
    return to
        ? <Link to={to} className="kpi" style={{ "--c": color }}>{body}</Link>
        : <div className="kpi" style={{ "--c": color }}>{body}</div>;
}

function Stack({ counts, total }) {
    if (!total) return <div className="stack" />;
    return (
        <div className="stack">
            {STATUS.map(([s, c]) => (counts[s] ? <i key={s} title={`${s}: ${counts[s]}`} style={{ width: `${(counts[s] / total) * 100}%`, background: c }} /> : null))}
        </div>
    );
}

export default function Dashboard() {
    const { me } = useUser();
    const [stats, setStats] = useState(null);
    const [recent, setRecent] = useState([]);
    const [failed, setFailed] = useState([]);
    const [tasks, setTasks] = useState({ mine: [] });
    const [error, setError] = useState(null);

    const load = async () => {
        try {
            const [s, r, f, t] = await Promise.all([
                api.get("/api/dashboard"),
                api.get("/api/instances?pageSize=8"),
                api.get("/api/instances?status=Failed&pageSize=5"),
                api.get("/api/tasks"),
            ]);
            setStats(s); setRecent(r.items); setFailed(f.items); setTasks(t); setError(null);
        } catch (e) { setError(e); }
    };

    useEffect(() => {
        load();
        const t = setInterval(load, 20000);
        return () => clearInterval(t);
    }, []);

    const hour = new Date().getHours();
    const greet = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
    const first = (me?.name || "").split(" ")[0];
    const by = stats?.byStatus || {};
    const total = stats?.total || 0;
    const max = Math.max(1, ...(stats?.perDay || []).map((d) => d.count));
    const waiting = tasks.mine || [];

    return (
        <>
            <div className="page-head">
                <div>
                    <h2>{greet}{first && `, ${first}`}</h2>
                    <p>Here is what is happening across your processes.</p>
                </div>
                <Link to="/processes"><button>+ New request</button></Link>
            </div>

            <ErrorBox error={error} />

            {stats && (
                <>
                    <div className="kpis">
                        <Kpi icon="tasks" color="#4f46e5" value={waiting.length} label="Waiting for you" to="/tasks" />
                        <Kpi icon="workflow" color="#f59e0b" value={by.Running || 0} label="In progress" to="/requests?status=Running" />
                        <Kpi icon="check" color="#16a34a" value={by.Completed || 0} label="Completed" to="/requests?status=Completed" />
                        <Kpi icon="alert" color="#dc2626" value={(by.Failed || 0) + (by.Rejected || 0)} label="Failed or rejected" to="/requests?status=Failed" />
                        <Kpi icon="requests" color="#0891b2" value={total} label="All requests" to="/requests" />
                        <Kpi icon="play" color="#7c3aed" value={stats.avgHours == null ? "–" : `${stats.avgHours} h`} label={`Average time to finish (${stats.completed30} in 30 days)`} />
                    </div>

                    <div className="grid2">
                        <div className="card-box">
                            <h3>Requests by status</h3>
                            <Stack counts={by} total={total} />
                            <div className="legend">
                                {STATUS.map(([s, c]) => <span key={s} style={{ "--c": c }}>{s} <b>{by[s] || 0}</b></span>)}
                            </div>
                        </div>
                        <div className="card-box">
                            <h3>Started in the last 14 days</h3>
                            <div className="bars">
                                {stats.perDay.map((d, n) => (
                                    <div key={d.date} title={`${d.date}: ${d.count}`}>
                                        <i style={{ height: `${d.count ? Math.max(4, Math.round((d.count / max) * 90)) : 3}px` }} />
                                        <small>{n % 2 === 0 ? d.date.slice(8) : ""}</small>
                                    </div>
                                ))}
                            </div>
                        </div>
                    </div>

                    <div className="grid2">
                        <div className="card-box">
                            <h3>Waiting for you</h3>
                            {waiting.length === 0 && <p className="muted">Nothing is waiting for you.</p>}
                            {waiting.slice(0, 5).map((t) => {
                                const m = STEP_TYPES[t.stepType] || STEP_TYPES.Form;
                                return (
                                    <Link key={t.instanceId + t.stepKey} to={`/instances/${t.instanceId}`} className="mrow" style={{ "--c": m.color }}>
                                        <span className="tbadge"><Icon name={m.icon} size={18} /></span>
                                        <span className="tmain"><b>{t.title}</b><small>{t.stepName} · {ago(t.since)}</small></span>
                                    </Link>
                                );
                            })}
                            {waiting.length > 5 && <Link to="/tasks" className="small">See all {waiting.length}</Link>}
                        </div>
                        <div className="card-box">
                            <h3>Needs attention</h3>
                            {failed.length === 0 && <p className="muted">No failed requests.</p>}
                            {failed.map((i) => (
                                <Link key={i.id} to={`/instances/${i.id}`} className="mrow" style={{ "--c": "#dc2626" }}>
                                    <span className="tbadge"><Icon name="alert" size={18} /></span>
                                    <span className="tmain"><b>{i.title}</b><small>Stopped at {i.currentStep || "a step"} · {ago(i.createdAt)}</small></span>
                                </Link>
                            ))}
                        </div>
                    </div>

                    <h3>Recent requests</h3>
                    <InstanceTable items={recent} />

                    <h3>By process</h3>
                    <div className="card-box">
                        {stats.byProcess.length === 0 && <p className="muted">No requests yet.</p>}
                        {stats.byProcess.map((p) => (
                            <div key={p.processKey} className="prow">
                                <b>{p.processName}</b>
                                <Stack counts={p.byStatus} total={p.total} />
                                <small className="muted">{p.total} request{p.total === 1 ? "" : "s"}</small>
                            </div>
                        ))}
                    </div>
                </>
            )}
        </>
    );
}