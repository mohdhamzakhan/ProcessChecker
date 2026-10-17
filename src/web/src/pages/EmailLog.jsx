import { useEffect, useState } from "react";
import { api } from "../api";
import Icon from "../designer/Icon";
import { ErrorBox, ago, fmt } from "../ui";
import { useDialogs } from "../Dialogs";
import { Link } from "react-router-dom";

const BADGE = { Sent: "Completed", Failed: "Failed", DryRun: "Cancelled" };

export default function EmailLog() {
    const { confirm } = useDialogs();
    const [stats, setStats] = useState(null);
    const [data, setData] = useState(null);
    const [status, setStatus] = useState("");
    const [q, setQ] = useState("");
    const [term, setTerm] = useState("");
    const [page, setPage] = useState(1);
    const [days, setDays] = useState("");
    const [view, setView] = useState(null);
    const [msg, setMsg] = useState("");
    const [error, setError] = useState(null);
    const [tick, setTick] = useState(0);

    useEffect(() => {
        const t = setTimeout(() => { setTerm(q.trim()); setPage(1); }, 300);
        return () => clearTimeout(t);
    }, [q]);

    useEffect(() => {
        const qs = new URLSearchParams({ page });
        if (status) qs.set("status", status);
        if (term) qs.set("q", term);
        Promise.all([api.get("/api/notifications/stats"), api.get(`/api/notifications?${qs}`)])
            .then(([s, d]) => { setStats(s); setData(d); setError(null); })
            .catch(setError);
    }, [status, term, page, tick]);

    const open = (id) => api.get(`/api/notifications/${id}`).then(setView).catch(setError);

    const resend = async (id) => {
        setMsg("");
        try { await api.post(`/api/notifications/${id}/resend`); setMsg("Sent."); setError(null); }
        catch (e) { setError(e); }
        setTick((n) => n + 1);
    };

    const cleanup = async () => {
        const d = Number(days) || stats?.retentionDays || 90;
        if (!(await confirm(`Delete every email older than ${d} days from the log? This cannot be undone.`,
            { title: "Delete old emails", okLabel: "Delete", danger: true }))) return;
        try {
            const r = await api.post(`/api/notifications/cleanup?days=${d}&failedDays=${d}`);
            setMsg(`Deleted ${r.removed} email(s).`); setError(null); setTick((n) => n + 1);
        } catch (e) { setError(e); }
    };

    const by = stats?.byStatus || {};
    const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

    return (
        <>
            <div className="page-head">
                <div><h2>Email log</h2><p>Every email the system sent, or tried to send.</p></div>
                <Link to="/emails/templates"><button className="secondary">Preview templates</button></Link>
            </div>
            <ErrorBox error={error} />
            {msg && <div className="okbar" style={{ borderRadius: 10, marginBottom: 14 }}>{msg}</div>}

            {stats && (
                <div className="kpis">
                    <div className="kpi" style={{ "--c": "#4f46e5" }}><span className="ki"><Icon name="mail" size={22} /></span>
                        <div><b>{stats.total}</b><small>In the log</small></div></div>
                    <div className="kpi" style={{ "--c": "#16a34a" }}><span className="ki"><Icon name="check" size={22} /></span>
                        <div><b>{by.Sent || 0}</b><small>Sent</small></div></div>
                    <div className="kpi" style={{ "--c": "#dc2626" }}><span className="ki"><Icon name="alert" size={22} /></span>
                        <div><b>{by.Failed || 0}</b><small>Failed</small></div></div>
                    <div className="kpi" style={{ "--c": "#64748b" }}><span className="ki"><Icon name="code" size={22} /></span>
                        <div><b>{by.DryRun || 0}</b><small>Test mode (not sent)</small></div></div>
                </div>
            )}

            <div className="panel" style={{ marginTop: 0 }}>
                <b>Clean-up</b>
                <p className="muted small" style={{ margin: "4px 0 10px" }}>
                    Old emails are deleted automatically every day: sent ones after {stats?.retentionDays ?? 90} days,
                    failed ones after {stats?.failedRetentionDays ?? 180} days.
                    {stats?.oldest && <> The oldest email in the log is from {fmt(stats.oldest)}.</>}
                </p>
                <div className="actions">
                    <input type="number" min={1} style={{ width: 130 }} placeholder={`${stats?.retentionDays ?? 90} days`}
                        value={days} onChange={(e) => setDays(e.target.value)} />
                    <button className="danger" onClick={cleanup}>Delete older emails now</button>
                </div>
            </div>

            <div className="filterbar">
                <div className="search"><Icon name="search" size={16} />
                    <input placeholder="Search recipient or subject" value={q} onChange={(e) => setQ(e.target.value)} /></div>
                <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}>
                    <option value="">All results</option><option value="Sent">Sent</option>
                    <option value="Failed">Failed</option><option value="DryRun">Test mode</option>
                </select>
            </div>

            {data && data.items.length === 0 && <div className="empty"><b>No emails found</b>Try another filter.</div>}

            {data && data.items.length > 0 && (
                <div className="scroll">
                    <table>
                        <thead><tr><th>When</th><th>To</th><th>Subject</th><th>Result</th><th></th></tr></thead>
                        <tbody>
                            {data.items.map((n) => (
                                <tr key={n.id}>
                                    <td title={fmt(n.createdAt)}>{ago(n.createdAt)}</td>
                                    <td style={{ maxWidth: 220, overflowWrap: "anywhere" }}>{n.toList.split(";").join(", ")}</td>
                                    <td>{n.subject}{n.attachmentNames && <div className="muted small">📎 {n.attachmentNames}</div>}</td>
                                    <td>
                                        <span className={`badge ${BADGE[n.status] || ""}`}>{n.status === "DryRun" ? "Test mode" : n.status}</span>
                                        {n.error && <div className="ok-err">{n.error}</div>}
                                        {n.attempts > 1 && <div className="muted small">{n.attempts} attempts</div>}
                                    </td>
                                    <td>
                                        <div className="actions">
                                            <button className="secondary" onClick={() => open(n.id)}>View</button>
                                            {n.status === "Failed" && <button onClick={() => resend(n.id)}>Resend</button>}
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {data && data.total > data.pageSize && (
                <div className="toolbar">
                    <button className="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
                    <span className="muted">Page {page} of {pages} · {data.total} emails</span>
                    <button className="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
                </div>
            )}

            {view && (
                <div className="vback" onClick={() => setView(null)}>
                    <div className="vbox" onClick={(e) => e.stopPropagation()}>
                        <div className="vhead">
                            <b title={view.subject}>{view.subject}</b>
                            <button className="icon" onClick={() => setView(null)}><Icon name="x" size={18} /></button>
                        </div>
                        <div className="vnote" style={{ borderTop: 0, borderBottom: "1px solid var(--line)" }}>To: {view.toList.split(";").join(", ")}</div>
                        <div className="vbody">
                            {view.isHtml
                                ? <iframe title="email" sandbox="" srcDoc={view.body} />
                                : <pre style={{ margin: 0, borderRadius: 0, whiteSpace: "pre-wrap" }}>{view.body}</pre>}
                        </div>
                    </div>
                </div>
            )}
        </>
    );
}