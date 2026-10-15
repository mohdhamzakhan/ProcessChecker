import { useState } from "react";
import { Link } from "react-router-dom";
import { downloadFile } from "./api";
import Icon from "./designer/Icon";
import Viewer from "./Viewer";
import { useDialogs } from "./Dialogs.jsx"

// the server sends UTC times without a "Z", so add it before the browser reads them
const toDate = (d) => {
    if (!d) return null;
    const s = String(d);
    return new Date(/[zZ]$|[+-]\d\d:?\d\d$/.test(s) ? s : s + "Z");
};
export const fmt = (d) => (d ? toDate(d).toLocaleString() : "");

export function ago(d) {
    const dt = toDate(d);
    if (!dt) return "";
    const s = Math.max(0, Math.floor((Date.now() - dt.getTime()) / 1000));
    if (s < 60) return "just now";
    const m = Math.floor(s / 60);
    if (m < 60) return `${m} min ago`;
    const h = Math.floor(m / 60);
    if (h < 24) return `${h} h ago`;
    const days = Math.floor(h / 24);
    return days === 1 ? "yesterday" : `${days} days ago`;
}

export function Badge({ status }) {
    return <span className={`badge ${status}`}>{status}</span>;
}

export function ErrorBox({ error }) {
    if (!error) return null;
    return (
        <div className="error">
            <b>{error.message}</b>
            {error.errors?.length > 0 && <ul>{error.errors.map((e, i) => <li key={i}>{e}</li>)}</ul>}
        </div>
    );
}

/** Each step shown as completed / in progress / pending */
export function Stepper({ steps }) {
    const icon = {
        Completed: <Icon name="check" size={16} stroke={3} />,
        Failed: <Icon name="x" size={16} stroke={3} />,
        Rejected: <Icon name="x" size={16} stroke={3} />,
        InProgress: <Icon name="play" size={12} />,
        Pending: null,
        Skipped: "–",
    };
    return (
        <div className="stepper">
            {steps.map((s, i) => (
                <div key={s.id} className={`step ${s.status}`}>
                    <div className="dot">{icon[s.status] ?? i + 1}</div>
                    <div className="step-name">{s.name}</div>
                    <div className="step-type">{s.type}</div>
                    <div className="step-status">{s.status}</div>
                </div>
            ))}
        </div>
    );
}

export function ProgressBar({ done, total }) {
    const pct = total ? Math.round((done / total) * 100) : 0;
    return (
        <div className="progress" title={`${done} of ${total} steps`}>
            <div style={{ width: `${pct}%` }} />
        </div>
    );
}

