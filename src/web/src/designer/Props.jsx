import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { DropZone, FIELD_MIME, endDrag, startDrag } from "./dnd";
import {
    FIELD_ORDER, FIELD_TYPES, STEP_TYPES, availableFields, isEmail, isPlaceholder,
    newField, slugKey, slugName, unique, placeholderNames
} from "./model";
import Icon from "./Icon";
import UserPicker from "../UserPicker";

const queryHint = (type) =>
    type === "Json" ? "JSONPath, for example $.requests[?(@.status=='new')]"
        : type === "Csv" ? "A filter, for example status = new AND amount > 1000"
            : "A single SELECT. Write :name where a value from the request data should go.";

// ---------------------------------------------------------------- inputs
function PlaceholderInput({ value, onChange, fields, multiline, rows = 4, placeholder }) {
    const ref = useRef(null);

    const insert = (name) => {
        if (!name) return;
        const el = ref.current;
        const token = `{{${name}}}`;
        const start = el?.selectionStart ?? value.length;
        const end = el?.selectionEnd ?? value.length;
        onChange(value.slice(0, start) + token + value.slice(end));
        requestAnimationFrame(() => {
            if (!el) return;
            el.focus();
            const p = start + token.length;
            el.setSelectionRange(p, p);
        });
    };

    const Tag = multiline ? "textarea" : "input";
    return (
        <div className="phi">
            <Tag ref={ref} rows={multiline ? rows : undefined} value={value} placeholder={placeholder}
                onChange={(e) => onChange(e.target.value)} />
            {fields.length > 0 && (
                <select value="" onChange={(e) => insert(e.target.value)} title="Insert a value collected earlier">
                    <option value="">＋ field</option>
                    {fields.map((f) => <option key={f.name} value={f.name}>{f.label} ({f.source})</option>)}
                </select>
            )}
        </div>
    );
}

function ApproverEditor({ value, onChange, fields }) {
    const [text, setText] = useState("");
    const [err, setErr] = useState("");

    const commit = (raw) => {
        const items = raw.split(/[;,\n]+/).map((x) => x.trim()).filter(Boolean);
        if (!items.length) return;
        const bad = items.filter((x) => !(isEmail(x) || isPlaceholder(x)));
        if (bad.length) { setErr(`Not an email or {{field}}: ${bad.join(", ")}`); return; }
        setErr("");
        const have = new Set(value.map((x) => x.toLowerCase()));
        onChange([...value, ...items.filter((x) => !have.has(x.toLowerCase()))]);
        setText("");
    };

    return (
        <div>
            <div className="chips-edit">
                {value.map((a, i) => (
                    <span key={a} className={`chip${a.startsWith("{{") ? " chip-field" : ""}`}>
                        {a}
                        <button type="button" className="x" onClick={() => onChange(value.filter((_, j) => j !== i))}>×</button>
                    </span>
                ))}
                <input
                    placeholder="name@company.com, then Enter"
                    value={text}
                    onChange={(e) => { setText(e.target.value); setErr(""); }}
                    onKeyDown={(e) => {
                        if (e.key === "Enter" || e.key === "," || e.key === ";") { e.preventDefault(); commit(text); }
                        else if (e.key === "Backspace" && !text && value.length) onChange(value.slice(0, -1));
                    }}
                    onBlur={() => text.trim() && commit(text)}
                />
            </div>
            {err && <div className="field-err">{err}</div>}
            {fields.length > 0 && (
                <select value="" onChange={(e) => e.target.value && commit(`{{${e.target.value}}}`)} style={{ marginTop: 6 }}>
                    <option value="">＋ approver chosen by a collected value (e.g. manager email)</option>
                    {fields.map((f) => <option key={f.name} value={f.name}>{f.label} ({f.source})</option>)}
                </select>
            )}
        </div>
    );
}

