import { useState } from "react";
import { Link } from "react-router-dom";
import { downloadFile } from "./api";
import Icon from "./designer/Icon";

export const fmt = (d) => (d ? new Date(d).toLocaleString() : "");

export function Badge({ status }) {
    return <span className={`badge ${status}`}>{status}</span>;
}

export function ErrorBox({ error }) {
    if (!error) return null;
    return (
        <div className="error">
            <b>{error.message}</b>
            {error.errors?.length > 0 && (
                <ul>{error.errors.map((e, i) => <li key={i}>{e}</li>)}</ul>
            )}
        </div>
    );
}

/** The step-by-step progress: each step shown as completed / in progress / pending */
export function Stepper({ steps }) {
    const icon = {
        Completed: <Icon name="check" size={16} stroke={3} />,
        Failed: <Icon name="x" size={16} stroke={3} />,
        Rejected: <Icon name="x" size={16} stroke={3} />,
        InProgress: <Icon name="play" size={12} />,
        Pending: null,
    };
    return (
        <div className="stepper">
            {steps.map((s, i) => (
                <div key={s.id} className={`step ${s.status}`}>
                    <div className="dot">{icon[s.status] || i + 1}</div>
                    <div className="step-name">{s.name}</div>
                    <div className="step-type">{s.type}</div>
                    <div className="step-status">{s.status}</div>
                </div>
            ))}
        </div>
    );
}

export function FileLinks({ files }) {
    if (!files?.length) return null;
    return files.map((f) => (
        <div key={f.fileId}>
            <button className="link"
                onClick={() => downloadFile(f.fileId, f.fileName).catch((e) => alert(e.message))}>
                📎 {f.fileName || f.fileId}
            </button>
        </div>
    ));
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
    if (!items?.length) return <p className="muted">No requests yet.</p>;
    return (
        <table>
            <thead>
                <tr><th>Request</th><th>Process</th><th>Status</th><th>Progress</th><th>Current step</th><th>Created</th></tr>
            </thead>
            <tbody>
                {items.map((i) => (
                    <tr key={i.id}>
                        <td><Link to={`/instances/${i.id}`}>{i.title}</Link></td>
                        <td>{i.processName} v{i.processVersion}</td>
                        <td><Badge status={i.status} /></td>
                        <td style={{ minWidth: 120 }}>
                            <ProgressBar done={i.completedSteps} total={i.totalSteps} />
                            <small>{i.completedSteps}/{i.totalSteps}</small>
                        </td>
                        <td>{i.currentStep || "-"}</td>
                        <td>{fmt(i.createdAt)}</td>
                    </tr>
                ))}
            </tbody>
        </table>
    );
}

/** Builds a form from the field definitions in the process */
export function DynamicForm({ fields, allowUpload, onSubmit, busy, submitLabel = "Submit" }) {
    const [values, setValues] = useState(() =>
        Object.fromEntries(fields.map((f) => [f.name, f.type === "checkbox" ? false : ""]))
    );
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
                        <input
                            type={f.type === "number" ? "number" : f.type === "date" ? "date" : "text"}
                            step="any"
                            value={values[f.name]}
                            onChange={(e) => set(f.name, e.target.value)}
                        />
                    )}
                </label>
            ))}
            {allowUpload && (
                <label>
                    <span>Attachment</span>
                    <input type="file" onChange={(e) => setFile(e.target.files[0] || null)} />
                </label>
            )}
            <button disabled={busy}>{busy ? "Saving..." : submitLabel}</button>
        </form>
    );
}