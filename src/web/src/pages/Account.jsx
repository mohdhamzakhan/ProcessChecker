import { useState } from "react";
import { api } from "../api";
import { useUser } from "../user";
import { ErrorBox } from "../ui";

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
            <h2>My account</h2>
            <p>{me.name} · {me.email} · {me.role}</p>
            <div className="panel form">
                <h3>Change password</h3>
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