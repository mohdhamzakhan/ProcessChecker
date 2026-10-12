import { Fragment, useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { api } from "../api";
import { ErrorBox } from "../ui";
import { DropZone, STEP_MIME, endDrag, startDrag } from "./dnd";
import { ProcessProps, StepProps } from "./Props";
import {
    STEP_ORDER, STEP_TYPES, allIssues, blankState, fromDefinition, newStep, stepIssues, toDefinition, uid, unique,
} from "./model";
import "./designer.css";

function summary(st) {
    switch (st.type) {
        case "Form": return `${st.fields.length} field${st.fields.length === 1 ? "" : "s"}${st.allowUpload ? " + attachment" : ""}`;
        case "Approval": return st.approvers.length ? st.approvers.join(", ") : "No approvers yet";
        case "Dispatch":
        case "Notify": return st.sendTo ? `To ${st.sendTo}` : "No recipient yet";
        case "Upload": return "Waits for a file";
        case "DataFetch": return st.connection ? `Reads from ${st.connection}` : "No data source yet";
        default: return "";
    }
}

export default function Designer() {
    const { key: routeKey } = useParams();
    const [state, setState] = useState(blankState);
    const [version, setVersion] = useState(null);
    const [selected, setSelected] = useState("process");
    const [dirty, setDirty] = useState(false);
    const [loading, setLoading] = useState(!!routeKey);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);
    const [msg, setMsg] = useState("");
    const [showJson, setShowJson] = useState(false);
    const [connections, setConnections] = useState([]);

    useEffect(() => {
        api.get("/api/connections").then(setConnections).catch(() => setConnections([]));
    }, []);

    useEffect(() => {
        setError(null); setMsg(""); setSelected("process");
        if (!routeKey) { setState(blankState()); setVersion(null); setDirty(false); setLoading(false); return; }
        setLoading(true);
        api.get(`/api/definitions/${encodeURIComponent(routeKey)}`)
            .then((r) => { setState(fromDefinition(r.definition)); setVersion(r.version); setDirty(false); })
            .catch(setError)
            .finally(() => setLoading(false));
    }, [routeKey]);

    useEffect(() => {
        window.addEventListener("dragend", endDrag);
        window.addEventListener("drop", endDrag, true);
        return () => { window.removeEventListener("dragend", endDrag); window.removeEventListener("drop", endDrag, true); };
    }, []);

    useEffect(() => {
        if (!dirty) return undefined;
        const h = (e) => { e.preventDefault(); e.returnValue = ""; };
        window.addEventListener("beforeunload", h);
        return () => window.removeEventListener("beforeunload", h);
    }, [dirty]);

    const change = (fn) => { setState(fn); setDirty(true); setMsg(""); };
    const issues = useMemo(() => allIssues(state), [state]);
    const stepIndex = state.steps.findIndex((s) => s.uid === selected);
    const selectedStep = stepIndex >= 0 ? state.steps[stepIndex] : null;

    // ------------------------------------------------ canvas actions
    const addStep = (type, at) => {
        const st = newStep(type, state.steps);
        change((s) => {
            const steps = [...s.steps];
            steps.splice(at ?? steps.length, 0, st);
            return { ...s, steps };
        });
        setSelected(st.uid);
    };

    const moveStep = (id, to) =>
        change((s) => {
            const from = s.steps.findIndex((x) => x.uid === id);
            if (from < 0) return s;
            const steps = [...s.steps];
            const [item] = steps.splice(from, 1);
            steps.splice(to > from ? to - 1 : to, 0, item);
            return { ...s, steps };
        });

    const shift = (id, dir) => {
        const i = state.steps.findIndex((x) => x.uid === id);
        const j = i + dir;
        if (i < 0 || j < 0 || j >= state.steps.length) return;
        change((s) => {
            const steps = [...s.steps];
            [steps[i], steps[j]] = [steps[j], steps[i]];
            return { ...s, steps };
        });
    };

    const duplicate = (id) => {
        const i = state.steps.findIndex((x) => x.uid === id);
        if (i < 0) return;
        const src = state.steps[i];
        const copy = {
            ...src, uid: uid(), name: `${src.name} (copy)`,
            key: unique(src.key, new Set(state.steps.map((s) => s.key))),
            approvers: [...src.approvers], columns: [...(src.columns || [])],
            fields: src.fields.map((f) => ({ ...f, uid: uid(), options: [...f.options] })),
        };
        change((s) => {
            const steps = [...s.steps];
            steps.splice(i + 1, 0, copy);
            return { ...s, steps };
        });
        setSelected(copy.uid);
    };

    const remove = (id) => {
        if (!confirm("Remove this step?")) return;
        change((s) => ({ ...s, steps: s.steps.filter((x) => x.uid !== id) }));
        setSelected("process");
    };

    const onDropStep = (p, at) => (p.kind === "new" ? addStep(p.type, at) : moveStep(p.uid, at));

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

    if (loading) return <p className="muted">Loading...</p>;

    const t = state.trigger;
    const startText = t.type === "DatabasePoll"
        ? `Checks ${t.connection || "a database"} every ${t.everyMinutes || "?"} min`
        : "Started by a person";

    return (
        <>
            <div className="dbar">
                <Link to="/processes" className="back">← Processes</Link>
                <div className="dtitle">
                    <b>{state.name || "Untitled"}</b> <code>{state.key}</code>
                    {version ? <span className="badge Completed">v{version}</span> : <span className="badge">Not saved yet</span>}
                    {dirty && <span className="badge Running">Unsaved changes</span>}
                </div>
                <div className="actions">
                    <button className="secondary" onClick={() => setShowJson((v) => !v)}>{showJson ? "Hide JSON" : "Show JSON"}</button>
                    <button disabled={busy || issues.length > 0} onClick={save}
                        title={issues.length ? "Fix the listed problems first" : "Save this process"}>
                        {busy ? "Saving..." : "Save"}
                    </button>
                </div>
            </div>

            <ErrorBox error={error} />
            {msg && <div className="okbar">{msg}</div>}
            {issues.length > 0 && (
                <details className="issues">
                    <summary>{issues.length} thing{issues.length === 1 ? "" : "s"} to fix before you can save</summary>
                    <ul>
                        {issues.map((i, n) => (
                            <li key={n}><button className="link" onClick={() => setSelected(i.scope)}>{i.text}</button></li>
                        ))}
                    </ul>
                </details>
            )}
            {showJson && <pre>{JSON.stringify(toDefinition(state), null, 2)}</pre>}

            <div className="designer">
                {/* ---------------- palette ---------------- */}
                <aside className="dpanel palette">
                    <h4>Steps</h4>
                    <p className="muted small">Drag onto the canvas, or click to add at the end.</p>
                    {STEP_ORDER.map((type) => {
                        const m = STEP_TYPES[type];
                        return (
                            <div key={type} className="pitem" style={{ "--c": m.color }} draggable
                                onDragStart={(e) => startDrag(e, STEP_MIME, { kind: "new", type })}
                                onDragEnd={endDrag}
                                onClick={() => addStep(type)}>
                                <span className="picon">{m.icon}</span>
                                <div><b>{m.label}</b><small>{m.hint}</small></div>
                            </div>
                        );
                    })}
                </aside>

                {/* ---------------- canvas ---------------- */}
                <section className="canvas">
                    <div className={`node start${selected === "process" ? " sel" : ""}`} onClick={() => setSelected("process")}>
                        ▶ Start
                        <small>{startText}</small>
                    </div>

                    {state.steps.length === 0 ? (
                        <DropZone big mime={STEP_MIME} label="Drop your first step here" onDrop={(p) => onDropStep(p, 0)} />
                    ) : (
                        <>
                            {state.steps.map((st, i) => {
                                const m = STEP_TYPES[st.type];
                                const bad = stepIssues(st, state.steps).length > 0;
                                return (
                                    <Fragment key={st.uid}>
                                        <DropZone mime={STEP_MIME} label="Move here" onDrop={(p) => onDropStep(p, i)} />
                                        <div className={`scard${selected === st.uid ? " sel" : ""}`} style={{ "--c": m.color }} draggable
                                            onDragStart={(e) => startDrag(e, STEP_MIME, { kind: "move", uid: st.uid })}
                                            onDragEnd={endDrag}
                                            onClick={() => setSelected(st.uid)}>
                                            <span className="sicon">{m.icon}</span>
                                            <div className="smain">
                                                <div className="stitle">{i + 1}. {st.name || "(unnamed)"} {bad && <span className="warn-dot" title="Needs attention" />}</div>
                                                <div className="ssub">{m.label} · {summary(st)}</div>
                                            </div>
                                            <div className="sbtns" onClick={(e) => e.stopPropagation()}>
                                                <button className="icon" title="Move up" disabled={i === 0} onClick={() => shift(st.uid, -1)}>↑</button>
                                                <button className="icon" title="Move down" disabled={i === state.steps.length - 1} onClick={() => shift(st.uid, 1)}>↓</button>
                                                <button className="icon" title="Duplicate" onClick={() => duplicate(st.uid)}>⧉</button>
                                                <button className="icon" title="Remove" onClick={() => remove(st.uid)}>🗑</button>
                                            </div>
                                        </div>
                                    </Fragment>
                                );
                            })}
                            <DropZone mime={STEP_MIME} label="Add at the end" onDrop={(p) => onDropStep(p, state.steps.length)} />
                        </>
                    )}

                    <div className="node end">■ End</div>
                </section>

                {/* ---------------- properties ---------------- */}
                <aside className="dpanel props">
                    {selectedStep ? (
                        <StepProps key={selectedStep.uid} step={selectedStep} index={stepIndex} state={state}
                            change={change} connections={connections} />
                    ) : (
                        <ProcessProps state={state} change={change} connections={connections} />
                    )}
                </aside>
            </div>
        </>
    );
}