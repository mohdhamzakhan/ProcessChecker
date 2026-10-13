const TOKEN_KEY = "pc_token";
export const getToken = () => localStorage.getItem(TOKEN_KEY);
export const setToken = (t) => (t ? localStorage.setItem(TOKEN_KEY, t) : localStorage.removeItem(TOKEN_KEY));
const authHeaders = () => (getToken() ? { Authorization: `Bearer ${getToken()}` } : {});

async function handle(res) {
    const text = await res.text();
    let data = null;
    try { data = text ? JSON.parse(text) : null; } catch { data = { error: text }; }

    if (!res.ok) {
        if (res.status === 401 && getToken()) {
            setToken(null);
            window.dispatchEvent(new Event("pc-unauthorized"));
        }
        const fallback =
            res.status === 403 ? "You do not have permission to do this."
                : res.status === 404 ? "Not found, or you do not have access to it."
                    : res.status === 429 ? "Too many attempts. Wait a minute and try again."
                        : [502, 503, 504].includes(res.status) ? "A backend service is not running or not reachable. Check the service windows."
                            : `Request failed (${res.status})`;
        const err = new Error(data?.error || fallback);
        err.errors = Array.isArray(data?.errors) ? data.errors : [];
        err.status = res.status;
        throw err;
    }
    return data;
}

async function request(method, url, body) {
    const headers = { ...authHeaders() };
    if (body !== undefined) headers["Content-Type"] = "application/json";
    const res = await fetch(url, {
        method, headers, body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    return handle(res);
}

export const api = {
    get: (u) => request("GET", u),
    post: (u, b) => request("POST", u, b ?? {}),
    put: (u, b) => request("PUT", u, b ?? {}),
    patch: (u, b) => request("PATCH", u, b),
    del: (u) => request("DELETE", u),
};

/** Uploads to DocumentService and returns { fileId, fileName } for the process step. */
export async function uploadDocument(file, { instanceId, stepKey } = {}) {
    const fd = new FormData();
    fd.append("file", file);
    if (instanceId) fd.append("instanceId", instanceId);
    if (stepKey) fd.append("stepKey", stepKey);
    const res = await fetch("/api/documents", { method: "POST", headers: authHeaders(), body: fd });
    const data = await handle(res);
    return { fileId: data.fileId, fileName: data.fileName };
}

export async function downloadFile(id, name) {
    const res = await fetch(`/api/documents/${id}/download`, { headers: authHeaders() });
    if (!res.ok) await handle(res);
    const url = URL.createObjectURL(await res.blob());
    const a = document.createElement("a");
    a.href = url;
    a.download = name || "file";
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
}
export async function fetchDocumentBlob(id) {
    const res = await fetch(`/api/documents/${id}/download`, { headers: authHeaders() });
    if (!res.ok) await handle(res);
    return res.blob();
}