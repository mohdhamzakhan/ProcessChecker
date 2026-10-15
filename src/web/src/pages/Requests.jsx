import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api";
import { buildLink } from "../links";
import Icon from "../designer/Icon";
import { Badge, ErrorBox, FileLinks, SafeLink, ago, fmt, DueTag } from "../ui";

const STATUSES = ["Running", "Completed", "Rejected", "Failed", "Cancelled"];
const initials = (s) => (s || "?").split(/[\s@._:-]+/).filter(Boolean).slice(0, 2).map((w) => w[0]).join("").toUpperCase();
const who = (s) => (!s ? "Unknown" : s === "system:poll" ? "Started automatically" : s);

function Segments({ done, total, status }) {
    const items = Array.from({ length: Math.max(total, 1) }, (_, n) => {
        if (n < done) return "done";
        if (n === done) return status === "Running" ? "now" : status === "Failed" || status === "Rejected" ? "bad" : "";
        return "";
    });
    return <div className="segs">{items.map((c, n) => <i key={n} className={c} />)}</div>;
}

function DocsModal({ item, links, onClose }) {
    const [detail, setDetail] = useState(null);
    const [error, setError] = useState(null);

    useEffect(() => {
        api.get(`/api/instances/${item.id}`).then(setDetail).catch(setError);
    }, [item.id]);

    const built = detail ? links.map((l) => ({ ...l, ...buildLink(l.url, detail.data) })) : [];
    const files = detail
        ? detail.steps.flatMap((st) => (st.output?.files || []).map((f) => ({ ...f, step: st.name })))
        : [];

    return (
        <div className="vback" onClick={onClose}>
            <div className="modal docsmodal" onClick={(e) => e.stopPropagation()}>
                <div className="modalhead">
                    <div><h3>Documents</h3><small className="muted">{item.title}</small></div>
                    <button className="icon" onClick={onClose} title="Close"><Icon name="x" size={18} /></button>
                </div>

                <ErrorBox error={error} />
                {!detail && !error && <p className="muted">Loading…</p>}

                {detail && (
                    <>
                        {built.length > 0 && (
                            <div className="docsec">
                                <h4>Links</h4>
                                {built.map((l, i) =>
                                    l.valid ? (
                                        <SafeLink key={i} url={l.url} label={l.label} instanceId={item.id} index={i} />
                                    ) : (
                                        <div key={i} className="linkrow">
                                            <Icon name="link" size={16} />
                                            <span className="muted">
                                                {l.label}: {l.missing.length ? `waiting for ${l.missing.join(", ")}` : "the address is not valid"}
                                            </span>
                                        </div>
                                    )
                                )}
                            </div>
                        )}
                        {files.length > 0 && (
                            <div className="docsec">
                                <h4>Uploaded files</h4>
                                <FileLinks files={files} />
                            </div>
                        )}
                        {built.length === 0 && files.length === 0 && (
                            <div className="empty" style={{ marginTop: 12 }}>
                                <b>No documents yet</b>
                                This process has no document links, and nobody has uploaded a file to this request.
                            </div>
                        )}
                        <div className="actions" style={{ marginTop: 16 }}>
                            <Link to={`/instances/${item.id}`}><button>Open the request</button></Link>
                        </div>
                    </>
                )}
            </div>
        </div>
    );
}