export function InstanceTable({ items }) {
    if (!items?.length)
        return <div className="empty"><b>No requests yet</b>They will appear here as soon as one is started.</div>;

    return (
        <div className="scroll">
            <table>
                <thead>
                    <tr><th>Request</th><th>Process</th><th>Status</th><th>Progress</th><th>Current step</th><th>Started</th></tr>
                </thead>
                <tbody>
                    {items.map((i) => (
                        <tr key={i.id}>
                            <td>
                                <Link to={`/instances/${i.id}`}><b>{i.title}</b></Link>
                                {i.startedBy && <div className="muted small">by {i.startedBy}</div>}
                            </td>
                            <td>{i.processName} <span className="muted small">v{i.processVersion}</span></td>
                            <td><Badge status={i.status} /></td>
                            <td style={{ minWidth: 130 }}>
                                <ProgressBar done={i.completedSteps} total={i.totalSteps} />
                                <small className="muted">{i.completedSteps} of {i.totalSteps} steps</small>
                            </td>
                            <td>{i.currentStep || "–"}</td>
                            <td title={fmt(i.createdAt)}>{ago(i.createdAt)}</td>
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}

/** Builds a form from the field definitions in the process */
export function DynamicForm({ fields, allowUpload, onSubmit, busy, submitLabel = "Submit" }) {
    const [values, setValues] = useState(() =>
        Object.fromEntries(fields.map((f) => [f.name, f.type === "checkbox" ? false : ""])));
    const [file, setFile] = useState(null);
    const set = (name, v) => setValues((s) => ({ ...s, [name]: v }));

    const submit = (e) => {
        e.preventDefault();
        onSubmit(values, file);
    };

    return (
        <form onSubmit={submit} className="form">
            {fields.map((f) => (
                <label key={f.name}>
                    <span>{f.label || f.name}{f.required && " *"}</span>
                    {f.type === "textarea" ? (
                        <textarea rows={3} value={values[f.name]} onChange={(e) => set(f.name, e.target.value)} />
                    ) : f.type === "select" ? (
                        <select value={values[f.name]} onChange={(e) => set(f.name, e.target.value)}>
                            <option value="">-- select --</option>
                            {f.options.map((o) => <option key={o} value={o}>{o}</option>)}
                        </select>
                    ) : f.type === "checkbox" ? (
                        <input type="checkbox" checked={values[f.name]} onChange={(e) => set(f.name, e.target.checked)} />
                    ) : (
                        <input type={f.type === "number" ? "number" : f.type === "date" ? "date" : "text"} step="any"
                            value={values[f.name]} onChange={(e) => set(f.name, e.target.value)} />
                    )}
                </label>
            ))}
            {allowUpload && (
                <label><span>Attachment</span>
                    <input type="file" onChange={(e) => setFile(e.target.files[0] || null)} /></label>
            )}
            <button disabled={busy}>{busy ? "Saving..." : submitLabel}</button>
        </form>
    );
}

/** Uploaded files, each with View and Download */
export function FileLinks({ files }) {

    const [view, setView] = useState(null);
    const { alert } = useDialogs();
    if (!files?.length) return null;
    return (
        <>
            {files.map((f) => (
                <div key={f.fileId} className="filerow">
                    <Icon name="form" size={16} />
                    <span className="fname" title={f.fileName}>{f.fileName || f.fileId}</span>
                    {f.step && <small className="muted">{f.step}</small>}
                    <button className="link" type="button"
                        onClick={() => setView({ kind: "file", id: f.fileId, name: f.fileName })}>View</button>
                    <button className="link" type="button"
                        onClick={() => downloadFile(f.fileId, f.fileName).catch((e) => useDialogs(e.message))}>Download</button>
                </div>
            ))}
            {view && <Viewer source={view} onClose={() => setView(null)} />}
        </>
    );
}

/** A link to a document on another server, with an in-page viewer */
export function SafeLink({ url, label, instanceId, index }) {
    const [view, setView] = useState(false);
    const ok = /^https?:\/\//i.test(url || "");
    const canView = ok && instanceId && Number.isInteger(index);
    return (
        <div className="linkrow">
            <Icon name="link" size={16} />
            {ok
                ? <a href={url} target="_blank" rel="noopener noreferrer" title={url}>{label || url}</a>
                : <span>{label || url}</span>}
            {canView && <button className="secondary" type="button" onClick={() => setView(true)}>View</button>}
            <button className="secondary" type="button" onClick={() => navigator.clipboard?.writeText(url)}>Copy</button>
            {view && <Viewer source={{ kind: "link", instanceId, index, url, label }} onClose={() => setView(false)} />}
        </div>
    );
}
export const isOverdue = (d) => !!d && toDate(d).getTime() < Date.now();

export function DueTag({ dueAt }) {
    if (!dueAt) return null;
    const ms = toDate(dueAt).getTime() - Date.now();
    const h = Math.round(Math.abs(ms) / 3600000);
    const text = h < 1 ? "less than an hour" : h < 48 ? `${h} h` : `${Math.round(h / 24)} days`;
    const cls = ms < 0 ? "due over" : ms < 86400000 ? "due soon" : "due";
    return <span className={cls} title={fmt(dueAt)}>{ms < 0 ? `Overdue by ${text}` : `Due in ${text}`}</span>;
}