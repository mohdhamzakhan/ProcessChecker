import { useEffect, useState } from "react";
import { api } from "../api";
import { ErrorBox } from "../ui";
import ApiFields, { apiPayload, defaultApi } from "../ApiFields";

const SCHEMA_TYPES = ["Oracle", "PostgreSql"];

export default function Connections() {
    const [types, setTypes] = useState([]);
    const [list, setList] = useState([]);
    const [error, setError] = useState(null);
    const [result, setResult] = useState(null);

    // new connection
    const [name, setName] = useState("");
    const [type, setType] = useState("Oracle");
    const [connStr, setConnStr] = useState("");
    const [filePath, setFilePath] = useState("");
    const [delimiter, setDelimiter] = useState(",");
    const [hasHeader, setHasHeader] = useState(true);
    const [desc, setDesc] = useState("");
    const [defSchema, setDefSchema] = useState("");
    const [allowed, setAllowed] = useState("");

    // query runner
    const [qConn, setQConn] = useState("");
    const [schemas, setSchemas] = useState([]);
    const [schema, setSchema] = useState("");
    const [tables, setTables] = useState([]);
    const [columns, setColumns] = useState(null);
    const [query, setQuery] = useState("");
    const [params, setParams] = useState("");
    const [out, setOut] = useState(null);

    const [baseUrl, setBaseUrl] = useState("");
    const [apiCfg, setApiCfg] = useState(defaultApi());

    const load = () => api.get("/api/connections").then(setList).catch(setError);
    useEffect(() => {
        api.get("/api/connection-types").then(setTypes).catch(setError);
        load();
    }, []);

    const t = types.find((x) => x.type === type);
    const isFile = t?.kind === "file";
    const isApi = t?.kind === "api";
    const newSupportsSchemas = SCHEMA_TYPES.includes(type);

    const qc = list.find((c) => c.name === qConn);
    const qSupportsSchemas = !!qc && SCHEMA_TYPES.includes(qc.type);

    // 1. a connection is chosen: load its schemas
    useEffect(() => {
        setSchemas([]); setSchema(""); setTables([]); setColumns(null); setOut(null);
        if (!qc || !SCHEMA_TYPES.includes(qc.type)) return;
        api.get(`/api/connections/${qc.name}/schemas`)
            .then((r) => { setSchemas(r.schemas); setSchema(r.defaultSchema || ""); setError(null); })
            .catch(setError);
        // eslint-disable-next-line
    }, [qConn]);

    // 2. a schema is chosen (empty = the connection's own schema): load its tables
    useEffect(() => {
        setTables([]); setColumns(null);
        if (!qConn) return;
        const url = schema
            ? `/api/connections/${qConn}/schemas/${encodeURIComponent(schema)}/tables`
            : `/api/connections/${qConn}/tables`;
        api.get(url).then(setTables).catch(setError);
        // eslint-disable-next-line
    }, [qConn, schema]);

    const pickTable = async (tb) => {
        if (!qc) return;
        if (qc.type === "Json") setQuery(tb.startsWith("$.") ? tb : "");
        else if (qc.type === "Csv") setQuery("");
        else setQuery(`SELECT * FROM ${tb}`);
        setColumns(null);
        if (schema && qSupportsSchemas) {
            try {
                setColumns(await api.get(
                    `/api/connections/${qConn}/schemas/${encodeURIComponent(schema)}/tables/${encodeURIComponent(tb)}/columns`));
            } catch (e) { setError(e); }
        }
    };

    const save = async () => {
        try {
            await api.post("/api/connections", {
                name, type, description: desc || undefined,
                connectionString: isApi ? JSON.stringify(apiPayload(apiCfg)) : isFile ? undefined : connStr,
                filePath: isApi ? baseUrl.trim() : isFile ? filePath : undefined,
                options: type === "Csv" ? { delimiter, hasHeader } : undefined,
                defaultSchema: newSupportsSchemas && defSchema ? defSchema : undefined,
                allowedSchemas: newSupportsSchemas
                    ? allowed.split(",").map((s) => s.trim()).filter(Boolean) : undefined,
            });
            setName(""); setConnStr(""); setFilePath(""); setDesc(""); setDefSchema(""); setAllowed(""); setError(null); setBaseUrl(""); setApiCfg(defaultApi());
            load();
        } catch (e) { setError(e); }
    };

    const call = async (fn, label) => {
        try { setResult({ label, data: await fn() }); setError(null); } catch (e) { setError(e); }
    };

    const remove = async (n) => {
        if (!confirm(`Delete connection ${n}?`)) return;
        try { await api.del(`/api/connections/${n}`); load(); } catch (e) { setError(e); }
    };

    const run = async () => {
        try {
            const body = { query, maxRows: 100, schema: schema || undefined };
            if (params.trim()) body.parameters = JSON.parse(params);
            setOut(await api.post(`/api/connections/${qConn}/query`, body));
            setError(null);
        } catch (e) {
            setOut(null);
            setError(e instanceof SyntaxError ? new Error("Parameters are not valid JSON.") : e);
        }
    };

    return (
        <>
            <h2>Connections</h2>
            <ErrorBox error={error} />

            <table>
                <thead><tr><th>Name</th><th>Type</th><th>Source</th><th>Schemas</th><th>Active</th><th></th></tr></thead>
                <tbody>
                    {list.map((c) => (
                        <tr key={c.id}>
                            <td>{c.name}</td><td>{c.type}</td><td>{c.filePath || "(connection string)"}</td>
                            <td>
                                {c.defaultSchema && <div>default: <b>{c.defaultSchema}</b></div>}
                                {c.allowedSchemas?.length > 0 && <small>only: {c.allowedSchemas.join(", ")}</small>}
                            </td>
                            <td>{c.isActive ? "yes" : "no"}</td>
                            <td>
                                <button onClick={() => call(() => api.post(`/api/connections/${c.name}/test`), `Test ${c.name}`)}>Test</button>{" "}
                                <button className="danger" onClick={() => remove(c.name)}>Delete</button>
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>
            {result && <div className="panel"><b>{result.label}</b><pre>{JSON.stringify(result.data, null, 2)}</pre></div>}

            <div className="panel">
                <h3>Add a connection</h3>
                <div className="form">
                    <label><span>Name (letters, numbers, _ -)</span><input value={name} onChange={(e) => setName(e.target.value)} /></label>
                    <label><span>Type</span>
                        <select value={type} onChange={(e) => setType(e.target.value)}>
                            {types.map((x) => <option key={x.type} value={x.type}>{x.label}</option>)}
                        </select>
                    </label>
                    {isApi ? (
                        <ApiFields baseUrl={baseUrl} setBaseUrl={setBaseUrl} cfg={apiCfg} setCfg={setApiCfg} />
                    ) : isFile ? (
                        <label><span>File path</span>
                            <input placeholder={t?.example} value={filePath} onChange={(e) => setFilePath(e.target.value)} /></label>
                    ) : (
                        <label><span>Connection string</span>
                            <textarea rows={2} placeholder={t?.example} value={connStr} onChange={(e) => setConnStr(e.target.value)} /></label>
                    )}
                    {newSupportsSchemas && (
                        <>
                            <label><span>Allowed schemas (comma-separated, empty = every schema this user can see)</span>
                                <input placeholder="HR, SALES, FINANCE" value={allowed} onChange={(e) => setAllowed(e.target.value)} /></label>
                            <label><span>Default schema (optional)</span>
                                <input placeholder="HR" value={defSchema} onChange={(e) => setDefSchema(e.target.value)} /></label>
                        </>
                    )}
                    {type === "Csv" && (
                        <>
                            <label><span>Delimiter (use "tab" for tab-separated)</span>
                                <input value={delimiter} onChange={(e) => setDelimiter(e.target.value)} /></label>
                            <label><span>First row is a header</span>
                                <input type="checkbox" checked={hasHeader} onChange={(e) => setHasHeader(e.target.checked)} /></label>
                        </>
                    )}
                    <label><span>Description</span><input value={desc} onChange={(e) => setDesc(e.target.value)} /></label>
                    {t && <p className="muted">Query help: {t.queryHelp}</p>}
                    <button onClick={save}>Save connection</button>
                </div>
            </div>

            <div className="panel">
                <h3>Browse and run a query</h3>
                <div className="form">
                    <label><span>1. Connection</span>
                        <select value={qConn} onChange={(e) => setQConn(e.target.value)}>
                            <option value="">-- select --</option>
                            {list.map((c) => <option key={c.name} value={c.name}>{c.name} ({c.type})</option>)}
                        </select>
                    </label>

                    {qSupportsSchemas && (
                        <label><span>2. Schema</span>
                            <select value={schema} onChange={(e) => setSchema(e.target.value)}>
                                <option value="">(connection's own schema)</option>
                                {schemas.map((s) => <option key={s} value={s}>{s}</option>)}
                            </select>
                        </label>
                    )}

                    {tables.length > 0 && (
                        <label><span>{qSupportsSchemas ? "3." : "2."} Table ({tables.length})</span>
                            <select defaultValue="" onChange={(e) => e.target.value && pickTable(e.target.value)}>
                                <option value="">-- pick one to fill the query --</option>
                                {tables.map((x) => <option key={x} value={x}>{x}</option>)}
                            </select>
                        </label>
                    )}
                    {columns && (
                        <p className="muted">
                            Columns: {columns.map((c) => `${c.name} (${c.dataType}${c.nullable ? "" : ", not null"})`).join(" · ")}
                        </p>
                    )}

                    <label><span>Query</span>
                        <textarea className="code" rows={3} value={query} onChange={(e) => setQuery(e.target.value)} /></label>
                    <label><span>Parameters (JSON, optional) e.g. {`{"status": "NEW"}`}</span>
                        <input value={params} onChange={(e) => setParams(e.target.value)} /></label>
                    <button disabled={!qConn} onClick={run}>Run</button>
                </div>
                {out && (
                    <>
                        <p className="muted">
                            {out.count} row(s){out.truncated && " (truncated)"}{out.schema && ` · schema ${out.schema}`}
                        </p>
                        <div className="scroll">
                            <table>
                                <thead><tr>{out.columns.map((c) => <th key={c}>{c}</th>)}</tr></thead>
                                <tbody>
                                    {out.rows.map((r, i) => (
                                        <tr key={i}>{out.columns.map((c) => <td key={c}>{String(r[c] ?? "")}</td>)}</tr>
                                    ))}
                                </tbody>
                            </table>
                        </div>
                    </>
                )}
            </div>
        </>
    );
}