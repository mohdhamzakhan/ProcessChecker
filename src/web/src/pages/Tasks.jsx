import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import { useUser } from "../user";
import { ErrorBox, fmt } from "../ui";

function TaskTable({ items }) {
    if (!items.length) return <p className="muted">Nothing here.</p>;
    return (
        <table>
            <thead><tr><th>Request</th><th>Process</th><th>Step</th><th>Waiting since</th></tr></thead>
            <tbody>
                {items.map((t) => (
                    <tr key={t.instanceId + t.stepKey}>
                        <td><Link to={`/instances/${t.instanceId}`}>{t.title}</Link></td>
                        <td>{t.processName}</td>
                        <td>{t.stepName} <small>({t.stepType})</small></td>
                        <td>{fmt(t.since)}</td>
                    </tr>
                ))}
            </tbody>
        </table>
    );
}

export default function Tasks() {
    const { user } = useUser();
    const [data, setData] = useState({ approvals: [], open: [] });
    const [error, setError] = useState(null);

    useEffect(() => {
        api.get(`/api/tasks?user=${encodeURIComponent(user)}`)
            .then((d) => { setData(d); setError(null); })
            .catch(setError);
    }, [user]);

    return (
        <>
            <h2>My Tasks</h2>
            <ErrorBox error={error} />
            <h3>Waiting for my approval</h3>
            {user ? <TaskTable items={data.approvals} /> : <p className="muted">Enter your email at the top right.</p>}
            <h3>Forms and uploads waiting for input</h3>
            <TaskTable items={data.open} />
        </>
    );
}