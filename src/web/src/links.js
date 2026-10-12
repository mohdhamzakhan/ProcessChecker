const PLACE = /\{\{\s*([^{}]+?)\s*\}\}/g;
const ARITH = /^([\w.\-]+?)\s*([+-])\s*(\d+)$/;

// case-insensitive lookup: Oracle returns ID, the template may say {{id}}
function lookup(data, name) {
    const key = Object.keys(data || {}).find((k) => k.toLowerCase() === name.toLowerCase());
    if (key === undefined) return undefined;
    const v = data[key];
    if (v === null || v === undefined) return "";
    return typeof v === "object" ? JSON.stringify(v) : String(v);
}

// {{id}} -> the value; {{id+1}} / {{id-2}} -> a whole number plus or minus
function resolve(data, token) {
    const direct = lookup(data, token);
    if (direct !== undefined) return direct;

    const m = ARITH.exec(token);
    if (!m) return undefined;
    const base = lookup(data, m[1]);
    if (base === undefined || !/^-?\d+$/.test(base.trim())) return undefined;
    const n = BigInt(base.trim());
    const delta = BigInt(m[3]);
    return String(m[2] === "+" ? n + delta : n - delta);
}

/** Fills a link template. Values are URL-encoded; empty or unknown ones are listed in `missing`. */
export function buildLink(template, data) {
    const missing = [];
    const url = (template || "").trim().replace(PLACE, (_, raw) => {
        const token = raw.trim();
        const v = resolve(data, token);
        if (v === undefined || v === "") {
            if (!missing.includes(token)) missing.push(token);
            return "";
        }
        return encodeURIComponent(v);
    });
    const valid = missing.length === 0 && /^https?:\/\/[^\s]+$/i.test(url);
    return { url, missing, valid };
}