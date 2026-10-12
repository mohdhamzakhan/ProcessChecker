import { NavLink, Route, Routes } from "react-router-dom";
import { useUser } from "./user";
import Dashboard from "./pages/Dashboard";
import Tasks from "./pages/Tasks";
import Requests from "./pages/Requests";
import Processes from "./pages/Processes";
import InstanceDetail from "./pages/InstanceDetail";
import Connections from "./pages/Connections";
import Users from "./pages/Users";
import Account from "./pages/Account";
import Login from "./pages/Login";

export default function App() {
    const { me, loading, logout, isAdmin } = useUser();

    if (loading) return <p className="muted" style={{ padding: 16 }}>Loading...</p>;
    if (!me) return <Login />;

    return (
        <>
            <header className="topbar">
                <b className="brand">ProcessChecker</b>
                <nav>
                    <NavLink to="/" end>Dashboard</NavLink>
                    <NavLink to="/tasks">My Tasks</NavLink>
                    <NavLink to="/requests">Requests</NavLink>
                    <NavLink to="/processes">Processes</NavLink>
                    {isAdmin && <NavLink to="/connections">Connections</NavLink>}
                    {isAdmin && <NavLink to="/users">Users</NavLink>}
                </nav>
                <div className="actas">
                    <NavLink to="/account" style={{ color: "#fff" }}>{me.name} ({me.role})</NavLink>
                    <button className="secondary" onClick={logout}>Sign out</button>
                </div>
            </header>
            <main>
                <Routes>
                    <Route path="/" element={<Dashboard />} />
                    <Route path="/tasks" element={<Tasks />} />
                    <Route path="/requests" element={<Requests />} />
                    <Route path="/processes" element={<Processes />} />
                    <Route path="/instances/:id" element={<InstanceDetail />} />
                    <Route path="/connections" element={isAdmin ? <Connections /> : <p>Admins only.</p>} />
                    <Route path="/users" element={isAdmin ? <Users /> : <p>Admins only.</p>} />
                    <Route path="/account" element={<Account />} />
                </Routes>
            </main>
        </>
    );
}