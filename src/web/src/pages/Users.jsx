import { useEffect, useState } from "react";
import { api } from "../api";
import { useUser } from "../user";
import { Badge, ErrorBox, fmt } from "../ui";

export default function Users() {
    const { user: myEmail } = useUser();
    const [list, setList] = useState([]);
    const [error, setError] = useState(null);
    const [email, setEmail] = useState("");
    const [name, setName] = useState("");
    const [password, setPassword] = useState("");
    const [role, setRole] = useState("User");

    const load = () => api.get("/api/auth/users").then(setList).catch(setError);
    useEffect(() => { load(); }, []);

    const create = async () => {
        try {
            await api.post("/api/auth/users", { email, displayName: name, password, role });
            setEmail(""); setName(""); setPassword(""); setRole("User"); setError(null);
            load();
        } catch (e) { setError(e); }
    };

    const patch = async (u, body) => {
        try { await api.patch(`/api/auth/users/${u.id}`, body); setError(null); load(); } catch (e) { setError(e); }
    };

    const reset = (u) => {
        const pw = prompt(`New password for ${u.email} (min 10 characters):`);
        if (pw) patch(u, { newPassword: pw });
    };

    return (
        <>
            <h2>Users</h2>
            <ErrorBox error={error} />
            <table>
                <thead><tr><th>Name</th><th>Email</th><th>Role</th><th>Status</th><th>Created</th><th></th></tr></thead>
                <tbody>
                    {list.map((u) => (
                        <tr key={u.id}>
                            <td>{u.displayName}</td><td>{u.email}</td>
                            <td>
                                <select value={u.role} disabled={u.email === myEmail}
                                    onChange={(e) => patch(u, { role: e.target.value })}>
                                    <option>User</option><option>Admin</option>
                                </select>
                            </td>
                            <td><Badge status={u.isActive ? "Completed" : "Failed"} /> {u.isActive ? "Active" : "Disabled"}</td>
                            <td>{fmt(u.createdAt)}</td>
                            <td>
                                <button className="secondary" onClick={() => reset(u)}>Reset password</button>{" "}
                                {u.email !== myEmail && (
                                    <button className={u.isActive ? "danger" : ""} onClick={() => patch(u, { isActive: !u.isActive })}>
                                        {u.isActive ? "Disable" : "Enable"}
                                    </button>
                                )}
                            </td>
                        </tr>
                    ))}
                </tbody>
            </table>

            <div className="panel">
                <h3>Add a user</h3>
                <div className="form">
                    <label><span>Name</span><input value={name} onChange={(e) => setName(e.target.value)} /></label>
                    <label><span>Email</span><input value={email} onChange={(e) => setEmail(e.target.value)} /></label>
                    <label><span>Temporary password (min 10 characters)</span>
                        <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></label>
                    <label><span>Role</span>
                        <select value={role} onChange={(e) => setRole(e.target.value)}>
                            <option>User</option><option>Admin</option>
                        </select>
                    </label>
                    <button onClick={create}>Create user</button>
                </div>
            </div>
        </>
    );
}