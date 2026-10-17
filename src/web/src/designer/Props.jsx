import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import { DropZone, FIELD_MIME, endDrag, startDrag } from "./dnd";
import {
    FIELD_ORDER, FIELD_TYPES, STEP_TYPES, availableFields, isEmail, isPlaceholder,
    newField, slugKey, slugName, unique, placeholderNames, uid, OPS, condSummary, invertConditions, emptyNotify, lanesCount, removeLane
} from "./model";
import Icon from "./Icon";
import UserPicker from "../UserPicker";
import { buildLink } from "../links";
import { useDialogs } from "../Dialogs";

const queryHint = (type) =>
    type === "Json" ? "JSONPath, for example $.requests[?(@.status=='new')]"
        : type === "Csv" ? "A filter, for example status = new AND amount > 1000"
            : type === "Api" ? "First line: GET /path?x=:value. Optional: a line  -> $.data  for the rows, headers (Name: value), a blank line, then a JSON body for POST."
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

function LinksEditor({ links, onChange, fields }) {
    const [sample, setSample] = useState('{"id": 41}');
    let data = {};
    let sampleErr = "";
    try { data = JSON.parse(sample || "{}"); } catch { sampleErr = "The sample is not valid JSON."; }

    const patch = (id, p) => onChange(links.map((l) => (l.uid === id ? { ...l, ...p } : l)));

    return (
        <div className="sect">
            <h5>Document links</h5>
            <p className="muted small">
                Links shown on every request of this process, filled in from its data. Use <code>{"{{id}}"}</code> for a
                value and <code>{"{{id+1}}"}</code> or <code>{"{{id-2}}"}</code> to add or subtract a whole number.
                Pick a field with “Insert field…”, then type the +1 yourself.
            </p>

            {links.map((l) => {
                const r = buildLink(l.url, data);
                const hostOk = /^https?:\/\/[^/?#{}\s]+(?=[/?#]|$)/i.test(l.url.trim());
                return (
                    <div key={l.uid} className="linkedit">
                        <label><span>Link text</span>
                            <input value={l.label} placeholder="Open the report" onChange={(e) => patch(l.uid, { label: e.target.value })} /></label>
                        <label><span>Address (URL)</span>
                            <PlaceholderInput multiline rows={3} value={l.url} fields={fields}
                                onChange={(v) => patch(l.uid, { url: v })}
                                placeholder="https://meaisrv014:9012/Document/PrintReport/{{id}}?mid={{id+1}}&sid={{id+2}}&Type=M" /></label>
                        {l.url.trim() && !hostOk && (
                            <div className="field-err">Start with http:// or https:// and a fixed server name (no {"{{ }}"} in the server part).</div>
                        )}
                        {l.url.trim() && hostOk && !sampleErr && (
                            <div className="small">
                                {r.valid
                                    ? <>Preview: <code className="wrap">{r.url}</code></>
                                    : <span className="field-err">Preview needs a value for: {r.missing.join(", ")}</span>}
                            </div>
                        )}
                        <button type="button" className="danger" onClick={() => onChange(links.filter((x) => x.uid !== l.uid))}>
                            Remove link
                        </button>
                    </div>
                );
            })}

            <button type="button" className="secondary" onClick={() => onChange([...links, { uid: uid(), label: "", url: "" }])}>
                + Add a link
            </button>

            {links.length > 0 && (
                <label><span>Try it with sample data (JSON)</span>
                    <input value={sample} onChange={(e) => setSample(e.target.value)} /></label>
            )}
            {sampleErr && <div className="field-err">{sampleErr}</div>}
        </div>
    );
}

export function LinksProps({ state, change }) {
    const fields = availableFields(state, state.steps.length);
    return (
        <div>
            <div className="phead" style={{ "--c": "#0d9488" }}>
                <span className="badge-ico"><Icon name="link" size={18} /></span>
                <div><small>Request page</small><b>Document links</b></div>
            </div>
            <div className="form">
                <LinksEditor links={state.links || []} fields={fields}
                    onChange={(links) => change((s) => ({ ...s, links }))} />
            </div>
        </div>
    );
}


function NotifyEditor({ state, change, fields }) {
    const events = [
        ["fileUploaded", "When a file is uploaded", "A file added in a form or upload step."],
        ["requestEnded", "When the request ends", "Completed, rejected or cancelled."],
    ];
    const cur = () => state.notify || emptyNotify();
    const set = (k, p) => change((s) => {
        const base = s.notify || emptyNotify();
        return { ...s, notify: { ...base, [k]: { ...base[k], ...p } } };
    });
    return (
        <div className="sect">
            <h5>Send emails when…</h5>
            {events.map(([k, label, hint]) => {
                const r = cur()[k];
                return (
                    <div key={k} className="nrule">
                        <label className="inline"><input type="checkbox" checked={r.on} onChange={(e) => set(k, { on: e.target.checked })} /><b>{label}</b></label>
                        <small className="muted">{hint}</small>
                        {r.on && (
                            <>
                                <label className="inline"><input type="checkbox" checked={r.toRequester} onChange={(e) => set(k, { toRequester: e.target.checked })} />The requester</label>
                                <label className="inline"><input type="checkbox" checked={r.toStarter} onChange={(e) => set(k, { toStarter: e.target.checked })} />The person who started it</label>
                                <UserPicker value={r.to} fields={fields} onChange={(to) => set(k, { to })} placeholder="Add other people or groups…" />
                                <label><span>Extra message (optional)</span>
                                    <PlaceholderInput multiline rows={2} value={r.message} fields={fields} onChange={(m) => set(k, { message: m })} /></label>
                            </>
                        )}
                    </div>
                );
            })}
            <p className="muted small">A requester is emailed only when an address can be found: an email in the data, or their company account.</p>
        </div>
    );
}
export function ProcessProps({ state, change, connections, dirty }) {
    const t = state.trigger;
    const fields = availableFields(state, state.steps.length);
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
                <NotifyEditor state={state} change={change} fields={availableFields(state, state.steps.length)} />
            </div>
            <LinksEditor links={state.links || []} fields={fields}
                onChange={(links) => change((s) => ({ ...s, links }))} />
        </div>
    );
}