export default function Requests() {
    const [sp] = useSearchParams();
    const [status, setStatus] = useState(sp.get("status") || "");
    const [q, setQ] = useState("");
    const [term, setTerm] = useState("");
    const [role, setRole] = useState("");
    const [proc, setProc] = useState("");
    const [page, setPage] = useState(1);
    const [data, setData] = useState(null);
    const [defs, setDefs] = useState({});
    const [counts, setCounts] = useState({});
    const [docs, setDocs] = useState(null);
    const [error, setError] = useState(null);

    useEffect(() => {
        api.get("/api/definitions")
            .then((l) => setDefs(Object.fromEntries(l.map((d) => [d.key, d]))))
            .catch(() => { });
        api.get("/api/dashboard").then((d) => setCounts({ ...d.byStatus, _all: d.total })).catch(() => { });
    }, []);

    useEffect(() => {
        const t = setTimeout(() => { setTerm(q.trim()); setPage(1); }, 300);
        return () => clearTimeout(t);
    }, [q]);

    useEffect(() => {
        const qs = new URLSearchParams({ page });
        if (status) qs.set("status", status);
        if (term) qs.set("q", term);
        if (role) qs.set("role", role);
        if (proc) qs.set("processKey", proc);
        api.get(`/api/instances?${qs}`).then((d) => { setData(d); setError(null); }).catch(setError);
    }, [status, term, page, role, proc]);

    const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
    const pick = (fn) => (e) => { fn(e.target.value); setPage(1); };

    return (
        <>
            <div className="page-head">
                <div><h2>Requests</h2><p>Every request you can see, from every process.</p></div>
                <Link to="/processes"><button>+ New request</button></Link>
            </div>

            <ErrorBox error={error} />

            <div className="filterbar">
                <div className="search">
                    <Icon name="search" size={16} />
                    <input placeholder="Search by title" value={q} onChange={(e) => setQ(e.target.value)} />
                </div>
                <select value={proc} onChange={pick(setProc)}>
                    <option value="">All processes</option>
                    {Object.values(defs).map((d) => <option key={d.key} value={d.key}>{d.name}</option>)}
                </select>
                <select value={role} onChange={pick(setRole)}>
                    <option value="">Everything I can see</option>
                    <option value="started">I started</option>
                    <option value="assigned">Assigned to me</option>
                    <option value="observer">I observe</option>
                    <option value="requested">I requested</option>
                </select>
            </div>

            <div className="fchips" style={{ marginBottom: 14 }}>
                {["", ...STATUSES].map((s) => {
                    const n = s ? counts[s] : counts._all;
                    return (
                        <button key={s || "all"} className={`fchip${status === s ? " active" : ""}`}
                            onClick={() => { setStatus(s); setPage(1); }}>
                            {s || "All"}{n > 0 && <span className="n">{n}</span>}
                        </button>
                    );
                })}
            </div>

            {data === null && <p className="muted">Loading…</p>}

            {data && data.items.length === 0 && (
                <div className="empty"><b>No requests found</b>Try another filter, or start a new request.</div>
            )}

            {data && data.items.length > 0 && (
                <div className="rlist">
                    <div className="rhead">
                        <span>Request</span><span>Status</span><span>Progress</span><span>Current step</span><span>Started</span><span />
                    </div>

                    {data.items.map((i) => {
                        const hasLinks = (defs[i.processKey]?.definition.links || []).length > 0;
                        return (
                            <div key={i.id} className={`rrow ${i.status}`}>
                                <div className="rmain">
                                    <span className={`rav${i.startedBy === "system:poll" ? " auto" : ""}`}>
                                        {i.startedBy === "system:poll" ? <Icon name="workflow" size={16} /> : initials(i.startedBy)}
                                    </span>
                                    <div className="rtext">
                                        <Link to={`/instances/${i.id}`} title={i.title}>{i.title}</Link>
                                        <small>{i.processName} v{i.processVersion} · {who(i.startedBy)}</small>
                                    </div>
                                </div>

                                <div><Badge status={i.status} /></div>

                                <div>
                                    <Segments done={i.completedSteps} total={i.totalSteps} status={i.status} />
                                    <small className="muted">{i.completedSteps} of {i.totalSteps} steps</small>
                                </div>

                                <div className="rstep">
                                    {i.status === "Completed"
                                        ? <span className="muted">Finished {ago(i.completedAt)}</span>
                                        : (i.currentStep || "–")}
                                    {i.status === "Running" && i.dueAt && <div><DueTag dueAt={i.dueAt} /></div>}
                                </div>

                                <div title={fmt(i.createdAt)} className="muted">{ago(i.createdAt)}</div>

                                <div className="ractions">
                                    <button className={`secondary${hasLinks ? " has-links" : ""}`} onClick={() => setDocs(i)}
                                        title={hasLinks ? "Open the document links and files" : "Files of this request"}>
                                        <Icon name="link" size={15} />Documents
                                    </button>
                                    <Link to={`/instances/${i.id}`}><button>Open</button></Link>
                                </div>
                            </div>
                        );
                    })}
                </div>
            )}

            {data && data.total > data.pageSize && (
                <div className="toolbar">
                    <button className="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
                    <span className="muted">Page {page} of {pages} · {data.total} requests</span>
                    <button className="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
                </div>
            )}

            {docs && (
                <DocsModal item={docs} links={defs[docs.processKey]?.definition.links || []} onClose={() => setDocs(null)} />
            )}
        </>
    );
}