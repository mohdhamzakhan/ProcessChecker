import { useEffect, useRef, useState } from "react";
import { api } from "./api";

const EMAIL = /^[^\s@;,{}]+@[^\s@;,{}]+\.[^\s@;,{}]+$/;
const PLACE = /^\{\{\s*[\w.\-]+\s*\}\}$/;
const initials = (n) => (n || "?").split(/\s+/).slice(0, 2).map((w) => w[0]).join("").toUpperCase();

export default function UserPicker({ value, onChange, fields = [], placeholder = "Search by name or email…" }) {
    const [q, setQ] = useState("");
    const [hits, setHits] = useState([]);
    const [open, setOpen] = useState(false);
    const [busy, setBusy] = useState(false);
    const [warn, setWarn] = useState("");
    const [err, setErr] = useState("");
    const names = useRef({});
    const box = useRef(null);

    useEffect(() => {
        const term = q.trim();
        if (term.length < 2) { setHits([]); setWarn(""); setBusy(false); return undefined; }
        let off = false;
        setBusy(true);
        const t = setTimeout(() => {
            api.get(`/api/auth/directory?q=${encodeURIComponent(term)}`)
                .then((r) => {
                    if (off) return;
                    r.items.forEach((i) => { names.current[i.email] = i.name; });
                    setHits(r.items); setWarn(r.warning || ""); setOpen(true);
                })
                .catch((e) => { if (!off) { setHits([]); setWarn(e.message); } })
                .finally(() => { if (!off) setBusy(false); });
        }, 300);
        return () => { off = true; clearTimeout(t); };
    }, [q]);

    useEffect(() => {
        const h = (e) => { if (box.current && !box.current.contains(e.target)) setOpen(false); };
        document.addEventListener("mousedown", h);
        return () => document.removeEventListener("mousedown", h);
    }, []);

    const add = (raw, name) => {
        const items = raw.split(/[;,\n]+/).map((x) => x.trim()).filter(Boolean);
        if (!items.length) return;
        const bad = items.filter((x) => !(EMAIL.test(x) || PLACE.test(x)));
        if (bad.length) { setErr(`Not an email or {{field}}: ${bad.join(", ")}`); return; }
        setErr("");
        const clean = items.map((x) => (PLACE.test(x) ? x : x.toLowerCase()));
        if (name && clean.length === 1) names.current[clean[0]] = name;
        const have = new Set(value.map((x) => x.toLowerCase()));
        onChange([...value, ...clean.filter((x) => !have.has(x.toLowerCase()))]);
        setQ(""); setHits([]); setOpen(false);
    };

    return (
        <div className="upicker" ref={box}>
            <div className="chips-edit">
                {value.map((a, i) => (
                    <span key={a} className={`chip${a.startsWith("{{") ? " chip-field" : ""}`} title={a}>
                        {names.current[a] || a}
                        <button type="button" className="x" onClick={() => onChange(value.filter((_, j) => j !== i))}>×</button>
                    </span>
                ))}
                <input
                    value={q}
                    placeholder={value.length ? "" : placeholder}
                    onChange={(e) => { setQ(e.target.value); setErr(""); setOpen(true); }}
                    onFocus={() => hits.length && setOpen(true)}
                    onKeyDown={(e) => {
                        if (e.key === "Enter" || e.key === ";") {
                            e.preventDefault();
                            const t = q.trim();
                            if (EMAIL.test(t) || PLACE.test(t)) add(t);
                            else if (hits[0]) add(hits[0].email, hits[0].name);
                        } else if (e.key === "Backspace" && !q && value.length) onChange(value.slice(0, -1));
                    }}
                />
            </div>

            {open && q.trim().length >= 2 && (
                <div className="uresults">
                    {busy && <div className="uhint">Searching…</div>}
                    {!busy && hits.length === 0 && <div className="uhint">No matches. You can type a full email address and press Enter.</div>}
                    {hits.map((h) => (
                        <button key={h.email} type="button" className="uhit" onClick={() => add(h.email, h.name)}>
                            <span className="uav">{initials(h.name)}</span>
                            <span className="utext"><b>{h.name}</b><small>{h.email}</small></span>
                            <span className="usrc">{h.source}</span>
                        </button>
                    ))}
                    {warn && <div className="uhint warn">{warn}</div>}
                </div>
            )}
            {err && <div className="field-err">{err}</div>}

            {fields.length > 0 && (
                <select value="" style={{ marginTop: 6 }}
                    onChange={(e) => e.target.value && add(`{{${e.target.value}}}`)}>
                    <option value="">Take the person from a collected value (e.g. manager email)…</option>
                    {fields.map((f) => <option key={f.name} value={f.name}>{f.label} ({f.source})</option>)}
                </select>
            )}
        </div>
    );
}