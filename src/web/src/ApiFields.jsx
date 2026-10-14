export const defaultApi = () => ({
  authType: "None", username: "", password: "", token: "", headerName: "X-Api-Key", headerValue: "",
  testPath: "", allowPost: false, ignoreCertErrors: false, timeoutSeconds: 30,
});

// only the settings of the chosen sign-in method are sent, so old secrets never linger
export function apiPayload(c) {
  const out = {
    authType: c.authType, testPath: c.testPath || undefined, allowPost: !!c.allowPost,
    ignoreCertErrors: !!c.ignoreCertErrors, timeoutSeconds: Number(c.timeoutSeconds) || 30,
  };
  if (c.authType === "Basic") { out.username = c.username; out.password = c.password; }
  if (c.authType === "Bearer") out.token = c.token;
  if (c.authType === "ApiKey") { out.headerName = c.headerName; out.headerValue = c.headerValue; }
  return out;
}

export default function ApiFields({ baseUrl, setBaseUrl, cfg, setCfg }) {
  const set = (p) => setCfg({ ...cfg, ...p });
  return (
    <>
      <label><span>Base address</span>
        <input placeholder="https://meaisrv014:9012/api" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} /></label>

      <label><span>Sign-in method</span>
        <select value={cfg.authType} onChange={(e) => set({ authType: e.target.value })}>
          <option value="None">None</option>
          <option value="Basic">User name and password</option>
          <option value="Bearer">Bearer token</option>
          <option value="ApiKey">API key in a header</option>
          <option value="Windows">Windows account that runs the connector service</option>
        </select></label>

      {cfg.authType === "Basic" && (
        <>
          <label><span>User name</span>
            <input autoComplete="off" value={cfg.username} onChange={(e) => set({ username: e.target.value })} /></label>
          <label><span>Password</span>
            <input type="password" autoComplete="new-password" value={cfg.password} onChange={(e) => set({ password: e.target.value })} /></label>
        </>
      )}
      {cfg.authType === "Bearer" && (
        <label><span>Token</span>
          <input type="password" autoComplete="new-password" value={cfg.token} onChange={(e) => set({ token: e.target.value })} /></label>
      )}
      {cfg.authType === "ApiKey" && (
        <>
          <label><span>Header name</span>
            <input value={cfg.headerName} onChange={(e) => set({ headerName: e.target.value })} /></label>
          <label><span>Key</span>
            <input type="password" autoComplete="new-password" value={cfg.headerValue} onChange={(e) => set({ headerValue: e.target.value })} /></label>
        </>
      )}

      <label><span>Test address (optional, a path that should answer, e.g. /health)</span>
        <input placeholder="/health" value={cfg.testPath} onChange={(e) => set({ testPath: e.target.value })} /></label>
      <label><span>Time-out (seconds)</span>
        <input type="number" min={1} max={120} value={cfg.timeoutSeconds} onChange={(e) => set({ timeoutSeconds: e.target.value })} /></label>
      <label className="inline">
        <input type="checkbox" checked={cfg.allowPost} onChange={(e) => set({ allowPost: e.target.checked })} />
        Allow POST requests (for search APIs). Everything else stays read-only.
      </label>
      <label className="inline">
        <input type="checkbox" checked={cfg.ignoreCertErrors} onChange={(e) => set({ ignoreCertErrors: e.target.checked })} />
        Accept a self-signed certificate (internal servers only)
      </label>
    </>
  );
}