import { useEffect, useState } from "react";
import { api } from "../api";
import { ErrorBox, InstanceTable } from "../ui";

const ORDER = ["Running", "Completed", "Rejected", "Failed", "Cancelled"];

export default function Dashboard() {
    const [stats, setStats] = useState(null);
    const [recent, setRecent] = useState([]);
    const [error, setError] = useState(null);

    const load = async () => {
        try {
            const [s, r] = await Promise.all([
                api.get("/api/dashboard"),
                api.get("/api/instances?pageSize=8"),
            ]);
            setStats(s);
            setRecent(r.items);
            setError(null);
        } catch (e) { setError(e); }
    };

    useEffect(() => {
        load();
        const t = setInterval(load, 15000);   // refresh every 15 seconds
        return () => clearInterval(t);
    }, []);

    return (
        <>
            <h2>Dashboard</h2>
            <ErrorBox error={error} />
            {stats && (
                <>
                    <div className="cards">
                        <div className="card"><div className="big">{stats.total}</div>Total requests</div>
                        {ORDER.map((s) => (
                            <div key={s} className={`card ${s}`}>
                                <div className="big">{stats.byStatus[s] || 0}</div>{s}
                            </div>
                        ))}
                    </div>

                    <h3>By process</h3>
                    <table>
                        <thead><tr><th>Process</th><th>Total</th>{ORDER.map((s) => <th key={s}>{s}</th>)}</tr></thead>
                        <tbody>
                            {stats.byProcess.map((p) => (
                                <tr key={p.processKey}>
                                    <td>{p.processName}</td><td>{p.total}</td>
                                    {ORDER.map((s) => <td key={s}>{p.byStatus[s] || 0}</td>)}
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </>
            )}
            <h3>Recent requests</h3>
            <InstanceTable items={recent} />
        </>
    );
}