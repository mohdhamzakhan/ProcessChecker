import { useEffect, useState } from "react";
import { api } from "../api";
import { useUser } from "../user";
import { useDialogs } from "../Dialogs";
import UserPicker from "../UserPicker";
import { ErrorBox } from "../ui";

const pad = (n) => String(n).padStart(2, "0");
const toInput = (d) =>
    `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
const asDate = (iso) => new Date(/[zZ]$|[+-]\d\d:?\d\d$/.test(iso) ? iso : iso + "Z");

function Away() {
    const { alert, confirm } = useDialogs();
    const [cur, setCur] = useState(undefined);              // undefined = loading, null = nothing set
    const [to, setTo] = useState([]);
    const [from, setFrom] = useState(toInput(new Date()));
    const [until, setUntil] = useState(toInput(new Date(Date.now() + 7 * 86400000)));
    const [handNow, setHandNow] = useState(true);
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    const load = () =>
        api.get("/api/away")
            .then((a) => {
                setCur(a);
                if (a) { setTo([a.delegateTo]); setFrom(toInput(asDate(a.fromAt))); setUntil(toInput(asDate(a.toAt))); }
            })
            .catch((e) => { setCur(null); setError(e); });
    useEffect(() => { load(); }, []);

    const save = async () => {
        setBusy(true); setError(null);
        try {
            const r = await api.put("/api/away", {
                delegateTo: to[0], fromAt: new Date(from).toISOString(), toAt: new Date(until).toISOString(), handOverNow: handNow,
            });
            await load();
            await alert(r.handedOver ? `Saved. ${r.handedOver} waiting task(s) were handed over.` : "Saved.", { title: "Out of office" });
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    const remove = async () => {
        const ok = await confirm("Switch the out-of-office off? New tasks will come to you again.",
            { title: "Out of office", okLabel: "Switch off" });
        if (!ok) return;
        try { await api.del("/api/away"); setCur(null); setTo([]); } catch (e) { setError(e); }
    };

    const now = Date.now();
    const awayNow = cur && asDate(cur.fromAt).getTime() <= now && now <= asDate(cur.toAt).getTime();

    return (
        <div className="panel form" style={{ maxWidth: 560 }}>
            <h3>Out of office</h3>
            <p className="muted small" style={{ margin: 0 }}>
                While you are away, new tasks for you go to a colleague. They get the email and can see the request.
            </p>
            {awayNow && <div className="warnbar">You are marked as out of office until {asDate(cur.toAt).toLocaleString()}. Tasks go to {cur.delegateTo}.</div>}
            {cur && !awayNow && <p className="muted small">Saved: {asDate(cur.fromAt).toLocaleString()} to {asDate(cur.toAt).toLocaleString()}, for {cur.delegateTo}.</p>}
            <ErrorBox error={error} />
            <label><span>Hand tasks to</span>
                <UserPicker value={to} onChange={(v) => setTo(v.slice(-1))} placeholder="Search a colleague…" /></label>
            <label><span>From</span><input type="datetime-local" value={from} onChange={(e) => setFrom(e.target.value)} /></label>
            <label><span>Until</span><input type="datetime-local" value={until} onChange={(e) => setUntil(e.target.value)} /></label>
            <label className="inline">
                <input type="checkbox" checked={handNow} onChange={(e) => setHandNow(e.target.checked)} />
                Also hand over the tasks that are waiting for me now
            </label>
            <div className="actions">
                <button disabled={busy || to.length === 0 || !from || !until} onClick={save}>{busy ? "Saving..." : cur ? "Update" : "Switch on"}</button>
                {cur && <button className="secondary" onClick={remove}>Switch off</button>}
            </div>
        </div>
    );
}

export default function Account() {
    const { me } = useUser();
    const [current, setCurrent] = useState("");
    const [next, setNext] = useState("");
    const [error, setError] = useState(null);
    const [done, setDone] = useState(false);

    const save = async () => {
        setDone(false);
        try {
            await api.post("/api/auth/change-password", { currentPassword: current, newPassword: next });
            setCurrent(""); setNext(""); setError(null); setDone(true);
        } catch (e) { setError(e); }
    };

    return (
        <>
            <div className="page-head"><div><h2>My account</h2><p>{me.name} · {me.email} · {me.role}</p></div></div>
            <Away />
            <div className="panel form" style={{ maxWidth: 560 }}>
                <h3>Change password</h3>
                <p className="muted small" style={{ margin: 0 }}>This applies to local accounts. A company account keeps its Windows password.</p>
                <ErrorBox error={error} />
                <label><span>Current password</span>
                    <input type="password" value={current} onChange={(e) => setCurrent(e.target.value)} /></label>
                <label><span>New password (min 10 characters)</span>
                    <input type="password" value={next} onChange={(e) => setNext(e.target.value)} /></label>
                <button onClick={save}>Change password</button>
                {done && <span className="ok">Password changed.</span>}
            </div>
        </>
    );
}