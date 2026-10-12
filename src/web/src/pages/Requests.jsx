import { useEffect, useState } from "react";
import { api } from "../api";
import { ErrorBox, InstanceTable } from "../ui";

export default function Requests() {
    const [status, setStatus] = useState("");
    const [q, setQ] = useState("");
    const [page, setPage] = useState(1);
    const [data, setData] = useState({ items: [], total: 0, pageSize: 25 });
    const [error, setError] = useState(null);

    useEffect(() => {
        const qs = new URLSearchParams({ page });
        if (status) qs.set("status", status);
        if (q) qs.set("q", q);
        api.get(`/api/instances?${qs}`).then((d) => { setData(d); setError(null); }).catch(setError);
    }, [status, q, page]);

    const pages = Math.max(1, Math.ceil(data.total / data.pageSize));

    return (
        <>
            <h2>Requests</h2>
            <ErrorBox error={error} />
            <div className="toolbar">
                <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}>
                    <option value="">All statuses</option>
                    {["Running", "Completed", "Rejected", "Failed", "Cancelled"].map((s) => <option key={s}>{s}</option>)}
                </select>
                <input placeholder="Search title" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} />
                <span className="muted">{data.total} request(s)</span>
            </div>
            <InstanceTable items={data.items} />
            <div className="toolbar">
                <button disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
                <span>Page {page} of {pages}</span>
                <button disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
            </div>
        </>
    );
}