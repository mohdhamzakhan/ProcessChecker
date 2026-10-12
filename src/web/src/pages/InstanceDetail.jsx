import { useCallback, useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { api, uploadDocument } from "../api";
import { useUser } from "../user";
import { Badge, DynamicForm, ErrorBox, FileLinks, Stepper, fmt } from "../ui";

function StepAction({ detail, step, user, onDone }) {
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);
    const [note, setNote] = useState("");
    const [file, setFile] = useState(null);
    const id = detail.summary.id;
    const base = `/api/instances/${id}/steps/${step.key}`;

    const run = async (fn) => {
        setBusy(true); setError(null);
        try { onDone(await fn()); } catch (e) { setError(e); } finally { setBusy(false); }
    };
    // upload to DocumentService first, then give the step the stored file id
    const withFile = async (f) =>
        f ? [await uploadDocument(f, { instanceId: id, stepKey: step.key })] : undefined;

    if (step.status === "Failed") {
        return (
            <div className="panel">
                <h3>{step.name} failed</h3>
                <ErrorBox error={{ message: step.error }} />
                <ErrorBox error={error} />
                <button disabled={busy} onClick={() => run(() => api.post(`${base}/retry`))}>Retry this step</button>
            </div>
        );
    }
    if (step.status !== "InProgress") return null;

    const header = <h3>Action needed: {step.name}</h3>;

    if (step.type === "Approval") {
        const me = step.approvals?.find((a) => a.approver === user.toLowerCase());
        return (
            <div className="panel">
                {header}
                <p>Approvers: {step.approvals?.map((a) => a.approver).join(", ")}</p>
                {!me && <p className="muted">You are not an approver for this step.</p>}
                {me && !me.decision && (
                    <>
                        <label><span>Note (optional)</span>
                            <input value={note} onChange={(e) => setNote(e.target.value)} /></label>
                        <ErrorBox error={error} />
                        <button disabled={busy} onClick={() => run(() => api.post(`${base}/approve`, { note }))}>Approve</button>{" "}
                        <button disabled={busy} className="danger" onClick={() => run(() => api.post(`${base}/reject`, { note }))}>Reject</button>
                    </>
                )}
            </div>
        );
    }

    if (step.type === "Form") {
        return (
            <div className="panel">
                {header}
                <ErrorBox error={error} />
                <DynamicForm
                    fields={step.fields || []}
                    allowUpload={step.allowUpload}
                    busy={busy}
                    onSubmit={(values, f) =>
                        run(async () => api.post(`${base}/submit`, { values, files: await withFile(f) }))}
                />
            </div>
        );
    }

    if (step.type === "Upload") {
        return (
            <div className="panel">
                {header}
                <p className="muted">Allowed: PDF, images, Word, Excel, CSV, TXT. Maximum 20 MB.</p>
                <input type="file" onChange={(e) => setFile(e.target.files[0] || null)} />
                <ErrorBox error={error} />
                <p>
                    <button disabled={busy || !file}
                        onClick={() => run(async () => api.post(`${base}/upload`, { files: await withFile(file) }))}>
                        {busy ? "Uploading..." : "Upload confirmation"}
                    </button>
                </p>
            </div>
        );
    }
    return null;
}

export default function InstanceDetail() {
    const { id } = useParams();
    const { user } = useUser();
    const [detail, setDetail] = useState(null);
    const [error, setError] = useState(null);

    const load = useCallback(() =>
        api.get(`/api/instances/${id}`).then((d) => { setDetail(d); setError(null); }).catch(setError), [id]);

    useEffect(() => { load(); }, [load]);

    if (!detail) return <><ErrorBox error={error} /><p className="muted">Loading...</p></>;

    const s = detail.summary;
    const active = detail.steps.find((x) => x.status === "InProgress" || x.status === "Failed");
    const canCancel = s.status === "Running" || s.status === "Failed";

    const cancel = async () => {
        if (!confirm("Cancel this request?")) return;
        try { await api.post(`/api/instances/${id}/cancel`); load(); } catch (e) { setError(e); }
    };

    return (
        <>
            <h2>{s.title} <Badge status={s.status} /></h2>
            <p className="muted">
                {s.processName} v{s.processVersion} · started {fmt(s.createdAt)}
                {s.startedBy && ` by ${s.startedBy}`}
                {s.completedAt && ` · finished ${fmt(s.completedAt)}`}
            </p>
            <ErrorBox error={error} />

            <Stepper steps={detail.steps} />

            {active && (
                <StepAction key={active.id + active.status} detail={detail} step={active} user={user}
                    onDone={(d) => setDetail(d)} />
            )}
            {canCancel && <button className="secondary" onClick={cancel}>Cancel request</button>}

            <h3>Step history</h3>
            <table>
                <thead><tr><th>#</th><th>Step</th><th>Type</th><th>Status</th><th>By</th><th>Started</th><th>Completed</th><th>Details</th></tr></thead>
                <tbody>
                    {detail.steps.map((st) => (
                        <tr key={st.id}>
                            <td>{st.order + 1}</td><td>{st.name}</td><td>{st.type}</td>
                            <td><Badge status={st.status} /></td>
                            <td>{st.actionBy || ""}</td><td>{fmt(st.startedAt)}</td><td>{fmt(st.completedAt)}</td>
                            <td>
                                {st.approvals && (
                                    <div>{st.approvals.map((a) => (
                                        <div key={a.approver}><small>{a.approver}: {a.decision || "waiting"}{a.note && ` (${a.note})`}</small></div>
                                    ))}</div>
                                )}
                                <FileLinks files={st.output?.files} />
                                {st.error && <div className="ok-err">{st.error}</div>}
                                {st.output && (
                                    <details><summary>output</summary><pre>{JSON.stringify(st.output, null, 2)}</pre></details>
                                )}
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>

            <details>
                <summary>Collected data</summary>
                <table>
                    <tbody>
                        {Object.entries(detail.data).sort().map(([k, v]) => (
                            <tr key={k}><td><code>{k}</code></td><td>{typeof v === "object" ? JSON.stringify(v) : String(v)}</td></tr>
                        ))}
                    </tbody>
                </table>
            </details>
        </>
    );
}