function ConditionsEditor({ title, value, fields, steps = [], index = 0, allowOpposite, onChange, help,
    emptyText = "No condition: this step always runs." }) {
    const rc = value || { match: "all", conditions: [] };
    const set = (p) => onChange({ ...rc, ...p });
    const known = new Set(fields.map((f) => f.name.toLowerCase()));
    const earlier = allowOpposite ? steps.slice(0, index).filter((s) => s.runIf?.conditions?.length) : [];
    const patch = (id, p) => set({ conditions: rc.conditions.map((c) => (c.uid === id ? { ...c, ...p } : c)) });

    return (
        <div className="sect">
            <h5>{title}</h5>
            <datalist id="fl-cond">
                {fields.map((f) => <option key={f.name} value={f.name}>{f.label} ({f.source})</option>)}
            </datalist>

            {rc.conditions.length === 0 && <p className="muted small">{emptyText}</p>}

            {rc.conditions.length > 1 && (
                <select value={rc.match} onChange={(e) => set({ match: e.target.value })}>
                    <option value="all">All of these are true</option>
                    <option value="any">Any of these is true</option>
                </select>
            )}

            {rc.conditions.map((c) => {
                const unknown = c.field.trim() && !known.has(c.field.trim().toLowerCase()) && !c.field.includes(".");
                return (
                    <div key={c.uid} className="cond">
                        <input list="fl-cond" value={c.field} placeholder="field, e.g. amount"
                            onChange={(e) => patch(c.uid, { field: e.target.value })} />
                        <select value={c.op} onChange={(e) => patch(c.uid, { op: e.target.value })}>
                            {Object.entries(OPS).map(([k, o]) => <option key={k} value={k}>{o.label}</option>)}
                        </select>
                        {OPS[c.op]?.needsValue && (
                            <input value={c.value} placeholder="value, or a {{field}}"
                                onChange={(e) => patch(c.uid, { value: e.target.value })} />
                        )}
                        <button type="button" className="icon danger" title="Remove"
                            onClick={() => set({ conditions: rc.conditions.filter((x) => x.uid !== c.uid) })}>
                            <Icon name="x" size={14} />
                        </button>
                        {unknown && (
                            <div className="field-err" style={{ flexBasis: "100%" }}>
                                Nothing collected before this step is called “{c.field}”. Ignore this if it comes from the starting data.
                            </div>
                        )}
                    </div>
                );
            })}

            <div className="actions">
                <button type="button" className="secondary"
                    onClick={() => set({ conditions: [...rc.conditions, { uid: uid(), field: "", op: "eq", value: "" }] })}>
                    + Add a condition
                </button>
                {rc.conditions.length > 0 && (
                    <button type="button" className="secondary" onClick={() => set({ conditions: [] })}>Remove all</button>
                )}
            </div>

            {earlier.length > 0 && (
                <select value="" onChange={(e) => {
                    const s = steps.find((x) => x.uid === e.target.value);
                    if (s) onChange(invertConditions(s.runIf));
                }}>
                    <option value="">Use the opposite (else) of an earlier step…</option>
                    {earlier.map((s) => <option key={s.uid} value={s.uid}>{s.name}: {condSummary(s.runIf)}</option>)}
                </select>
            )}

            <p className="muted small">{help}</p>
        </div>
    );
}

