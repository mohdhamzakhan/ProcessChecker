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
    const [list, setList] = useState(null);          // null = still loading
    const [error, setError] = useState(null);
    const [startKey, setStartKey] = useState(null);
    const [dataText, setDataText] = useState("{}");
    const [title, setTitle] = useState("");
    const [showEditor, setShowEditor] = useState(false);
    const [json, setJson] = useState(JSON.stringify(SAMPLE, null, 2));
    const [msg, setMsg] = useState("");

    const load = () =>
        api.get("/api/definitions")
            .then((d) => { setList(d); setError(null); })
            .catch((e) => { setList([]); setError(e); });
    useEffect(() => { load(); }, []);

    const start = async () => {
        try {
            const data = dataText.trim() ? JSON.parse(dataText) : {};
            const inst = await api.post("/api/instances/start", {
                processKey: startKey, data, title: title || undefined,
            });
            nav(`/instances/${inst.summary.id}`);
        } catch (e) { setError(e instanceof SyntaxError ? new Error("Starting data is not valid JSON.") : e); }
    };

    const create = async () => {
        setMsg("");
        try {
            const res = await api.post("/api/definitions", JSON.parse(json));
            setMsg(`Saved "${res.key}" as version ${res.version}.`);
            setError(null);
            load();
        } catch (e) { setError(e instanceof SyntaxError ? new Error("The definition is not valid JSON.") : e); }
    };

    const toggle = async (d) => {
        try { await api.patch(`/api/definitions/${d.key}/active?value=${!d.isActive}`); load(); }
        catch (e) { setError(e); }
    };

    return (
        <>
            <div className="page-head">
                <div>
                    <h2>Processes</h2>
                    <p>The workflows people can start. Each save of a key creates a new version.</p>
                </div>
                {isAdmin && (
                    <div className="actions">
                        <button onClick={() => nav("/designer")}>+ New process</button>
                        <button className="secondary" onClick={() => setShowEditor((v) => !v)}>
                            {showEditor ? "Close JSON" : "Advanced: JSON"}
                        </button>
                    </div>
                )}
            </div>

            <ErrorBox error={error} />

            {list === null && <p className="muted">Loading...</p>}

            {list !== null && list.length === 0 && !error && (
                <div className="empty">
                    <b>No processes yet</b>
                    {isAdmin
                        ? "Click “New process”, then “Save definition” to add the sample."
                        : "Ask an administrator to create one."}
                </div>
            )}

            {list?.length > 0 && (
                <div className="scroll">
                    <table>
                        <thead>
                            <tr><th>Process</th><th>Version</th><th>Trigger</th><th>Steps</th><th>Status</th><th></th></tr>
                        </thead>
                        <tbody>
                            {list.map((d) => (
                                <tr key={d.key}>
                                    <td><b>{d.name}</b><div><code>{d.key}</code></div></td>
                                    <td>v{d.version}</td>
                                    <td>{d.definition.trigger.type === "DatabasePoll" ? "Checks a database" : "Manual"}</td>
                                    <td>
                                        <div className="chips">
                                            {d.definition.steps.map((s, i) => (
                                                <span key={s.key} className="chips">
                                                    {i > 0 && <span className="chip-sep">→</span>}
                                                    <span className="chip">{s.name}</span>
                                                </span>
                                            ))}
                                        </div>
                                    </td>
                                    <td>
                                        <span className={`badge ${d.isActive ? "Completed" : "Cancelled"}`}>
                                            {d.isActive ? "Active" : "Disabled"}
                                        </span>
                                    </td>
                                    <td>
                                        <div className="actions">
                                            <button disabled={!d.isActive}
                                                onClick={() => { setStartKey(d.key); setError(null); }}>Start</button>
                                            {isAdmin && (
                                                <button className="secondary" onClick={() => toggle(d)}>
                                                    {d.isActive ? "Disable" : "Enable"}
                                                </button>
                                            )}
                                            {isAdmin && (
                                                <button className="secondary" onClick={() => nav(`/designer/${d.key}`)}>Edit</button>
                                            )}
                                        </div>
                                    </td>
                                </tr>
                            ))}
                        </tbody>
                    </table>
                </div>
            )}

            {startKey && (
                <div className="panel">
                    <h3>Start “{startKey}”</h3>
                    <div className="form">
                        <label><span>Title (optional)</span>
                            <input value={title} onChange={(e) => setTitle(e.target.value)} /></label>
                        <label>
                            <span>Starting data (JSON, optional)</span>
                            <textarea rows={3} placeholder={`{"empId": 1, "managerEmail": "boss@company.com"}`}
                                value={dataText} onChange={(e) => setDataText(e.target.value)} />
                        </label>
                        <div className="actions">
                            <button onClick={start}>Start request</button>
                            <button className="secondary" onClick={() => setStartKey(null)}>Cancel</button>
                        </div>
                    </div>
                </div>
            )}

            {isAdmin && showEditor && (
                <div className="panel">
                    <h3>Create or update a process</h3>
                    <p className="muted">Paste a definition as JSON. A visual designer is planned.</p>
                    <textarea className="code" rows={18} value={json} onChange={(e) => setJson(e.target.value)} />
                    <div className="toolbar">
                        <button onClick={create}>Save definition</button>
                        <button className="secondary" onClick={() => setJson(JSON.stringify(SAMPLE, null, 2))}>Load sample</button>
                        {msg && <span className="ok">{msg}</span>}
                    </div>
                </div>
            )}
        </>
    );
}