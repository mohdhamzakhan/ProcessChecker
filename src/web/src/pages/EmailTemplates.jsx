import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import { useUser } from "../user";
import { useDialogs } from "../Dialogs";
import { ErrorBox } from "../ui";

export default function EmailTemplates() {
    const { me } = useUser();
    const { alert } = useDialogs();
    const [list, setList] = useState(null);
    const [kind, setKind] = useState("task");
    const [to, setTo] = useState(me?.email || "");
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState(null);

    useEffect(() => { api.get("/api/notifications/templates").then(setList).catch(setError); }, []);

    const current = list?.find((t) => t.kind === kind) || list?.[0];

    const sendTest = async () => {
        setBusy(true); setError(null);
        try {
            const r = await api.post("/api/notifications/test", { to: to.trim(), kind: current.kind });
            await alert(
                r.status === "DryRun"
                    ? "Test mode is on (Smtp:DryRun), so nothing was sent. You can see it in the Email log."
                    : `Sent to ${to.trim()}. Check your inbox.`,
                { title: "Test email" });
        } catch (e) { setError(e); } finally { setBusy(false); }
    };

    return (
        <>
            <div className="page-head">
                <div><h2>Email templates</h2><p>How the emails of the system look. These use example data.</p></div>
                <Link to="/emails"><button className="secondary">Back to the email log</button></Link>
            </div>
            <ErrorBox error={error} />

            {!list && !error && <p className="muted">Loading…</p>}
            {list && current && (
                <div className="tplgrid">
                    <div className="tpllist">
                        {list.map((t) => (
                            <button key={t.kind} className={`tplitem${t.kind === current.kind ? " active" : ""}`}
                                style={{ "--c": t.color }} onClick={() => setKind(t.kind)}>
                                <i />{t.label}
                            </button>
                        ))}
                    </div>

                    <div>
                        <div className="panel" style={{ margin: "0 0 12px" }}>
                            <b>{current.subject}</b>
                            <div className="actions" style={{ marginTop: 10 }}>
                                <input type="email" style={{ flex: "1 1 240px" }} value={to} placeholder="Send this one to…"
                                    onChange={(e) => setTo(e.target.value)} />
                                <button disabled={busy || !to.trim()} onClick={sendTest}>{busy ? "Sending…" : "Send me this one"}</button>
                            </div>
                        </div>
                        <iframe className="tplframe" title="Email preview" sandbox="" srcDoc={current.html} />
                    </div>
                </div>
            )}
        </>
    );
}