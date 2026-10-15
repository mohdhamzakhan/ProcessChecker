import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api, uploadDocument } from "../api";
import { useUser } from "../user";
import { useDialogs } from "../Dialogs";
import { buildLink } from "../links";
import Icon from "../designer/Icon";
import UserPicker from "../UserPicker";
import { Badge, DueTag, DynamicForm, ErrorBox, FileLinks, SafeLink, Stepper, fmt } from "../ui";

function AssignBar({ detail, step, isAdmin, onDone }) {
    const [edit, setEdit] = useState(false);
    const [list, setList] = useState([]);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    const isApproval = step.type === "Approval";
    const current = isApproval
        ? (step.approvals || []).filter((a) => a.decision !== "Delegated").map((a) => a.approver)
        : step.assignees || [];

    const save = async () => {
        setBusy(true); setError(null);
        try {
            onDone(await api.post(`/api/instances/${detail.summary.id}/steps/${step.key}/assign`, { assignees: list }));
            setEdit(false);
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    return (
        <div className="assignbar">
            <Icon name="users" size={16} />
            <div className="assignwho">
                <small>{isApproval ? "Approvers" : "Assigned to"}</small>
                <b>{current.length ? current.join(", ") : "Nobody (the person who started it, or an admin)"}</b>
            </div>
            {isAdmin && !edit && (
                <button className="secondary" type="button" onClick={() => { setList(current); setEdit(true); }}>Change</button>
            )}
            {edit && (
                <div className="assignedit">
                    <UserPicker value={list} onChange={setList} />
                    <ErrorBox error={error} />
                    <div className="actions" style={{ marginTop: 8 }}>
                        <button disabled={busy || list.length === 0} onClick={save}>{busy ? "Saving..." : "Save and notify"}</button>
                        <button className="secondary" onClick={() => setEdit(false)}>Cancel</button>
                    </div>
                </div>
            )}
        </div>
    );
}

function ObserversBar({ detail, canEdit, onDone }) {
    const [edit, setEdit] = useState(false);
    const [list, setList] = useState([]);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);
    const current = detail.observers || [];

    const save = async () => {
        setBusy(true); setError(null);
        try {
            onDone(await api.post(`/api/instances/${detail.summary.id}/observers`, { observers: list }));
            setEdit(false);
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    return (
        <div className="assignbar">
            <Icon name="eye" size={16} />
            <div className="assignwho">
                <small>Observers (can follow, cannot act)</small>
                <b>{current.length ? current.join(", ") : "None"}</b>
            </div>
            {canEdit && !edit && (
                <button className="secondary" type="button" onClick={() => { setList(current); setEdit(true); }}>Change</button>
            )}
            {edit && (
                <div className="assignedit">
                    <UserPicker value={list} onChange={setList} />
                    <ErrorBox error={error} />
                    <div className="actions" style={{ marginTop: 8 }}>
                        <button disabled={busy} onClick={save}>{busy ? "Saving..." : "Save"}</button>
                        <button className="secondary" onClick={() => setEdit(false)}>Cancel</button>
                    </div>
                </div>
            )}
        </div>
    );
}

/** A person who holds a waiting task passes it to someone else. */
function HandOver({ detail, step, onDone }) {
    const [open, setOpen] = useState(false);
    const [to, setTo] = useState([]);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    const send = async () => {
        setBusy(true); setError(null);
        try {
            onDone(await api.post(`/api/instances/${detail.summary.id}/steps/${step.key}/delegate`, { to: to[0] }));
            setOpen(false);
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    if (!open)
        return (
            <button type="button" className="secondary" onClick={() => setOpen(true)}>
                <Icon name="users" size={15} /> Hand over
            </button>
        );

    return (
        <div className="assignedit" style={{ flexBasis: "100%" }}>
            <p className="muted small" style={{ margin: "0 0 6px" }}>Pick the person who should do this instead. They get an email.</p>
            <UserPicker value={to} onChange={(v) => setTo(v.slice(-1))} placeholder="Search a person…" />
            <ErrorBox error={error} />
            <div className="actions" style={{ marginTop: 8 }}>
                <button type="button" disabled={busy || to.length === 0} onClick={send}>{busy ? "Handing over..." : "Hand over"}</button>
                <button type="button" className="secondary" onClick={() => setOpen(false)}>Cancel</button>
            </div>
        </div>
    );
}

function StepAction({ detail, step, user, ids, isAdmin, isStarter, canWork, onDone }) {
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
    const withFile = async (f) =>
        f ? [await uploadDocument(f, { instanceId: id, stepKey: step.key })] : undefined;

    if (step.status === "Failed") {
        return (
            <div className="panel">
                <h3>{step.name} failed</h3>
                <ErrorBox error={{ message: step.error }} />
                <ErrorBox error={error} />
                {canWork
                    ? <button disabled={busy} onClick={() => run(() => api.post(`${base}/retry`))}>Retry this step</button>
                    : <p className="muted">Only the people working on this request can retry it.</p>}
            </div>
        );
    }
    if (step.status !== "InProgress") return null;

    const bar = <AssignBar detail={detail} step={step} isAdmin={isAdmin} onDone={onDone} />;
    const header = <h3>Action needed: {step.name} <DueTag dueAt={step.dueAt} /></h3>;
    const assigned = step.assignees || [];
    const mineAssigned = assigned.some((a) => ids.includes(a));
    const canAct = assigned.length > 0 ? isAdmin || mineAssigned : isAdmin || isStarter;
    const notYours = <p className="muted">This task is for someone else. Only they, or an admin, can complete it.</p>;

    if (step.type === "Approval") {
        const live = (step.approvals || []).filter((a) => a.decision !== "Delegated");
        const approved = live.filter((a) => a.decision === "Approved").length;
        const mine = (step.approvals || []).find((a) => !a.decision && ids.includes(a.approver));
        const decided = (step.approvals || []).some((a) => a.decidedBy && ids.includes(a.decidedBy));
        return (
            <div className="panel">
                {header}
                {bar}
                {live.length > 1 && <p className="muted small">{approved} of {live.length} approved</p>}
                {!mine && !decided && <p className="muted">You are not an approver for this step.</p>}
                {decided && <p className="muted">You have already decided on this step.</p>}
                {mine && !decided && (
                    <>
                        <label><span>Note (optional)</span>
                            <input value={note} onChange={(e) => setNote(e.target.value)} /></label>
                        <ErrorBox error={error} />
                        <div className="actions" style={{ marginTop: 10 }}>
                            <button disabled={busy} onClick={() => run(() => api.post(`${base}/approve`, { note }))}>Approve</button>
                            <button disabled={busy} className="danger" onClick={() => run(() => api.post(`${base}/reject`, { note }))}>Reject</button>
                            <HandOver detail={detail} step={step} onDone={onDone} />
                        </div>
                    </>
                )}
            </div>
        );
    }

    if (step.type === "Form") {
        return (
            <div className="panel">
                {header}
                {bar}
                {!canAct ? notYours : (
                    <>
                        {mineAssigned && <div style={{ margin: "8px 0" }}><HandOver detail={detail} step={step} onDone={onDone} /></div>}
                        <ErrorBox error={error} />
                        <DynamicForm fields={step.fields || []} allowUpload={step.allowUpload} busy={busy}
                            onSubmit={(values, f) =>
                                run(async () => api.post(`${base}/submit`, { values, files: await withFile(f) }))} />
                    </>
                )}
            </div>
        );
    }

    if (step.type === "Upload") {
        return (
            <div className="panel">
                {header}
                {bar}
                {!canAct ? notYours : (
                    <>
                        {mineAssigned && <div style={{ margin: "8px 0" }}><HandOver detail={detail} step={step} onDone={onDone} /></div>}
                        <p className="muted">Allowed: PDF, images, Word, Excel, CSV, TXT. Maximum 20 MB.</p>
                        <input type="file" onChange={(e) => setFile(e.target.files[0] || null)} />
                        <ErrorBox error={error} />
                        <p>
                            <button disabled={busy || !file}
                                onClick={() => run(async () => api.post(`${base}/upload`, { files: await withFile(file) }))}>
                                {busy ? "Uploading..." : "Upload confirmation"}
                            </button>
                        </p>
                    </>
                )}
            </div>
        );
    }
    return null;
}

export default function InstanceDetail() {
    const { id } = useParams();
    const { me, user, isAdmin } = useUser();
    const { confirm } = useDialogs();
    const [detail, setDetail] = useState(null);
    const [error, setError] = useState(null);
    const [procDef, setProcDef] = useState(null);

    const load = useCallback(() =>
        api.get(`/api/instances/${id}`).then((d) => { setDetail(d); setError(null); }).catch(setError), [id]);

    useEffect(() => { load(); }, [load]);

    const processKey = detail?.summary.processKey;
    useEffect(() => {
        if (!processKey) return;
        api.get(`/api/definitions/${encodeURIComponent(processKey)}`)
            .then((r) => setProcDef(r.definition))
            .catch(() => setProcDef(null));
    }, [processKey]);

    if (!detail) return <><ErrorBox error={error} /><p className="muted">Loading...</p></>;

    const s = detail.summary;
    const links = procDef?.links || [];
    const ids = [me?.email, me?.username, ...(me?.groups || []).map((g) => `group:${g}`)]
        .filter(Boolean).map((x) => x.toLowerCase());
    const isStarter = (s.startedBy || "").toLowerCase() === (user || "").toLowerCase();
    const involved = detail.steps.some((st) =>
        (st.assignees || []).some((a) => ids.includes(a)) || (st.approvals || []).some((a) => ids.includes(a.approver)));
    const isRequester = (detail.requesters || []).some((r) => ids.includes(r));
    const isObserver = (detail.observers || []).some((o) => ids.includes(o));
    const watchOnly = !isAdmin && !isStarter && !involved && (isRequester || isObserver);
    const canManage = isAdmin || isStarter;
    const canWork = canManage || involved;
    const needsRequester = isAdmin && (procDef?.requesters || []).length > 0 && (detail.requesters || []).length === 0;

    const actives = detail.steps.filter((x) => x.status === "InProgress" || x.status === "Failed");
    const endedAt = detail.steps.find((x) => x.type === "End" && x.status === "Completed");
    const canCancel = canManage && (s.status === "Running" || s.status === "Failed");
    const built = links.map((l) => ({ ...l, ...buildLink(l.url, detail.data) }));
    const files = detail.steps.flatMap((st) => (st.output?.files || []).map((f) => ({ ...f, step: st.name })));

    const cancel = async () => {
        const ok = await confirm("Cancel this request? Every step that has not finished is closed.",
            { title: "Cancel request", okLabel: "Cancel the request", danger: true });
        if (!ok) return;
        try { await api.post(`/api/instances/${id}/cancel`); load(); } catch (e) { setError(e); }
    };

    return (
        <>
            <h2>{s.title} <Badge status={s.status} /></h2>
            <p className="muted">
                {s.processName} v{s.processVersion} · started {fmt(s.createdAt)}
                {s.startedBy && ` by ${s.startedBy}`}
                {(detail.requesters || []).length > 0 && ` · requested by ${detail.requesters.join(", ")}`}
                {s.completedAt && ` · finished ${fmt(s.completedAt)}`}
            </p>
            <ErrorBox error={error} />

            {watchOnly && (
                <div className="hintbar">
                    <Icon name="eye" size={16} />
                    <span>
                        {isRequester
                            ? "This is your request. It is being handled for you, and any booking documents will appear below."
                            : "You are observing this request. You can follow it but you cannot act on it."}
                    </span>
                </div>
            )}
            {endedAt && (
                <div className="hintbar">
                    <Icon name="stop" size={16} />
                    <span>This request ended at “{endedAt.name}”{endedAt.output?.reason ? `: ${endedAt.output.reason}` : "."}</span>
                </div>
            )}
            {needsRequester && (
                <div className="warnbar" style={{ marginBottom: 14 }}>
                    The requester could not be worked out for this request, so the requester cannot see it.
                    Check the value in “Collected data” below, and the requester setting of the process.
                </div>
            )}

            <Stepper steps={detail.steps} />

            {(canManage || (detail.observers || []).length > 0) && (
                <ObserversBar detail={detail} canEdit={canManage} onDone={(d) => setDetail(d)} />
            )}

            <div className="panel">
                <h3>Documents</h3>
                {built.map((l, i) =>
                    l.valid ? (
                        <SafeLink key={i} url={l.url} label={l.label} instanceId={s.id} index={i} />
                    ) : (
                        <div key={i} className="linkrow">
                            <Icon name="link" size={16} />
                            <span className="muted">
                                {l.label}: {l.missing.length ? `waiting for ${l.missing.join(", ")}` : "the address is not valid"}
                            </span>
                        </div>
                    )
                )}
                <FileLinks files={files} />
                {built.length === 0 && files.length === 0 && (
                    <p className="muted">No documents yet. Booking files appear here once they have been uploaded.</p>
                )}
                {links.length === 0 && isAdmin && (
                    <div className="hintbar" style={{ marginTop: 10 }}>
                        <Icon name="link" size={16} />
                        <span>No document links are set up for this process.</span>
                        <Link to={`/designer/${s.processKey}`}>Add one in the designer</Link>
                    </div>
                )}
            </div>

            {actives.map((st) => (
                <StepAction key={st.id + st.status} detail={detail} step={st} user={user} ids={ids}
                    isAdmin={isAdmin} isStarter={isStarter} canWork={canWork} onDone={(d) => setDetail(d)} />
            ))}
            {canCancel && <button className="secondary" onClick={cancel}>Cancel request</button>}

            <h3>Step history</h3>
            <div className="scroll">
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
                                        <div>{st.approvals.map((a, n) => (
                                            <div key={a.approver + n}>
                                                <small>
                                                    {a.approver.startsWith("group:") ? `${a.approver.slice(6)} (group)` : a.approver}:{" "}
                                                    {a.decision
                                                        ? `${a.decision}${a.decidedBy && a.decidedBy !== a.approver ? ` by ${a.decidedBy}` : ""}`
                                                        : "waiting"}
                                                    {a.note && ` (${a.note})`}
                                                </small>
                                            </div>
                                        ))}</div>
                                    )}
                                    {st.assignees && <div><small>Assigned to {st.assignees.join(", ")}</small></div>}
                                    {st.dueAt && st.status === "InProgress" && <div><DueTag dueAt={st.dueAt} /></div>}
                                    {st.error && <div className="ok-err">{st.error}</div>}
                                    {st.type === "Decision" && st.output?.result && (
                                        <div><small>Went the <b>{st.output.result === "then" ? "If true" : "Else"}</b> way. {st.output.reason}</small></div>
                                    )}
                                    {st.status === "Skipped" ? (
                                        <div><small>Skipped: {st.output?.reason || "its condition was not met"}</small></div>
                                    ) : st.output && (
                                        <details><summary>output</summary><pre>{JSON.stringify(st.output, null, 2)}</pre></details>
                                    )}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>

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