import { useEffect, useMemo, useState } from "react";
import { api } from "../api";
import ApiFields, { apiPayload, defaultApi } from "../ApiFields";
import { Modal, useDialogs } from "../Dialogs";
import Icon from "../designer/Icon";
import { ErrorBox } from "../ui";

const SCHEMA_TYPES = ["Oracle", "PostgreSql"];
const KIND_ICON = { server: "database", file: "form", api: "plug" };

/* ------------------------------------------------------------ add / edit */
function ConnectionForm({ initial, types, onClose, onSaved }) {
    const edit = !!initial;
    const [name, setName] = useState(initial?.name || "");
    const [type, setType] = useState(initial?.type || "Oracle");
    const [connStr, setConnStr] = useState("");
    const [filePath, setFilePath] = useState(initial?.filePath || "");
    const [delimiter, setDelimiter] = useState(initial?.options?.delimiter || ",");
    const [hasHeader, setHasHeader] = useState(initial?.options?.hasHeader ?? true);
    const [desc, setDesc] = useState(initial?.description || "");
    const [active, setActive] = useState(initial?.isActive ?? true);
    const [defSchema, setDefSchema] = useState(initial?.defaultSchema || "");
    const [allowed, setAllowed] = useState((initial?.allowedSchemas || []).join(", "));
    const [apiCfg, setApiCfg] = useState(defaultApi());
    const [changeApi, setChangeApi] = useState(!edit);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    const t = types.find((x) => x.type === type);
    const kind = t?.kind;
    const schemaCapable = SCHEMA_TYPES.includes(type);

    const save = async () => {
        setBusy(true); setError(null);
        const body = {
            name, type, description: desc.trim() || undefined, isActive: active,
            filePath: kind === "file" || kind === "api" ? filePath.trim() : undefined,
            connectionString:
                kind === "server" ? (connStr.trim() || undefined)
                    : kind === "api" && changeApi ? JSON.stringify(apiPayload(apiCfg)) : undefined,
            options: type === "Csv" ? { delimiter, hasHeader } : undefined,
            defaultSchema: schemaCapable ? defSchema.trim() || undefined : undefined,
            allowedSchemas: schemaCapable ? allowed.split(",").map((s) => s.trim()).filter(Boolean) : undefined,
        };
        try {
            if (edit) await api.put(`/api/connections/${encodeURIComponent(initial.name)}`, body);
            else await api.post("/api/connections", body);
            onSaved();
            onClose();
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    return (
        <Modal title={edit ? `Edit ${initial.name}` : "Add a connection"} onClose={onClose}
            footer={
                <>
                    <button className="secondary" onClick={onClose}>Cancel</button>
                    <button disabled={busy || !name || !type} onClick={save}>{busy ? "Saving..." : "Save connection"}</button>
                </>
            }>
            <ErrorBox error={error} />
            <div className="form" style={{ maxWidth: "none" }}>
                <label><span>Name (letters, numbers, _ and -)</span>
                    <input autoFocus={!edit} disabled={edit} value={name} onChange={(e) => setName(e.target.value)} /></label>
                <label><span>Type</span>
                    <select value={type} disabled={edit} onChange={(e) => setType(e.target.value)}>
                        {types.map((x) => <option key={x.type} value={x.type}>{x.label}</option>)}
                    </select></label>

                {kind === "server" && (
                    <label><span>Connection string{edit && " (leave empty to keep the saved one)"}</span>
                        <textarea rows={2} placeholder={edit ? "Saved, not shown" : t?.example} value={connStr}
                            onChange={(e) => setConnStr(e.target.value)} /></label>
                )}
                {kind === "file" && (
                    <label><span>File path</span>
                        <input placeholder={t?.example} value={filePath} onChange={(e) => setFilePath(e.target.value)} /></label>
                )}
                {kind === "api" && (
                    <>
                        {edit && (
                            <label className="inline">
                                <input type="checkbox" checked={changeApi} onChange={(e) => setChangeApi(e.target.checked)} />
                                Change the sign-in settings (otherwise the saved ones stay)
                            </label>
                        )}
                        {changeApi
                            ? <ApiFields baseUrl={filePath} setBaseUrl={setFilePath} cfg={apiCfg} setCfg={setApiCfg} />
                            : <label><span>Base address</span><input value={filePath} onChange={(e) => setFilePath(e.target.value)} /></label>}
                    </>
                )}

                {schemaCapable && (
                    <>
                        <label><span>Allowed schemas (comma separated; empty = every schema this user can see)</span>
                            <input placeholder="HR, SALES" value={allowed} onChange={(e) => setAllowed(e.target.value)} /></label>
                        <label><span>Default schema (optional)</span>
                            <input placeholder="HR" value={defSchema} onChange={(e) => setDefSchema(e.target.value)} /></label>
                    </>
                )}
                {type === "Csv" && (
                    <>
                        <label><span>Delimiter (type "tab" for tab-separated)</span>
                            <input value={delimiter} onChange={(e) => setDelimiter(e.target.value)} /></label>
                        <label className="inline"><input type="checkbox" checked={hasHeader} onChange={(e) => setHasHeader(e.target.checked)} />
                            The first row is a header</label>
                    </>
                )}

                <label><span>Description</span><input value={desc} onChange={(e) => setDesc(e.target.value)} /></label>
                {edit && (
                    <label className="inline"><input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} />
                        Active (a disabled connection cannot be queried)</label>
                )}
                {t && <p className="muted small" style={{ margin: 0 }}>Query help: {t.queryHelp}</p>}
            </div>
        </Modal>
    );
}

/* ------------------------------------------------------------ browse and query */
function Browser({ conn, types, onClose }) {
    const t = types.find((x) => x.type === conn.type);
    const canSchema = SCHEMA_TYPES.includes(conn.type);
    const base = `/api/connections/${encodeURIComponent(conn.name)}`;

    const [schemas, setSchemas] = useState([]);
    const [schema, setSchema] = useState("");
    const [tables, setTables] = useState([]);
    const [columns, setColumns] = useState(null);
    const [query, setQuery] = useState("");
    const [params, setParams] = useState("");
    const [out, setOut] = useState(null);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    useEffect(() => {
        if (!canSchema) return;
        api.get(`${base}/schemas`)
            .then((r) => { setSchemas(r.schemas); setSchema(r.defaultSchema || ""); })
            .catch(setError);
    }, [base, canSchema]);

    useEffect(() => {
        setTables([]); setColumns(null);
        const url = canSchema && schema ? `${base}/schemas/${encodeURIComponent(schema)}/tables` : `${base}/tables`;
        api.get(url).then(setTables).catch(setError);
    }, [base, canSchema, schema]);

    const pickTable = async (tb) => {
        if (conn.type === "Json") setQuery(tb.startsWith("$.") ? tb : "");
        else if (conn.type === "Csv") setQuery("");
        else setQuery(`SELECT * FROM ${tb}`);
        setColumns(null);
        if (canSchema && schema) {
            try { setColumns(await api.get(`${base}/schemas/${encodeURIComponent(schema)}/tables/${encodeURIComponent(tb)}/columns`)); }
            catch (e) { setError(e); }
        }
    };

    const run = async () => {
        setBusy(true); setError(null);
        try {
            const body = { query, maxRows: 100, schema: schema || undefined };
            if (params.trim()) body.parameters = JSON.parse(params);
            setOut(await api.post(`${base}/query`, body));
        } catch (e) {
            setOut(null);
            setError(e instanceof SyntaxError ? new Error("The parameters are not valid JSON.") : e);
        } finally { setBusy(false); }
    };

    return (
        <Modal wide title={`Browse and query: ${conn.name}`} onClose={onClose}>
            <ErrorBox error={error} />
            <div className="browse">
                <div className="form" style={{ maxWidth: "none" }}>
                    {canSchema && (
                        <label><span>Schema</span>
                            <select value={schema} onChange={(e) => setSchema(e.target.value)}>
                                <option value="">(the connection's own schema)</option>
                                {schemas.map((s) => <option key={s} value={s}>{s}</option>)}
                            </select></label>
                    )}
                    {tables.length > 0 && (
                        <label><span>Table ({tables.length})</span>
                            <select value="" onChange={(e) => e.target.value && pickTable(e.target.value)}>
                                <option value="">Pick one to fill in the query…</option>
                                {tables.map((x) => <option key={x} value={x}>{x}</option>)}
                            </select></label>
                    )}
                    {columns && (
                        <p className="muted small" style={{ margin: 0 }}>
                            {columns.map((c) => `${c.name} (${c.dataType}${c.nullable ? "" : ", not null"})`).join(" · ")}
                        </p>
                    )}
                    <label><span>Query</span>
                        <textarea className="code" rows={4} value={query} onChange={(e) => setQuery(e.target.value)} /></label>
                    {t && <p className="muted small" style={{ margin: 0 }}>{t.queryHelp}</p>}
                    <label><span>Parameters (JSON, optional) e.g. {`{"status": "NEW"}`}</span>
                        <input value={params} onChange={(e) => setParams(e.target.value)} /></label>
                    <div className="actions">
                        <button disabled={busy || !query.trim() && conn.type !== "Json" && conn.type !== "Csv"} onClick={run}>
                            {busy ? "Running..." : "Run"}
                        </button>
                    </div>
                </div>

                {out && (
                    <>
                        <p className="muted small">
                            {out.count} row(s){out.truncated && " (limited to the first 100)"}{out.schema && ` · schema ${out.schema}`}
                        </p>
                        <div className="scroll">
                            <table>
                                <thead><tr>{out.columns.map((c) => <th key={c}>{c}</th>)}</tr></thead>
                                <tbody>
                                    {out.rows.map((r, i) => <tr key={i}>{out.columns.map((c) => <td key={c}>{String(r[c] ?? "")}</td>)}</tr>)}
                                </tbody>
                            </table>
                        </div>
                    </>
                )}
            </div>
        </Modal>
    );
}

/* ------------------------------------------------------------ page */
export default function Connections() {
    const { confirm } = useDialogs();
    const [types, setTypes] = useState([]);
    const [list, setList] = useState(null);
    const [error, setError] = useState(null);
    const [q, setQ] = useState("");
    const [tests, setTests] = useState({});
    const [editing, setEditing] = useState(null);     // null | "new" | a connection
    const [browsing, setBrowsing] = useState(null);

    const load = () => api.get("/api/connections").then((l) => { setList(l); setError(null); }).catch((e) => { setList([]); setError(e); });
    useEffect(() => {
        api.get("/api/connection-types").then(setTypes).catch(setError);
        load();
    }, []);

    const typeOf = (c) => types.find((x) => x.type === c.type);

    const test = async (c) => {
        setTests((t) => ({ ...t, [c.name]: { busy: true } }));
        try {
            const r = await api.post(`/api/connections/${encodeURIComponent(c.name)}/test`);
            setTests((t) => ({ ...t, [c.name]: { ok: r.success, message: r.message } }));
        } catch (e) { setTests((t) => ({ ...t, [c.name]: { ok: false, message: e.message } })); }
    };

    const remove = async (c) => {
        const ok = await confirm(
            `Delete the connection ${c.name}? Processes that use it will fail until it is added again.`,
            { title: "Delete connection", okLabel: "Delete", danger: true });
        if (!ok) return;
        try { await api.del(`/api/connections/${encodeURIComponent(c.name)}`); load(); } catch (e) { setError(e); }
    };

    const rows = useMemo(() => {
        const t = q.trim().toLowerCase();
        return (list || []).filter((c) => !t || c.name.toLowerCase().includes(t) || c.type.toLowerCase().includes(t));
    }, [list, q]);

    return (
        <>
            <div className="page-head">
                <div><h2>Connections</h2><p>The databases, files and APIs that processes can read from.</p></div>
                <button onClick={() => setEditing("new")}>+ Add connection</button>
            </div>

            <ErrorBox error={error} />

            <div className="filterbar">
                <div className="search"><Icon name="search" size={16} />
                    <input placeholder="Search connections" value={q} onChange={(e) => setQ(e.target.value)} /></div>
            </div>

            {list && rows.length === 0 && (
                <div className="empty"><b>{list.length ? "No connection matches" : "No connections yet"}</b>
                    {list.length ? "Try another search." : "Add the first one with “+ Add connection”."}</div>
            )}

            <div className="cgrid">
                {rows.map((c) => {
                    const t = typeOf(c);
                    const r = tests[c.name];
                    return (
                        <div key={c.id} className={`ccard${c.isActive ? "" : " off"}`}>
                            <div className="chd">
                                <span className="pbadge" style={{ "--c": "#4f46e5" }}><Icon name={KIND_ICON[t?.kind] || "database"} size={20} /></span>
                                <div className="ctitle"><b title={c.name}>{c.name}</b><small>{t?.label || c.type}</small></div>
                                <span className={`badge ${c.isActive ? "Completed" : "Cancelled"}`}>{c.isActive ? "Active" : "Disabled"}</span>
                            </div>

                            <div className="csrc">
                                {c.filePath || (c.hasSecret ? "Connection string saved (hidden)" : "No connection string")}
                            </div>
                            {c.description && <p className="muted small" style={{ margin: 0 }}>{c.description}</p>}
                            {(c.defaultSchema || c.allowedSchemas?.length > 0) && (
                                <div className="chips">
                                    {c.defaultSchema && <span className="chip">default: {c.defaultSchema}</span>}
                                    {c.allowedSchemas?.map((s) => <span key={s} className="chip">{s}</span>)}
                                </div>
                            )}

                            {r && !r.busy && <div className={`tres ${r.ok ? "ok" : "bad"}`}>{r.ok ? "✓ " : "✕ "}{r.message}</div>}

                            <div className="cfoot">
                                <button className="secondary" disabled={r?.busy} onClick={() => test(c)}>{r?.busy ? "Testing..." : "Test"}</button>
                                <button className="secondary" disabled={!c.isActive} onClick={() => setBrowsing(c)}>Browse</button>
                                <button className="secondary" onClick={() => setEditing(c)}>Edit</button>
                                <button className="danger" onClick={() => remove(c)}>Delete</button>
                            </div>
                        </div>
                    );
                })}
            </div>

            {editing && (
                <ConnectionForm initial={editing === "new" ? null : editing} types={types}
                    onClose={() => setEditing(null)} onSaved={load} />
            )}
            {browsing && <Browser conn={browsing} types={types} onClose={() => setBrowsing(null)} />}
        </>
    );
}