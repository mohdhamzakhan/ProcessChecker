import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api";
import { useUser } from "../user";
import { Modal, useDialogs } from "../Dialogs";
import Icon from "../designer/Icon";
import { STEP_TYPES } from "../designer/model";
import { ErrorBox } from "../ui";

export default function Processes() {
    const { isAdmin } = useUser();
    const { alert } = useDialogs();
    const nav = useNavigate();
    const fileRef = useRef(null);

    const [list, setList] = useState(null);
    const [error, setError] = useState(null);
    const [q, setQ] = useState("");
    const [starting, setStarting] = useState(null);

    const load = () => {
        api.get("/api/definitions")
            .then((d) => { setList(d); setError(null); })
            .catch((e) => { setList([]); setError(e); });
    };

    useEffect(() => { load(); }, []);

    const importFile = async (e) => {
        const file = e.target.files?.[0];
        e.target.value = "";
        if (!file) return;
        try {
            const def = JSON.parse(await file.text());
            const res = await api.post("/api/definitions", def);
            setError(null);
            await load();
            await alert(`Imported “${res.name}” as version ${res.version}.`, { title: "Imported" });
        } catch (err) {
            setError(err instanceof SyntaxError ? new Error("That file is not valid JSON.") : err);
        }
    };

    const ql = q.trim().toLowerCase();
    const visible = (list || [])
        .filter((d) => isAdmin || d.isActive)
        .filter((d) => !ql || d.name.toLowerCase().includes(ql) || d.key.includes(ql));

    return (
        <>
            <div className="page-head">
                <div>
                    <h2>Processes</h2>
                    <p>Pick a process to start a new request.</p>
                </div>
                {isAdmin && (
                    <div className="actions">
                        <button onClick={() => nav("/designer")}>+ New process</button>
                        <button className="secondary" onClick={() => fileRef.current?.click()}>Import JSON</button>
                        <input ref={fileRef} type="file" accept=".json,application/json" hidden onChange={importFile} />
                    </div>
                )}
            </div>

            <ErrorBox error={error} />

            <div className="filterbar">
                <div className="search">
                    <Icon name="search" size={16} />
                    <input placeholder="Search processes" value={q} onChange={(e) => setQ(e.target.value)} />
                </div>
            </div>

            {list === null && <p className="muted">Loading...</p>}
            {list !== null && visible.length === 0 && !error && (
                <div className="empty">
                    <b>No processes found</b>
                    {isAdmin ? "Click “+ New process” or “Import JSON”." : "Ask an administrator to create one."}
                </div>
            )}

            <div className="pgrid">
                {visible.map((d) => (
                    <ProcessCard
                        key={d.key}
                        d={d}
                        isAdmin={isAdmin}
                        onStart={() => setStarting(d)}
                        onReload={load}
                        onError={setError}
                    />
                ))}
            </div>

            {starting && (
                <StartModal
                    starting={starting}
                    onClose={() => setStarting(null)}
                />
            )}
        </>
    );
}

// ----------------------------------------------------------------------
// Extracted Card Component (Using exact original class structure)
// ----------------------------------------------------------------------
function ProcessCard({ d, isAdmin, onStart, onReload, onError }) {
    const nav = useNavigate();
    const { alert } = useDialogs();
    const [checking, setChecking] = useState(null);

    const steps = d.definition.steps;
    const auto = d.definition.trigger.type === "DatabasePoll";

    const toggle = async () => {
        try {
            await api.patch(`/api/definitions/${d.key}/active?value=${!d.isActive}`);
            onReload();
        } catch (e) { onError(e); }
    };

    const checkNow = async () => {
        setChecking(d.key);
        try {
            const r = await api.post(`/api/poll/${encodeURIComponent(d.key)}/run-now`);
            let text = `Found ${r.found} row(s): started ${r.created} new request(s), skipped ${r.skipped} already handled.`;
            if (r.errors?.length) text += `\n\nProblems:\n${r.errors.join("\n")}`;
            await alert(text, { title: "Database check" });
            onError(null);
        } catch (e) { onError(e); } finally { setChecking(null); }
    };

    return (
        <div className={`pcard${d.isActive ? "" : " off"}`}>
            <div className="phd">
                <span className="pbadge" style={{ "--c": "#4f46e5" }}><Icon name="workflow" size={20} /></span>
                <div className="ptitle">
                    <b>{d.name}</b>
                    <small>v{d.version} · {auto ? "Also starts automatically" : "Started by a person"}</small>
                </div>
                {!d.isActive && <span className="badge Cancelled">Disabled</span>}
            </div>

            <p className="pdesc">{d.definition.description || "No description."}</p>

            <div className="pflow">
                {steps.slice(0, 6).map((s, i) => {
                    const m = STEP_TYPES[s.type] || STEP_TYPES.Form;
                    return (
                        <span key={s.key} className="pstep" style={{ "--c": m.color }} title={`${i + 1}. ${s.name}`}>
                            <Icon name={m.icon} size={14} />
                        </span>
                    );
                })}
                {steps.length > 6 && <small className="muted">+{steps.length - 6}</small>}
                <small className="muted" style={{ marginLeft: "auto" }}>{steps.length} step{steps.length === 1 ? "" : "s"}</small>
            </div>

            <div className="pfoot">
                <button disabled={!d.isActive} onClick={onStart}>Start</button>
                {isAdmin && auto && d.isActive && (
                    <button className="secondary" disabled={checking === d.key} onClick={checkNow}>
                        {checking === d.key ? "Checking..." : "Check now"}
                    </button>
                )}
                {isAdmin && (
                    <>
                        <button className="secondary" onClick={() => nav(`/designer/${d.key}`)}>Edit</button>
                        <button className="secondary" onClick={toggle}>{d.isActive ? "Disable" : "Enable"}</button>
                    </>
                )}
            </div>
        </div>
    );
}

// ----------------------------------------------------------------------
// Extracted Start Modal Component
// ----------------------------------------------------------------------
function StartModal({ starting, onClose }) {
    const nav = useNavigate();
    const [title, setTitle] = useState("");
    const [dataText, setDataText] = useState("");
    const [busy, setBusy] = useState(false);
    const [startError, setStartError] = useState(null);

    const start = async () => {
        setBusy(true); setStartError(null);
        try {
            const data = dataText.trim() ? JSON.parse(dataText) : {};
            const inst = await api.post("/api/instances/start", {
                processKey: starting.key, data, title: title.trim() || undefined,
            });
            nav(`/instances/${inst.summary.id}`);
        } catch (e) {
            setStartError(e instanceof SyntaxError ? new Error("The starting data is not valid JSON.") : e);
        } finally { setBusy(false); }
    };

    return (
        <Modal title={`Start “${starting.name}”`} onClose={onClose}
            footer={
                <>
                    <button className="secondary" onClick={onClose}>Cancel</button>
                    <button disabled={busy} onClick={start}>{busy ? "Starting..." : "Start request"}</button>
                </>
            }>
            <ErrorBox error={startError} />
            <div className="form" style={{ maxWidth: "none" }}>
                <label><span>Title (optional)</span>
                    <input autoFocus value={title} placeholder="Leave empty to use the process name and time"
                        onChange={(e) => setTitle(e.target.value)} /></label>
                <details>
                    <summary>Advanced: starting data</summary>
                    <textarea rows={3} className="code" style={{ marginTop: 8 }}
                        placeholder={`{"id": 41}`} value={dataText} onChange={(e) => setDataText(e.target.value)} />
                    <p className="muted small">Values the steps can use. Leave empty if the first step collects what it needs.</p>
                </details>
            </div>
        </Modal>
    );
}