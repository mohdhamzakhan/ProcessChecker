import { useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api";
import { useUser } from "../user";
import Icon from "../designer/Icon";
import { ErrorBox, InstanceTable } from "../ui";

const STATUSES = ["", "Running", "Completed", "Rejected", "Failed", "Cancelled"];

export default function Requests() {
    const { user } = useUser();
    const [sp] = useSearchParams();
    const [status, setStatus] = useState(sp.get("status") || "");
    const [q, setQ] = useState("");
    const [term, setTerm] = useState("");
    const [mine, setMine] = useState(false);
    const [page, setPage] = useState(1);
    const [data, setData] = useState(null);
    const [error, setError] = useState(null);

    useEffect(() => {
        const t = setTimeout(() => { setTerm(q.trim()); setPage(1); }, 300);
        return () => clearTimeout(t);
    }, [q]);

    useEffect(() => {
        const qs = new URLSearchParams({ page });
        if (status) qs.set("status", status);
        if (term) qs.set("q", term);
        if (mine && user) qs.set("startedBy", user);
        api.get(`/api/instances?${qs}`).then((d) => { setData(d); setError(null); }).catch(setError);
    }, [status, term, page, mine, user]);

    const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

    return (
        <>
            <div className="page-head">
                <div><h2>Requests</h2><p>Every request from every process.</p></div>
                <Link to="/processes"><button>+ New request</button></Link>
            </div>

            <ErrorBox error={error} />

            <div className="filterbar">
                <div className="search">
                    <Icon name="search" size={16} />
                    <input placeholder="Search by title" value={q} onChange={(e) => setQ(e.target.value)} />
                </div>
                <div className="fchips">
                    {STATUSES.map((s) => (
                        <button key={s || "all"} className={`fchip${status === s ? " active" : ""}`}
                            onClick={() => { setStatus(s); setPage(1); }}>{s || "All"}</button>
                    ))}
                </div>
                <label className="inline">
                    <input type="checkbox" checked={mine} onChange={(e) => { setMine(e.target.checked); setPage(1); }} />
                    Started by me
                </label>
            </div>

            {data === null ? <p className="muted">Loading...</p> : <InstanceTable items={data.items} />}

            {data && data.total > data.pageSize && (
                <div className="toolbar">
                    <button className="secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
                    <span className="muted">Page {page} of {pages} · {data.total} requests</span>
                    <button className="secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
                </div>
            )}
        </>
    );
}