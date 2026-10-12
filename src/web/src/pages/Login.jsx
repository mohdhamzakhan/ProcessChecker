import { useState } from "react";
import { useUser } from "../user";
import { ErrorBox } from "../ui";

export default function Login() {
    const { login } = useUser();
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [error, setError] = useState(null);
    const [busy, setBusy] = useState(false);

    const submit = async (e) => {
        e.preventDefault();
        setBusy(true); setError(null);
        try { await login(email, password); } catch (err) { setError(err); } finally { setBusy(false); }
    };

    return (
        <div className="login">
            <form className="panel form" onSubmit={submit}>
                <h2>ProcessChecker</h2>
                <ErrorBox error={error} />
                <label><span>Email or username</span>
                    <input type="text" autoFocus autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
                <label><span>Password</span>
                    <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></label>
                <button disabled={busy || !email || !password}>{busy ? "Signing in..." : "Sign in"}</button>
            </form>
        </div>
    );
}