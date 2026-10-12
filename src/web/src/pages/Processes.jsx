import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api";
import { useUser } from "../user";
import { ErrorBox } from "../ui";

const SAMPLE = {
    key: "test-approval",
    name: "Test Approval",
    trigger: { type: "Manual" },
    steps: [
        {
            key: "details", name: "Request Details", type: "Form",
            fields: [
                { name: "destination", label: "Destination", type: "text", required: true },
                { name: "date", label: "Travel Date", type: "date", required: true },
            ],
        },
        { key: "approval", name: "Manager Approval", type: "Approval", approvers: ["manager@company.com"] },
        { key: "receipt", name: "Upload Confirmation", type: "Upload" },
    ],
};

export default function Processes() {
    const { isAdmin } = useUser();
    const nav = useNavigate();
    const [list, setList] = useState([]);
    const [error, setError] = useState(null);
    const [startKey, setStartKey] = useState(null);
    const [dataText, setDataText] = useState("{}");
    const [title, setTitle] = useState("");
    const [json, setJson] = useState(JSON.stringify(SAMPLE, null, 2));
    const [msg, setMsg] = useState("");

    const load = () => api.get("/api/definitions").then(setList).catch(setError);
    useEffect(() => { load(); }, []);

    const start = async () => {
        try {
            const data = dataText.trim() ? JSON.parse(dataText) : {};
            const inst = await api.post("/api/instances/start", { processKey: startKey, data, title: title || undefined });
            nav(`/instances/${inst.summary.id}`);
        } catch (e) { setError(e instanceof SyntaxError ? new Error("Data is not valid JSON.") : e); }
    };

    const create = async () => {
        setMsg("");
        try {
            const res = await api.post("/api/definitions", JSON.parse(json));
            setMsg(`Saved "${res.key}" as version ${res.version}.`);
            setError(null);
            load();
        } catch (e) { setError(e instanceof SyntaxError ? new Error("Definition is not valid JSON.") : e); }
    };

    const toggle = async (d) => {
        await api.patch(`/api/definitions/${d.key}/active?value=${!d.isActive}`);
        load();
    };

    return (
        <>
            <h2>Processes</h2>
            <ErrorBox error={error} />
            <table>
                <thead><tr><th>Name</th><th>Key</th><th>Version</th><th>Trigger</th><th>Steps</th><th>Active</th><th></th></tr></thead>
                <tbody>
                    {list.map((d) => (
                        <tr key={d.key}>
                            <td>{d.name}</td><td><code>{d.key}</code></td><td>v{d.version}</td>
                            <td>{d.definition.trigger.type}</td>
                            <td>{d.definition.steps.map((s) => s.name).join(" → ")}</td>
                            <td>
                                {isAdmin
                                    ? <button className="link" onClick={() => toggle(d)}>{d.isActive ? "Yes (disable)" : "No (enable)"}</button>
                                    : (d.isActive ? "Yes" : "No")}
                            </td>
                            <td><button className="link" onClick={() => toggle(d)}>{d.isActive ? "Yes (disable)" : "No (enable)"}</button></td>
                            <td><button disabled={!d.isActive} onClick={() => { setStartKey(d.key); setError(null); }}>Start</button></td>
                        </tr>
                    ))}
                </tbody>
            </table>

            {startKey && (
                <div className="panel">
                    <h3>Start “{startKey}”</h3>
                    <label><span>Title (optional)</span><input value={title} onChange={(e) => setTitle(e.target.value)} /></label>
                    <label>
                        <span>Starting data (JSON), e.g. {`{"empId": 1, "managerEmail": "boss@company.com"}`}</span>
                        <textarea rows={4} value={dataText} onChange={(e) => setDataText(e.target.value)} />
                    </label>
                    <button onClick={start}>Start request</button>{" "}
                    <button className="secondary" onClick={() => setStartKey(null)}>Cancel</button>
                </div>
            )}

            {isAdmin && (<div className="panel">
                <h3>Create or update a process (JSON)</h3>
                <p className="muted">Saving an existing key creates a new version. A visual designer comes later.</p>
                <textarea className="code" rows={18} value={json} onChange={(e) => setJson(e.target.value)} />
                <button onClick={create}>Save definition</button>
                {msg && <span className="ok"> {msg}</span>}
            </div>)}
        </>
    );
}