function ScheduleEditor({ step, patch, fields }) {
    const unit = (key, ukey, ph = "none") => (
        <div className="dur">
            <input type="number" min={0} value={step[key]} placeholder={ph} onChange={(e) => patch({ [key]: e.target.value })} />
            <select value={step[ukey]} onChange={(e) => patch({ [ukey]: e.target.value })}>
                <option value="hours">hours</option><option value="days">days</option>
            </select>
        </div>
    );
    const hasDue = Number(step.dueN) > 0;
    return (
        <div className="sect">
            <h5>Deadline and reminders</h5>
            <label><span>Due after the step starts waiting</span>{unit("dueN", "dueUnit")}</label>
            <label><span>Remind the people responsible every</span>{unit("remN", "remUnit")}</label>
            <p className="muted small">
                Overdue tasks turn red in My Tasks and Requests. Reminders are emailed while the step is waiting (at most 10).
                Leave a box empty for none.
            </p>

            <label className="inline">
                <input type="checkbox" checked={!!step.escOn} disabled={!hasDue}
                    onChange={(e) => patch({ escOn: e.target.checked })} />
                Escalate when the deadline is missed
            </label>
            {!hasDue && <p className="muted small">Set a deadline first to switch escalation on.</p>}

            {step.escOn && hasDue && (
                <div className="nrule">
                    <label><span>Escalate this long after the deadline (empty = at the deadline)</span>
                        {unit("escN", "escUnit", "at the deadline")}</label>
                    <label><span>What happens</span>
                        <select value={step.escAction} onChange={(e) => patch({ escAction: e.target.value })}>
                            <option value="forward">Forward the task to someone else (for example a manager)</option>
                            <option value="reject">Reject the request automatically</option>
                            <option value="notify">Only tell someone</option>
                        </select></label>
                    {step.escAction !== "reject" && (
                        <label><span>{step.escAction === "forward" ? "Forward to" : "Tell"}</span>
                            <UserPicker value={step.escTo || []} fields={fields} onChange={(escTo) => patch({ escTo })} /></label>
                    )}
                    <label><span>Message (optional)</span>
                        <PlaceholderInput multiline rows={2} value={step.escMessage || ""} fields={fields}
                            onChange={(v) => patch({ escMessage: v })} /></label>
                    <p className="muted small">
                        {step.escAction === "forward" && "The people who had the task are replaced by the people above, and get an email about it. A person who is already on the step is not added again."}
                        {step.escAction === "reject" && "The request is rejected and closed. Every step still open is skipped, and the “request ended” emails go out."}
                        {step.escAction === "notify" && "The task stays where it is. The people above get an email."}
                    </p>
                </div>
            )}
        </div>
    );
}

function AttachEditor({ step, steps, index, patch }) {
    const sources = steps.slice(0, index).filter((s) => s.type === "Form" || s.type === "Upload");
    const toggle = (key) =>
        patch({ attachFrom: step.attachFrom.includes(key) ? step.attachFrom.filter((k) => k !== key) : [...step.attachFrom, key] });
    return (
        <div className="sect">
            <label className="inline">
                <input type="checkbox" checked={!!step.attachFiles} onChange={(e) => patch({ attachFiles: e.target.checked })} />
                Attach the files that were uploaded
            </label>
            {step.attachFiles && (
                <>
                    <p className="muted small">
                        {step.attachFrom.length ? "Only files from the steps ticked below." : "Files from every earlier step. Tick steps to limit it."}
                    </p>
                    {sources.map((s) => (
                        <label key={s.uid} className="checkrow">
                            <input type="checkbox" checked={step.attachFrom.includes(s.key)} onChange={() => toggle(s.key)} />{s.name}
                        </label>
                    ))}
                    <p className="muted small">Up to 10 files and 15 MB together. Your mail server may allow less.</p>
                </>
            )}
        </div>
    );
}