// ---------------------------------------------------------------- data source helpers
function SchemaSelect({ connection, value, onChange, fields = [] }) {
    const [info, setInfo] = useState({ loading: false, supported: false, schemas: [] });

    useEffect(() => {
        if (!connection) { setInfo({ loading: false, supported: false, schemas: [] }); return undefined; }
        let off = false;
        setInfo((i) => ({ ...i, loading: true }));
        api.get(`/api/connections/${encodeURIComponent(connection)}/schemas`)
            .then((r) => { if (!off) setInfo({ loading: false, supported: true, schemas: r.schemas }); })
            .catch(() => { if (!off) setInfo({ loading: false, supported: false, schemas: [] }); });
        return () => { off = true; };
    }, [connection]);

    if (!connection || (!info.loading && !info.supported)) return null;
    const known = !value || info.schemas.includes(value) || fields.some((f) => `{{${f.name}}}` === value);

    return (
        <label>
            <span>Schema</span>
            <select value={value} onChange={(e) => onChange(e.target.value)} disabled={info.loading}>
                <option value="">(the connection's default schema)</option>
                {!known && <option value={value}>{value}</option>}
                {info.schemas.length > 0 && (
                    <optgroup label="Schemas">
                        {info.schemas.map((s) => <option key={s} value={s}>{s}</option>)}
                    </optgroup>
                )}
                {fields.length > 0 && (
                    <optgroup label="Chosen by the user (a collected value)">
                        {fields.map((f) => <option key={f.name} value={`{{${f.name}}}`}>{f.label} ({f.source})</option>)}
                    </optgroup>
                )}
            </select>
        </label>
    );
}

const PARAM = /(?<!:):([A-Za-z_]\w*)/g;
const paramNames = (q) =>
    [...new Set([...(q || "").replace(/'(?:[^']|'')*'/g, "''").matchAll(PARAM)].map((m) => m[1]))];

function TestQuery({ connection, schema, query, onColumns }) {
    const [res, setRes] = useState(null);
    const [sample, setSample] = useState("");
    const names = paramNames(query);

    const run = async () => {
        let parameters;
        if (names.length && sample.trim()) {
            try { parameters = JSON.parse(sample); }
            catch { setRes({ error: "The sample values are not valid JSON." }); return; }
        }
        setRes({ busy: true });
        try {
            const body = { query, maxRows: 5 };
            if (parameters) body.parameters = parameters;
            if (schema && !schema.includes("{{")) body.schema = schema;
            const r = await api.post(`/api/connections/${encodeURIComponent(connection)}/query`, body);
            onColumns(r.columns);
            setRes({ ok: true, count: r.count, columns: r.columns });
        } catch (e) {
            setRes({ error: e.message });
        }
    };

    return (
        <div className="testq">
            {names.length > 0 && (
                <label style={{ width: "100%" }}>
                    <span>Sample values for {names.map((n) => `:${n}`).join(", ")} (JSON, for testing only)</span>
                    <input value={sample} placeholder={`{"${names[0]}": "R-1001"}`} onChange={(e) => setSample(e.target.value)} />
                </label>
            )}
            <button type="button" className="secondary" disabled={res?.busy} onClick={run}>
                {res?.busy ? "Testing..." : "Test query"}
            </button>
            {res?.ok && (
                <div className="ok small">
                    Works: {res.count} row(s) (limited to 5). Columns:{" "}
                    {res.columns.map((c) => <code key={c}>{c}</code>)}
                </div>
            )}
            {res?.error && <div className="field-err">{res.error}</div>}
        </div>
    );
}

// ---------------------------------------------------------------- form builder
function FormPreview({ fields, allowUpload }) {
    if (!fields.length) return null;
    return (
        <details className="preview">
            <summary>Preview the form</summary>
            <div className="form">
                {fields.map((f) => (
                    <label key={f.uid}>
                        <span>{f.label || f.name}{f.required && " *"}</span>
                        {f.type === "textarea" ? <textarea rows={2} disabled />
                            : f.type === "select" ? <select disabled><option>{f.options.filter(Boolean)[0] || "Select"}</option></select>
                                : f.type === "checkbox" ? <input type="checkbox" disabled />
                                    : <input disabled type={f.type === "number" ? "number" : f.type === "date" ? "date" : "text"} />}
                    </label>
                ))}
                {allowUpload && <label><span>Attachment</span><input type="file" disabled /></label>}
            </div>
        </details>
    );
}

function FieldBuilder({ fields, onChange }) {
    const [open, setOpen] = useState(null);

    const insertAt = (type, at) => {
        const f = newField(type, fields);
        const next = [...fields];
        next.splice(at, 0, f);
        onChange(next);
        setOpen(f.uid);
    };
    const moveTo = (id, to) => {
        const from = fields.findIndex((f) => f.uid === id);
        if (from < 0) return;
        const next = [...fields];
        const [item] = next.splice(from, 1);
        next.splice(to > from ? to - 1 : to, 0, item);
        onChange(next);
    };
    const drop = (p, at) => (p.kind === "new" ? insertAt(p.type, at) : moveTo(p.uid, at));
    const patch = (id, change) => onChange(fields.map((f) => (f.uid === id ? { ...f, ...change } : f)));

    const setLabel = (f, label) => {
        const change = { label };
        if (f.nameAuto) {
            const taken = new Set(fields.filter((x) => x.uid !== f.uid).map((x) => x.name));
            change.name = unique(slugName(label) || "field", taken, "_");
        }
        patch(f.uid, change);
    };
    const setType = (f, type) =>
        patch(f.uid, { type, options: type === "select" && f.options.length === 0 ? ["Option 1", "Option 2"] : f.options });

    return (
        <div className="sect fields">
            <h5>Form controls</h5>
            <p className="muted small">Drag a control into the form below, or click it to add at the end.</p>
            <div className="fpalette">
                {FIELD_ORDER.map((t) => (
                    <div key={t} className="fbtn" role="button" tabIndex={0} draggable
                        onDragStart={(e) => startDrag(e, FIELD_MIME, { kind: "new", type: t })}
                        onDragEnd={endDrag}
                        onClick={() => insertAt(t, fields.length)}
                        onKeyDown={(e) => e.key === "Enter" && insertAt(t, fields.length)}>
                        <Icon name={FIELD_TYPES[t].icon} size={18} />{FIELD_TYPES[t].label}
                    </div>
                ))}
            </div>

            <div className="flist">
                {fields.length === 0 && <div className="mini-empty">Drop a control here.</div>}
                <DropZone mime={FIELD_MIME} label="Insert here" onDrop={(p) => drop(p, 0)} />
                {fields.map((f, i) => (
                    <div key={f.uid}>
                        <div className={`fcard${open === f.uid ? " open" : ""}`}>
                            <div className="fhead" draggable
                                onDragStart={(e) => startDrag(e, FIELD_MIME, { kind: "move", uid: f.uid })}
                                onDragEnd={endDrag}
                                onClick={() => setOpen(open === f.uid ? null : f.uid)}>
                                <Icon name="grip" size={14} className="grip" />
                                <span className="ficon"><Icon name={FIELD_TYPES[f.type].icon} size={15} /></span>
                                <span className="flabel">{f.label || "(no label)"}{f.required && " *"}</span>
                                <small>{FIELD_TYPES[f.type].label}</small>
                                <button type="button" className="icon" title="Remove"
                                    onClick={(e) => { e.stopPropagation(); onChange(fields.filter((x) => x.uid !== f.uid)); }}><Icon name="trash" size={15} /></button>
                            </div>
                            {open === f.uid && (
                                <div className="fbody">
                                    <label><span>Label (what the person sees)</span>
                                        <input value={f.label} onChange={(e) => setLabel(f, e.target.value)} /></label>
                                    <label><span>Type</span>
                                        <select value={f.type} onChange={(e) => setType(f, e.target.value)}>
                                            {FIELD_ORDER.map((t) => <option key={t} value={t}>{FIELD_TYPES[t].label}</option>)}
                                        </select></label>
                                    <label className="inline">
                                        <input type="checkbox" checked={f.required} onChange={(e) => patch(f.uid, { required: e.target.checked })} />
                                        Required
                                    </label>
                                    {f.type === "select" && (
                                        <label><span>Options (one per line)</span>
                                            <textarea rows={4} value={f.options.join("\n")}
                                                onChange={(e) => patch(f.uid, { options: e.target.value.split("\n") })} /></label>
                                    )}
                                    <label><span>Field name (used as <code>{`{{${f.name || "name"}}}`}</code>)</span>
                                        <input value={f.name}
                                            onChange={(e) => patch(f.uid, { name: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, "_"), nameAuto: false })} /></label>
                                </div>
                            )}
                        </div>
                        <DropZone mime={FIELD_MIME} label="Insert here" onDrop={(p) => drop(p, i + 1)} />
                    </div>
                ))}
            </div>
        </div>
    );
}
function LinkHint({ url, fields }) {
    const known = new Set(fields.map((f) => f.name.toLowerCase()));
    const names = placeholderNames(url, known);
    const unknown = names.filter((n) => !known.has(n.toLowerCase()) && !n.includes("."));
    const text = (url || "").trim();
    const hostOk = /^https?:\/\/[^/?#{}\s]+(?=[/?#]|$)/i.test(text);

    return (
        <div className="sect">
            {text && !hostOk && (
                <div className="field-err">Start with http:// or https:// and a fixed server name (no {"{{ }}"} in the server part).</div>
            )}
            {names.length > 0 && (
                <div>
                    <span className="muted small">Values used: </span>
                    {names.map((n) => (
                        <code key={n} style={unknown.includes(n) ? { background: "#fef3c7" } : undefined}>{n}</code>
                    ))}
                </div>
            )}
            {unknown.length > 0 && (
                <div className="field-err">
                    Not collected before this step: {unknown.join(", ")}. Move this step later, or fix the name.
                </div>
            )}
            <p className="muted small">
                Use <code>{"{{id}}"}</code> for a value, and <code>{"{{id+1}}"}</code> or <code>{"{{id-2}}"}</code> to add or subtract a whole number. Pick a field with “Insert field…”, then type the +1 yourself.
                If a value is missing when the request runs, the step fails with a clear message and can be retried.
            </p>
        </div>
    );
}

// ---------------------------------------------------------------- panels

function ApplyRequesters({ processKey, disabled }) {
    const [msg, setMsg] = useState("");
    const [busy, setBusy] = useState(false);
    const run = async () => {
        setBusy(true); setMsg("");
        try {
            const r = await api.post(`/api/instances/resolve-requesters?processKey=${encodeURIComponent(processKey)}`);
            setMsg(`Updated ${r.updated} existing request(s).`);
        } catch (e) { setMsg(e.message); } finally { setBusy(false); }
    };
    return (
        <div>
            <button type="button" className="secondary" disabled={disabled || busy} onClick={run}>
                {busy ? "Working..." : "Apply to existing requests"}
            </button>
            {msg && <span className="small"> {msg}</span>}
            {disabled && <p className="muted small">Save the process first.</p>}
        </div>
    );
}
export function ProcessProps({ state, change, connections, dirty }) {
    const t = state.trigger;
    const setT = (patch) => change((s) => ({ ...s, trigger: { ...s.trigger, ...patch } }));
    const conn = connections.find((c) => c.name === t.connection);

    return (
        <div>
            <div className="phead" style={{ "--c": "#16a34a" }}>
                <span className="badge-ico"><Icon name="play" size={18} /></span>
                <div><small>Process</small><b>Settings and start trigger</b></div>
            </div>
            <div className="form">
                <label><span>Name</span>
                    <input value={state.name}
                        onChange={(e) => {
                            const name = e.target.value;
                            change((s) => ({ ...s, name, key: s.isNew && !s.keyTouched ? slugKey(name) : s.key }));
                        }} /></label>
                <label><span>Key {state.isNew ? "(letters, numbers, -)" : "(fixed once saved)"}</span>
                    <input value={state.key} disabled={!state.isNew}
                        onChange={(e) => change((s) => ({ ...s, keyTouched: true, key: e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "-") }))} /></label>
                <label><span>Description</span>
                    <textarea rows={2} value={state.description}
                        onChange={(e) => change((s) => ({ ...s, description: e.target.value }))} /></label>

                <div className="sect">
                    <h5>How does it start?</h5>
                    <label className="inline">
                        <input type="radio" checked={t.type === "Manual"} onChange={() => setT({ type: "Manual" })} />
                        Someone starts it by hand
                    </label>
                    <label className="inline">
                        <input type="radio" checked={t.type === "DatabasePoll"} onChange={() => setT({ type: "DatabasePoll" })} />
                        Automatically, when new rows appear in a database
                    </label>

                    {t.type === "DatabasePoll" && (
                        <>
                            <label><span>Data source</span>
                                <select value={t.connection} onChange={(e) => { setT({ connection: e.target.value, schema: "" }); change((s) => ({ ...s, triggerColumns: [] })); }}>
                                    <option value="">-- choose --</option>
                                    {connections.map((c) => <option key={c.name} value={c.name}>{c.name} ({c.type})</option>)}
                                </select></label>
                            {connections.length === 0 && <p className="muted small">No data sources yet. Add one under Connections.</p>}
                            <SchemaSelect connection={t.connection} value={t.schema} onChange={(v) => setT({ schema: v })} />
                            <label><span>Query that finds the rows to start from</span>
                                <textarea className="code" rows={4} value={t.query} onChange={(e) => setT({ query: e.target.value })} /></label>
                            <p className="muted small">{queryHint(conn?.type)} Filter to new rows only, for example WHERE STATUS = 'NEW'.</p>
                            {t.connection && t.query && (
                                <TestQuery connection={t.connection} schema={t.schema} query={t.query}
                                    onColumns={(cols) => change((s) => ({ ...s, triggerColumns: cols }))} />
                            )}
                            <label><span>Column that uniquely identifies a row</span>
                                {state.triggerColumns.length > 0 ? (
                                    <select value={t.keyColumn} onChange={(e) => setT({ keyColumn: e.target.value })}>
                                        <option value="">-- choose --</option>
                                        {state.triggerColumns.map((c) => <option key={c} value={c}>{c}</option>)}
                                    </select>
                                ) : (
                                    <input value={t.keyColumn} placeholder="e.g. ID" onChange={(e) => setT({ keyColumn: e.target.value })} />
                                )}
                            </label>
                            <label><span>Check every (minutes)</span>
                                <input type="number" min={1} value={t.everyMinutes} onChange={(e) => setT({ everyMinutes: e.target.value })} /></label>
                        </>
                    )}
                </div>

                <div className="sect">
                    <h5>Who can observe?</h5>
                    <UserPicker value={state.observers || []} fields={availableFields(state, 0)}
                        onChange={(observers) => change((s) => ({ ...s, observers }))} />
                    <p className="muted small">
                        Observers can open every request of this process and follow it, but cannot act on it. They get an email when a request starts.
                    </p>
                </div>

                <div className="sect">
                    <h5>Who is the requester?</h5>
                    <p className="muted small">
                        The person the request is for. They can open it and its documents, but cannot act on it.
                        Pick a field that holds a username or email, such as the creator.
                    </p>
                    <UserPicker value={state.requesters || []} fields={availableFields(state, state.steps.length)}
                        onChange={(requesters) => change((s) => ({ ...s, requesters }))} />
                    <p className="muted small">
                        If your data only has an ID, add a <b>Fetch data</b> step first that looks up the username from your user table,
                        then pick that username here. You can also type <code>{"{{USERNAME}}"}</code> and press Enter.
                    </p>
                    {!state.isNew && (state.requesters || []).length > 0 &&
                        <ApplyRequesters processKey={state.key} disabled={dirty} />}
                </div>
            </div>
        </div>
    );
}

export function StepProps({ step, index, state, change, connections }) {
    const patch = (p) => change((s) => ({ ...s, steps: s.steps.map((x) => (x.uid === step.uid ? { ...x, ...p } : x)) }));
    const fields = availableFields(state, index);
    const meta = STEP_TYPES[step.type];
    const conn = connections.find((c) => c.name === step.connection);

    return (
        <div>
            <div className="phead" style={{ "--c": meta.color }}>
                <span className="badge-ico"><Icon name={meta.icon} size={18} /></span>
                <div><small>{meta.label}</small><b>{step.name || "Untitled step"}</b></div>
            </div>
            <div className="form">
                <label><span>Step name</span>
                    <input value={step.name} onChange={(e) => patch({ name: e.target.value })} /></label>
                {step.type === "Upload" && (
                    <p className="muted">The person uploads a file here (PDF, image, Word, Excel, CSV or text, up to 20 MB). The request waits until a file is uploaded.</p>
                )}
                {step.type === "Form" && (
                    <>
                        {(step.type === "Form" || step.type === "Upload") && (
                            <div className="sect">
                                <h5>Who does this?</h5>
                                <UserPicker value={step.assignees} onChange={(a) => patch({ assignees: a })} fields={fields} />
                                <p className="muted small">
                                    Leave empty to let anyone open it. Assigned people get an email and see it under My Tasks.
                                </p>
                            </div>
                        )}
                        <FieldBuilder fields={step.fields} onChange={(f) => patch({ fields: f })} />
                        <label className="inline">
                            <input type="checkbox" checked={step.allowUpload} onChange={(e) => patch({ allowUpload: e.target.checked })} />
                            Let the person attach a file to this form
                        </label>
                        <FormPreview fields={step.fields} allowUpload={step.allowUpload} />
                    </>
                )}

                {step.type === "Approval" && (
                    <>
                        <label><span>Who can approve?</span></label>
                        <UserPicker value={step.approvers} onChange={(a) => patch({ approvers: a })} fields={fields} />
                        <p className="muted small">Everyone listed gets an email. One approval completes the step; one rejection stops the request.</p>
                    </>
                )}

                {(step.type === "Dispatch" || step.type === "Notify") && (
                    <>
                        <label><span>Send to (separate several with ;)</span>
                            <PlaceholderInput value={step.sendTo} onChange={(v) => patch({ sendTo: v })} fields={fields}
                                placeholder="supplier@example.com" /></label>
                        <label><span>Subject{step.type === "Dispatch" ? " (optional)" : ""}</span>
                            <PlaceholderInput value={step.subject} onChange={(v) => patch({ subject: v })} fields={fields} /></label>
                        <label><span>Message (optional)</span>
                            <PlaceholderInput multiline value={step.body} onChange={(v) => patch({ body: v })} fields={fields}
                                placeholder="Please book {{from}} to {{to}}" /></label>
                        {step.type === "Dispatch" && (
                            <p className="muted small">Leave the message empty to email everything collected so far as a table.</p>
                        )}
                    </>
                )}


                {step.type === "Upload" && (
                    <p className="muted">The person uploads a file here (PDF, image, Word, Excel, CSV or text, up to 20 MB), for example a booking confirmation. The request waits until a file is uploaded.</p>
                )}
                {step.type === "Link" && (
                    <>
                        <label><span>Link text</span>
                            <PlaceholderInput value={step.linkLabel} onChange={(v) => patch({ linkLabel: v })} fields={fields}
                                placeholder="Open the report" /></label>
                        <label><span>Link address (URL)</span>
                            <PlaceholderInput multiline rows={4} value={step.url} onChange={(v) => patch({ url: v })} fields={fields}
                                placeholder="https://google.com" /></label>
                        <LinkHint url={step.url} fields={fields} />
                        <p className="muted small">
                            Later steps can use this link as <code>{`{{${step.key}.url}}`}</code>, for example in an email.
                        </p>
                    </>
                )}
                {step.type === "DataFetch" && (
                    <>
                        <label><span>Data source</span>
                            <select value={step.connection} onChange={(e) => patch({ connection: e.target.value, schema: "", columns: [] })}>
                                <option value="">-- choose --</option>
                                {connections.map((c) => <option key={c.name} value={c.name}>{c.name} ({c.type})</option>)}
                            </select></label>
                        {connections.length === 0 && <p className="muted small">No data sources yet. Add one under Connections.</p>}
                        <SchemaSelect connection={step.connection} value={step.schema} onChange={(v) => patch({ schema: v })} fields={fields} />
                        <label><span>Query</span>
                            <textarea className="code" rows={4} value={step.query} onChange={(e) => patch({ query: e.target.value })} /></label>
                        <p className="muted small">{queryHint(conn?.type)} The first row returned becomes available to later steps.</p>
                        {step.connection && step.query && (
                            <TestQuery connection={step.connection} schema={step.schema} query={step.query}
                                onColumns={(cols) => patch({ columns: cols })} />
                        )}
                    </>
                )}

                <details className="sect">
                    <summary>Advanced</summary>
                    <label><span>Step key</span>
                        <input value={step.key} onChange={(e) => patch({ key: e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, "-") })} /></label>
                    <p className="muted small">
                        Used in placeholders such as <code>{`{{${step.key}.fieldname}}`}</code>. If you change it, update any placeholder that uses it.
                    </p>
                </details>
            </div>
        </div>
    );
}