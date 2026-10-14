import { useCallback, useEffect, useState } from "react";
import { Link,useParams } from "react-router-dom";
import { api, uploadDocument } from "../api";
import { useUser } from "../user";
import { buildLink } from "../links";
import Icon from "../designer/Icon";
import UserPicker from "../UserPicker";
import { Badge, DynamicForm, ErrorBox, FileLinks, SafeLink, Stepper, fmt } from "../ui";

function AssignBar({ detail, step, isAdmin, onDone }) {
    const [edit, setEdit] = useState(false);
    const [list, setList] = useState([]);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    const isApproval = step.type === "Approval";
    const current = isApproval ? (step.approvals || []).map((a) => a.approver) : step.assignees || [];

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

function StepAction({ detail, step, user, isAdmin, isStarter, canWork, onDone }) {
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
    const header = <h3>Action needed: {step.name}</h3>;
    const assigned = step.assignees || [];
    const canAct = assigned.length > 0
        ? isAdmin || assigned.includes(user.toLowerCase())
        : isAdmin || isStarter;
    const notYours = <p className="muted">This task is for someone else. Only they, or an admin, can complete it.</p>;

    if (step.type === "Approval") {
        const me = step.approvals?.find((a) => a.approver === user.toLowerCase());
        return (
            <div className="panel">
                {header}
                {bar}
                {!me && <p className="muted">You are not an approver for this step.</p>}
                {me && !me.decision && (
                    <>
                        <label><span>Note (optional)</span>
                            <input value={note} onChange={(e) => setNote(e.target.value)} /></label>
                        <ErrorBox error={error} />
                        <div className="actions" style={{ marginTop: 10 }}>
                            <button disabled={busy} onClick={() => run(() => api.post(`${base}/approve`, { note }))}>Approve</button>
                            <button disabled={busy} className="danger" onClick={() => run(() => api.post(`${base}/reject`, { note }))}>Reject</button>
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

    useEffect(() => {
        if (!detail) return;
        const built = links.map((l) => ({ ...l, ...buildLink(l.url, detail.data) }));
        api.get(`/api/definitions/${encodeURIComponent(detail.summary.processKey)}`)
            .then((r) => setLinks(r.definition.links || []))
            .catch(() => setLinks([]));
    }, [detail?.summary.processKey]);   // eslint-disable-line

    if (!detail) return <><ErrorBox error={error} /><p className="muted">Loading...</p></>;

    const s = detail.summary;
    const links = procDef?.links || [];
    const ids = [me?.email, me?.username].filter(Boolean).map((x) => x.toLowerCase());
    const isStarter = (s.startedBy || "").toLowerCase() === (user || "").toLowerCase();
    const involved = detail.steps.some((st) =>
        (st.assignees || []).some((a) => ids.includes(a)) || (st.approvals || []).some((a) => ids.includes(a.approver)));
    const isRequester = (detail.requesters || []).some((r) => ids.includes(r));
    const isObserver = (detail.observers || []).some((o) => ids.includes(o));
    const watchOnly = !isAdmin && !isStarter && !involved && (isRequester || isObserver);
    const canManage = isAdmin || isStarter;
    const canWork = canManage || involved;
    const needsRequester = isAdmin && (procDef?.requesters || []).length > 0 && (detail.requesters || []).length === 0;

    const active = detail.steps.find((x) => x.status === "InProgress" || x.status === "Failed");
    const canCancel = canManage && (s.status === "Running" || s.status === "Failed");
    const built = links.map((l) => ({ ...l, ...buildLink(l.url, detail.data) }));
    const files = detail.steps.flatMap((st) => (st.output?.files || []).map((f) => ({ ...f, step: st.name })));

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
                {(detail.requesters || []).length > 0 && ` · requested by ${detail.requesters.join(", ")}`}
                {s.completedAt && ` · finished ${fmt(s.completedAt)}`}
            </p>
            <ErrorBox error={error} />

            {watchOnly && (
                <div className="hintbar">
                    <Icon name="eye" size={16} />
                    <span>
                        {isRequester
                            ? "This is your request. An administrator is handling it, and any booking documents will appear below."
                            : "You are observing this request. You can follow it but you cannot act on it."}
                    </span>
                </div>
            )}
            {needsRequester && (
                <div className="warnbar" style={{ marginBottom: 14 }}>
                    The requester could not be worked out for this request, so the requester cannot see it.
                    Check the value in “Collected data” below, and the requester setting of the process.
                </div>
            )}

            <Stepper steps={detail.steps} />

            {built.length > 0 && (
                <div className="panel">
                    <h3>Documents</h3>
                    {built.map((l, i) =>
                        l.valid ? (
                            <SafeLink key={i} url={l.url} label={l.label} />
                        ) : (
                            <div key={i} className="linkrow">
                                <Icon name="link" size={16} />
                                <span className="muted">
                                    {l.label}: {l.missing.length ? `waiting for ${l.missing.join(", ")}` : "the address is not valid"}
                                </span>
                            </div>
                        )
                    )}
                </div>
            )}

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

            {active && (
                <StepAction key={active.id + active.status} detail={detail} step={active} user={user}
                    isAdmin={isAdmin} isStarter={isStarter} canWork={canWork} onDone={(d) => setDetail(d)} />
            )}
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
                                        <div>{st.approvals.map((a) => (
                                            <div key={a.approver}><small>{a.approver}: {a.decision || "waiting"}{a.note && ` (${a.note})`}</small></div>
                                        ))}</div>
                                    )}
                                    {st.assignees && <div><small>Assigned to {st.assignees.join(", ")}</small></div>}
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