import { Fragment, useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { api } from "../api";
import { ErrorBox } from "../ui";
import Icon from "./Icon";
import { DropZone, STEP_MIME, endDrag, startDrag } from "./dnd";
import { LinksProps, ProcessProps, StepProps } from "./Props";
import {
    STEP_GROUPS, STEP_ORDER, STEP_TYPES, allIssues, blankState, buildTree, condSummary, countInside,
    fromDefinition, insertStep, laneSize, locate, moveStep, newStep, removeStep, shiftStep,
    slugKey, stepIssues, toDefinition, uid, unique,
} from "./model";
import "./designer.css";

function summary(st) {
    switch (st.type) {
        case "Form": return `${st.fields.length} field${st.fields.length === 1 ? "" : "s"}${st.allowUpload ? " + attachment" : ""}${st.assignees?.length ? ` · for ${st.assignees.length}` : ""}`;
        case "Approval": return st.approvers.length ? st.approvers.join(", ") : "No approvers yet";
        case "Dispatch":
        case "Notify": return st.sendTo ? `To ${st.sendTo}` : "No recipient yet";
        case "Upload": return st.assignees?.length ? `For ${st.assignees.join(", ")}` : "Waits for a file";
        case "DataFetch": return st.connection ? `Reads from ${st.connection}` : "No data source yet";
        case "Link": return st.url || "No address yet";
        case "Decision": return st.condition?.conditions?.length ? `If ${condSummary(st.condition)}` : "No condition yet";
        case "End": return `${st.outcome || "Completed"}${st.reason ? ` · ${st.reason}` : ""}`;
        default: return "";
    }
}

/** The + between two steps; also a drop target. lane = null (main flow) or { of, path }. */
function Connector({ id, lane, index, ctx }) {
    const open = ctx.menuAt === id;
    return (
        <DropZone mime={STEP_MIME} label="Drop here" onDrop={(p) => ctx.onDropStep(p, lane, index)}>
            <button type="button" className={`plus${open ? " open" : ""}`} title="Insert a step here"
                onClick={(e) => { e.stopPropagation(); ctx.setMenuAt(open ? null : id); }}>
                <Icon name="plus" size={14} stroke={2.5} />
            </button>
            {open && (
                <div className="addmenu" onClick={(e) => e.stopPropagation()}>
                    {STEP_ORDER.map((type) => {
                        const m = STEP_TYPES[type];
                        return (
                            <button key={type} type="button" style={{ "--c": m.color }}
                                onClick={() => { ctx.addStep(type, lane, index); ctx.setMenuAt(null); }}>
                                <span className="pbadge"><Icon name={m.icon} size={16} /></span>{m.label}
                            </button>
                        );
                    })}
                </div>
            )}
        </DropZone>
    );
}

function Lane({ nodes, lane, ctx }) {
    const key = lane ? `${lane.of}:${lane.path}` : "root";
    return (
        <>
            {nodes.map((n, i) => {
                const prevNode = i > 0 ? nodes[i - 1] : null;
                const nextNode = i < nodes.length - 1 ? nodes[i + 1] : null;
                const isAfterEnd = prevNode && prevNode.step.type === "End";

                // Prevent moving up if it's the first item OR if it's an End step
                const cannotMoveUp = i === 0 || n.step.type === "End";

                // Prevent moving down if it's the last item OR if the next step is an End step
                const cannotMoveDown = i === nodes.length - 1 || (nextNode && nextNode.step.type === "End");

                return (
                    <Fragment key={n.step.uid}>
                        {!isAfterEnd && <Connector id={`${key}:${i}`} lane={lane} index={i} ctx={ctx} />}
                        <NodeView
                            n={n}
                            first={cannotMoveUp}
                            last={cannotMoveDown}
                            ctx={ctx}
                        />
                    </Fragment>
                );
            })}

            {(!nodes.length || nodes[nodes.length - 1].step.type !== "End") && (
                <Connector id={`${key}:${nodes.length}`} lane={lane} index={nodes.length} ctx={ctx} />
            )}

            {lane && nodes.length === 0 && (
                <div className="laneempty">
                    <small className="muted">No steps on this path</small>
                    <button type="button" className="secondary" onClick={() => ctx.addStep("End", lane, 0)}>
                        <Icon name="stop" size={13} />End the request here
                    </button>
                </div>
            )}
        </>
    );
}

function NodeView({ n, first, last, ctx }) {
    const st = n.step;
    const m = STEP_TYPES[st.type];
    const bad = stepIssues(st, ctx.steps).length > 0;
    const number = ctx.steps.findIndex((x) => x.uid === st.uid) + 1;

    const card = (
        <div className={`scard${st.type === "Decision" ? " decision" : ""}${ctx.selected === st.uid ? " sel" : ""}${bad ? " bad" : ""}`}
            style={{ "--c": m.color }} draggable
            onDragStart={(e) => startDrag(e, STEP_MIME, { kind: "move", uid: st.uid })}
            onDragEnd={endDrag}
            onClick={() => ctx.setSelected(st.uid)}>
            <Icon name="grip" size={16} className="grip" />
            <span className="sbadge"><Icon name={m.icon} size={20} /></span>
            <div className="smain">
                <div className="skind">Step {number} · {m.label}</div>
                <div className="stitle">{st.name || "(unnamed)"}</div>
                <div className="ssub">{summary(st)}</div>
                {st.type !== "Decision" && st.runIf?.conditions?.length > 0 && (
                    <div className="scond" title={condSummary(st.runIf)}>
                        <Icon name="branch" size={11} />If {condSummary(st.runIf)}
                    </div>
                )}
            </div>
            {bad && <span className="needs" title="This step needs attention"><Icon name="alert" size={13} /></span>}
            <div className="sbtns" onClick={(e) => e.stopPropagation()}>
                <button className="icon" title="Move up" disabled={first} onClick={() => ctx.shift(st.uid, -1)}><Icon name="up" size={15} /></button>
                <button className="icon" title="Move down" disabled={last} onClick={() => ctx.shift(st.uid, 1)}><Icon name="down" size={15} /></button>
                <button className="icon" title="Duplicate" onClick={() => ctx.duplicate(st.uid)}><Icon name="copy" size={15} /></button>
                <button className="icon danger" title="Remove" onClick={() => ctx.remove(st.uid)}><Icon name="trash" size={15} /></button>
            </div>
        </div>
    );

    if (st.type !== "Decision") return card;

    return (
        <>
            {card}
            <div className="lanes">
                {["then", "else"].map((path) => (
                    <div key={path} className={`lane ${path}`}>
                        <span className="lanehead">{path === "then" ? "If true" : "Else"}</span>
                        <Lane nodes={n[path]} lane={{ of: st.uid, path }} ctx={ctx} />
                    </div>
                ))}
            </div>
            <div className="merge" />
        </>
    );
}

export default function Designer() {
    const { key: routeKey } = useParams();
    const nav = useNavigate();
    const [state, setState] = useState(blankState);
    const [version, setVersion] = useState(null);
    const [selected, setSelected] = useState("process");
    const [dirty, setDirty] = useState(false);
    const [loading, setLoading] = useState(!!routeKey);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);
    const [msg, setMsg] = useState("");
    const [panel, setPanel] = useState(null);       // "issues" | "json" | null
    const [menuAt, setMenuAt] = useState(null);
    const [q, setQ] = useState("");
    const [connections, setConnections] = useState([]);

    useEffect(() => {
        api.get("/api/connections").then(setConnections).catch(() => setConnections([]));
    }, []);

    useEffect(() => {
        setError(null); setMsg(""); setSelected("process"); setPanel(null);
        if (!routeKey) { setState(blankState()); setVersion(null); setDirty(false); setLoading(false); return; }
        setLoading(true);
        api.get(`/api/definitions/${encodeURIComponent(routeKey)}`)
            .then((r) => { setState(fromDefinition(r.definition)); setVersion(r.version); setDirty(false); })
            .catch(setError)
            .finally(() => setLoading(false));
    }, [routeKey]);

    useEffect(() => {
        const close = () => setMenuAt(null);
        const esc = (e) => { if (e.key === "Escape") { setMenuAt(null); setPanel(null); } };
        window.addEventListener("dragend", endDrag);
        window.addEventListener("drop", endDrag, true);
        document.addEventListener("click", close);
        document.addEventListener("keydown", esc);
        return () => {
            window.removeEventListener("dragend", endDrag);
            window.removeEventListener("drop", endDrag, true);
            document.removeEventListener("click", close);
            document.removeEventListener("keydown", esc);
        };
    }, []);

    useEffect(() => {
        if (!dirty) return undefined;
        const h = (e) => { e.preventDefault(); e.returnValue = ""; };
        window.addEventListener("beforeunload", h);
        return () => window.removeEventListener("beforeunload", h);
    }, [dirty]);

    const change = (fn) => { setState(fn); setDirty(true); setMsg(""); };
    const issues = useMemo(() => allIssues(state), [state]);
    const tree = useMemo(() => buildTree(state.steps), [state.steps]);
    const stepIndex = state.steps.findIndex((s) => s.uid === selected);
    const selectedStep = stepIndex >= 0 ? state.steps[stepIndex] : null;
    const branched = state.steps.some((s) => s.type === "Decision");

    // ---------------------------------------------------------- canvas actions
    const addStep = (type, lane = null, index = null) => {
        const st = newStep(type, state.steps);
        change((s) => ({ ...s, steps: insertStep(s.steps, st, lane, index ?? laneSize(s.steps, lane)) }));
        setSelected(st.uid);
    };

    const onDropStep = (p, lane, index) =>
        p.kind === "new"
            ? addStep(p.type, lane, index)
            : change((s) => ({ ...s, steps: moveStep(s.steps, p.uid, lane, index) }));

    const shift = (id, dir) => change((s) => ({ ...s, steps: shiftStep(s.steps, id, dir) }));

    const duplicate = (id) => {
        const src = state.steps.find((x) => x.uid === id);
        if (!src) return;
        const copy = {
            ...src, uid: uid(), name: `${src.name} (copy)`,
            key: unique(src.key, new Set(state.steps.map((s) => s.key))),
            approvers: [...(src.approvers || [])], assignees: [...(src.assignees || [])],
            columns: [...(src.columns || [])],
            fields: (src.fields || []).map((f) => ({ ...f, uid: uid(), options: [...f.options] })),
            runIf: { match: src.runIf?.match || "all", conditions: (src.runIf?.conditions || []).map((c) => ({ ...c, uid: uid() })) },
            condition: { match: src.condition?.match || "all", conditions: (src.condition?.conditions || []).map((c) => ({ ...c, uid: uid() })) },
        };
        const at = locate(state.steps, id);
        change((s) => ({ ...s, steps: insertStep(s.steps, copy, at.lane, at.index + 1) }));
        setSelected(copy.uid);
    };

    const remove = (id) => {
        const inside = countInside(state.steps, id);
        if (!confirm(inside ? `Remove this step and the ${inside} step${inside === 1 ? "" : "s"} inside it?` : "Remove this step?")) return;
        change((s) => ({ ...s, steps: removeStep(s.steps, id) }));
        setSelected("process");
    };

    const ctx = { steps: state.steps, selected, setSelected, menuAt, setMenuAt, onDropStep, addStep, shift, duplicate, remove };

    // ---------------------------------------------------------- saving
    const save = async () => {
        if (issues.length) return;
        setBusy(true); setError(null); setMsg("");
        try {
            const res = await api.post("/api/definitions", toDefinition(state));
            setVersion(res.version);
            setDirty(false);
            setState((s) => ({ ...s, isNew: false }));
            setMsg(`Saved as version ${res.version}. New requests use it; running requests keep their old version.`);
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    const saveCopy = async () => {
        if (issues.length) { setPanel("issues"); return; }
        const k = prompt("Key for the new process (letters, numbers, -):", `${state.key}-copy`);
        if (!k) return;
        const key = slugKey(k);
        if (!key) { alert("That key is not valid."); return; }

        setBusy(true); setError(null); setMsg("");
        try {
            try {
                await api.get(`/api/definitions/${encodeURIComponent(key)}`);
                alert(`A process with the key "${key}" already exists. Choose another key.`);
                return;
            } catch (e) { if (e.status !== 404) throw e; }

            await api.post("/api/definitions", { ...toDefinition(state), key, name: `${state.name.trim()} (copy)` });
            setDirty(false);
            nav(`/designer/${key}`);
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    if (loading) return <p className="muted" style={{ padding: 24 }}>Loading...</p>;

    const t = state.trigger;
    const startText = t.type === "DatabasePoll"
        ? `Checks ${t.connection || "a database"} every ${t.everyMinutes || "?"} min`
        : "Started by a person";
    const ql = q.trim().toLowerCase();

    return (
        <div className="dwrap">
            {/* ---------------------------------------------------------- toolbar */}
            <div className="dtop">
                <Link to="/processes" className="iconbtn" title="Back to processes"><Icon name="back" size={18} /></Link>

                <div className="titlebox">
                    <input className="title-input" aria-label="Process name" placeholder="Process name"
                        value={state.name}
                        onChange={(e) => {
                            const name = e.target.value;
                            change((s) => ({ ...s, name, key: s.isNew && !s.keyTouched ? slugKey(name) : s.key }));
                        }} />
                    <Icon name="pencil" size={14} />
                </div>

                <code className="keychip" title={state.isNew ? "The key is set on the Start panel" : "The key cannot change after the first save"}>
                    {!state.isNew && <Icon name="lock" size={11} />}{state.key || "key"}
                </code>
                {version ? <span className="badge Completed">v{version}</span> : <span className="badge">Draft</span>}
                {dirty && <span className="badge Running">Unsaved</span>}

                <div className="dspacer" />

                <button type="button" className={`pill ${issues.length ? "warn" : "ok"}`}
                    onClick={(e) => { e.stopPropagation(); setPanel(panel === "issues" ? null : "issues"); }}>
                    <Icon name={issues.length ? "alert" : "check"} size={14} />
                    {issues.length ? `${issues.length} to fix` : "All good"}
                </button>
                <button className="secondary" onClick={() => setPanel(panel === "json" ? null : "json")}>
                    <Icon name="code" size={15} />JSON
                </button>
                {!state.isNew && (
                    <button className="secondary" disabled={busy} onClick={saveCopy}>
                        <Icon name="copy" size={15} />Save as copy
                    </button>
                )}
                <button disabled={busy || issues.length > 0} onClick={save}
                    title={issues.length ? "Fix the listed problems first" : "Save this process"}>
                    <Icon name="save" size={15} />{busy ? "Saving..." : "Save"}
                </button>
            </div>

            {error && <div className="dmsg"><ErrorBox error={error} /></div>}
            {msg && <div className="okbar">{msg}</div>}

            {panel === "issues" && (
                <div className="popover" onClick={(e) => e.stopPropagation()}>
                    <b>{issues.length ? "Fix these before saving" : "No problems found"}</b>
                    <ul>
                        {issues.map((i, n) => (
                            <li key={n}>
                                <button className="link" onClick={() => { setSelected(i.scope); setPanel(null); }}>{i.text}</button>
                            </li>
                        ))}
                    </ul>
                </div>
            )}
            {panel === "json" && (
                <div className="popover wide" onClick={(e) => e.stopPropagation()}>
                    <b>Generated definition</b>
                    <pre>{JSON.stringify(toDefinition(state), null, 2)}</pre>
                </div>
            )}

            {/* ---------------------------------------------------------- body */}
            <div className="dbody">
                <aside className="pane palette">
                    <div className="psearch">
                        <Icon name="search" size={15} />
                        <input placeholder="Search steps" value={q} onChange={(e) => setQ(e.target.value)} />
                    </div>
                    <p className="muted small">Drag a step onto the canvas, or click one to add it at the end.</p>

                    {STEP_GROUPS.map((g) => {
                        const items = g.types.filter((type) =>
                            !ql || STEP_TYPES[type].label.toLowerCase().includes(ql) || STEP_TYPES[type].hint.toLowerCase().includes(ql));
                        if (!items.length) return null;
                        return (
                            <div key={g.label}>
                                <div className="plabel">{g.label}</div>
                                {items.map((type) => {
                                    const m = STEP_TYPES[type];
                                    return (
                                        <div key={type} className="pitem" style={{ "--c": m.color }} draggable
                                            onDragStart={(e) => startDrag(e, STEP_MIME, { kind: "new", type })}
                                            onDragEnd={endDrag}
                                            onClick={() => addStep(type)}>
                                            <span className="pbadge"><Icon name={m.icon} size={18} /></span>
                                            <div><b>{m.label}</b><small>{m.hint}</small></div>
                                            <Icon name="grip" size={14} className="grip" />
                                        </div>
                                    );
                                })}
                            </div>
                        );
                    })}
                </aside>

                <section className="pane canvas">
                    <div className={`flow${branched ? " branched" : ""}`}>
                        <div className={`node start${selected === "process" ? " sel" : ""}`} onClick={() => setSelected("process")}>
                            <span className="nbadge"><Icon name="play" size={15} /></span>
                            <div><b>Start</b><small>{startText}</small></div>
                        </div>

                        {state.steps.length === 0 ? (
                            <>
                                <div className="line" />
                                <DropZone big mime={STEP_MIME} onDrop={(p) => onDropStep(p, null, 0)}>
                                    <div className="empty-flow">
                                        <Icon name="workflow" size={30} />
                                        <b>Start building your process</b>
                                        <span>Drag a step here from the left, or click one to add it.</span>
                                    </div>
                                </DropZone>
                            </>
                        ) : (
                            <Lane nodes={tree} lane={null} ctx={ctx} />
                        )}

                        <div className="node end">
                            <span className="nbadge"><Icon name="stop" size={14} /></span>
                            <div><b>End</b><small>The request is completed</small></div>
                        </div>

                        <div className={`node docs${selected === "links" ? " sel" : ""}`} onClick={() => setSelected("links")}>
                            <span className="nbadge"><Icon name="link" size={15} /></span>
                            <div>
                                <b>Document links</b>
                                <small>{(state.links || []).length ? `${state.links.length} shown on every request` : "None yet. Click to add"}</small>
                            </div>
                        </div>
                    </div>
                </section>

                <aside className="pane props">
                    {selected === "links" ? (
                        <LinksProps state={state} change={change} />
                    ) : selectedStep ? (
                        <StepProps key={selectedStep.uid} step={selectedStep} index={stepIndex} state={state}
                            change={change} connections={connections} />
                    ) : (
                        <ProcessProps state={state} change={change} connections={connections} dirty={dirty} />
                    )}
                </aside>
            </div>
        </div>
    );
}