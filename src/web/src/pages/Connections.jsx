import { useEffect, useState } from "react";
import { api } from "../api";
import { ErrorBox } from "../ui";

export default function Connections() {
    const [types, setTypes] = useState([]);
    const [list, setList] = useState([]);
    const [error, setError] = useState(null);
    const [result, setResult] = useState(null);

    // new connection form
    const [name, setName] = useState("");
    const [type, setType] = useState("Oracle");
    const [connStr, setConnStr] = useState("");
    const [filePath, setFilePath] = useState("");
    const [delimiter, setDelimiter] = useState(",");
    const [hasHeader, setHasHeader] = useState(true);
    const [desc, setDesc] = useState("");

    // query runner
    const [qConn, setQConn] = useState("");
    const [query, setQuery] = useState("");
    const [params, setParams] = useState("");
    const [out, setOut] = useState(null);

    const load = () => api.get("/api/connections").then(setList).catch(setError);
    useEffect(() => {
        api.get("/api/connection-types").then(setTypes).catch(setError);
        load();
    }, []);

    const t = types.find((x) => x.type === type);
    const isFile = t?.kind === "file";

    const save = async () => {
        try {
            await api.post("/api/connections", {
                name, type, description: desc || undefined,
                connectionString: isFile ? undefined : connStr,
                filePath: isFile ? filePath : undefined,
                options: type === "Csv" ? { delimiter, hasHeader } : undefined,
            });
            setName(""); setConnStr(""); setFilePath(""); setDesc(""); setError(null);
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
            const body = { query, maxRows: 100 };
            if (params.trim()) body.parameters = JSON.parse(params);
            setOut(await api.post(`/api/connections/${qConn}/query`, body));
            setError(null);
        } catch (e) { setOut(null); setError(e instanceof SyntaxError ? new Error("Parameters are not valid JSON.") : e); }
    };

    return (
        <>
            <h2>Connections</h2>
            <ErrorBox error={error} />

            <table>
                <thead><tr><th>Name</th><th>Type</th><th>Source</th><th>Secret saved</th><th>Active</th><th></th></tr></thead>
                <tbody>
                    {list.map((c) => (
                        <tr key={c.id}>
                            <td>{c.name}</td><td>{c.type}</td><td>{c.filePath || "(connection string)"}</td>
                            <td>{c.hasSecret ? "yes" : "-"}</td><td>{c.isActive ? "yes" : "no"}</td>
                            <td>
                                <button onClick={() => call(() => api.post(`/api/connections/${c.name}/test`), `Test ${c.name}`)}>Test</button>{" "}
                                <button onClick={() => call(() => api.get(`/api/connections/${c.name}/tables`), `Tables of ${c.name}`)}>Tables</button>{" "}
                                <button className="danger" onClick={() => remove(c.name)}>Delete</button>
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>
            {result && (
                <div className="panel"><b>{result.label}</b><pre>{JSON.stringify(result.data, null, 2)}</pre></div>
            )}

            <div className="panel">
                <h3>Add a connection</h3>
                <div className="form">
                    <label><span>Name (letters, numbers, _ -)</span><input value={name} onChange={(e) => setName(e.target.value)} /></label>
                    <label><span>Type</span>
                        <select value={type} onChange={(e) => setType(e.target.value)}>
                            {types.map((x) => <option key={x.type} value={x.type}>{x.label}</option>)}
                        </select>
                    </label>
                    {isFile ? (
                        <label><span>File path</span>
                            <input placeholder={t?.example} value={filePath} onChange={(e) => setFilePath(e.target.value)} /></label>
                    ) : (
                        <label><span>Connection string</span>
                            <textarea rows={2} placeholder={t?.example} value={connStr} onChange={(e) => setConnStr(e.target.value)} /></label>
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
                <h3>Run a query</h3>
                <div className="form">
                    <label><span>Connection</span>
                        <select value={qConn} onChange={(e) => setQConn(e.target.value)}>
                            <option value="">-- select --</option>
                            {list.map((c) => <option key={c.name} value={c.name}>{c.name} ({c.type})</option>)}
                        </select>
                    </label>
                    <label><span>Query</span>
                        <textarea className="code" rows={3} value={query} onChange={(e) => setQuery(e.target.value)} /></label>
                    <label><span>Parameters (JSON, optional) e.g. {`{"status": "NEW"}`}</span>
                        <input value={params} onChange={(e) => setParams(e.target.value)} /></label>
                    <button disabled={!qConn} onClick={run}>Run</button>
                </div>
                {out && (
                    <>
                        <p className="muted">{out.count} row(s){out.truncated && " (truncated)"}</p>
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