function LanesEditor({ step, steps, patch, change }) {
    const { confirm } = useDialogs();
    const lanes = step.lanes || [];
    const add = () => {
        if (lanes.length >= 6) return;
        let n = lanes.length + 1;
        while (lanes.some((l) => l.id === `l${n}`)) n++;
        patch({ lanes: [...lanes, { id: `l${n}`, name: `Path ${lanes.length + 1}` }] });
    };
    const drop = async (l) => {
        const inside = lanesCount(steps, step.uid, l.id);
        if (inside && !(await confirm(`Remove "${l.name}" and the ${inside} step(s) in it?`,
            { title: "Remove path", okLabel: "Remove", danger: true }))) return;
        change((s) => ({ ...s, steps: removeLane(s.steps, step.uid, l.id) }));
    };
    return (
        <div className="sect">
            <h5>Paths that run at the same time</h5>
            {lanes.map((l) => (
                <div key={l.id} className="wrow">
                    <input value={l.name} placeholder="e.g. Flight"
                        onChange={(e) => patch({ lanes: lanes.map((x) => (x.id === l.id ? { ...x, name: e.target.value } : x)) })} />
                    <button type="button" className="icon danger" disabled={lanes.length <= 2} title="Remove this path" onClick={() => drop(l)}>
                        <Icon name="trash" size={15} />
                    </button>
                </div>
            ))}
            <button type="button" className="secondary" disabled={lanes.length >= 6} onClick={add}>+ Add a path</button>
            <p className="muted small">
                Every path starts at the same moment. The steps after the block run when all paths are finished.
                An End step inside a path closes the whole request.
            </p>
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
                        <label className="inline">
                            <input type="checkbox" checked={!!step.requireAll} onChange={(e) => patch({ requireAll: e.target.checked })} />
                            Every approver must approve
                        </label>
                        <p className="muted small">
                            Everyone listed gets an email.{" "}
                            {step.requireAll
                                ? "The step finishes when all of them have approved. One rejection stops the request."
                                : "One approval completes the step. One rejection stops the request."}
                        </p>
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
                {step.type === "End" && (
                    <>
                        <label><span>How does the request end?</span>
                            <select value={step.outcome} onChange={(e) => patch({ outcome: e.target.value })}>
                                <option value="Completed">Completed (finished, nothing more to do)</option>
                                <option value="Rejected">Rejected (not accepted, or not eligible)</option>
                                <option value="Cancelled">Cancelled</option>
                            </select></label>
                        <label><span>Reason shown on the request (optional)</span>
                            <PlaceholderInput value={step.reason} onChange={(v) => patch({ reason: v })} fields={fields}
                                placeholder="Only domestic travel is handled by this process" /></label>
                        <p className="muted small">
                            When the request reaches this step it closes right away, and every step after it is skipped.
                            Put it in an If or Else lane to stop on that path only. To tell someone first, put a Notify step before it.
                        </p>
                    </>
                )}
                {["Form", "Upload", "Approval"].includes(step.type) && <ScheduleEditor step={step} patch={patch} fields={fields} />}
                {(step.type === "Dispatch" || step.type === "Notify") && (
                    <div className="sect">
                        <h5>Look of the email</h5>
                        <label><span>Style</span>
                            <select value={step.emailStyle || ""} onChange={(e) => patch({ emailStyle: e.target.value })}>
                                <option value="">Normal</option>
                                <option value="success">Good news (green)</option>
                                <option value="warning">Please note (amber)</option>
                                <option value="alert">Important (red)</option>
                            </select></label>
                        <label><span>Button that opens the request (empty = no button)</span>
                            <PlaceholderInput value={step.buttonLabel || ""} fields={fields}
                                onChange={(v) => patch({ buttonLabel: v })} placeholder="Open the request" /></label>
                        <p className="muted small">
                            {step.type === "Dispatch"
                                ? "Suppliers outside the company cannot open the application, so keep the button empty for them."
                                : "The message you write above appears inside this design, with line breaks and web addresses kept."}
                        </p>
                    </div>
                )}
                {(step.type === "Dispatch" || step.type === "Notify") &&
                    <AttachEditor step={step} steps={state.steps} index={index} patch={patch} />}
                {step.type === "Parallel" && <LanesEditor step={step} steps={state.steps} patch={patch} change={change} />}
                {step.type === "Decision" && (
                    <ConditionsEditor title="If…" value={step.condition} fields={fields}
                        emptyText="No condition yet."
                        onChange={(condition) => patch({ condition })}
                        help="When this is true, the steps in the “If true” lane run. Otherwise the steps in the “Else” lane run. Either lane can stay empty, and the steps after the block run on both paths." />
                )}

                {step.type !== "Decision" && step.branch && (
                    <div className="hintbar">
                        <Icon name="branch" size={16} />
                        <span>
                            This step is in the <b>{step.branch.path === "else" ? "Else" : "If true"}</b> lane of
                            “{state.steps.find((x) => x.uid === step.branch.of)?.name}”, so it runs only on that path.
                        </span>
                    </div>
                )}

                {step.type !== "Decision" && !step.branch && (
                    <ConditionsEditor title="Run this step only if…" value={step.runIf} fields={fields}
                        steps={state.steps} index={index} allowOpposite
                        onChange={(runIf) => patch({ runIf })}
                        help="If the answer is no, this step is skipped and the request moves on. For a real if / else with several steps, use an If / Else step instead." />
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