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
import Designer from "./designer/Designer";

const Item = ({ to, icon, end, children }) => (
    <NavLink to={to} end={end} className="nav"><span>{icon}</span>{children}</NavLink>
);

export default function App() {
    const { me, loading, logout, isAdmin } = useUser();

    if (loading) return <p className="muted" style={{ padding: 16 }}>Loading...</p>;
    if (!me) return <Login />;

    const initials = (me.name || me.email).split(/[\s@.]+/).filter(Boolean).slice(0, 2)
        .map((w) => w[0]).join("").toUpperCase();

    return (
        <div className="app">
            <aside className="sidebar">
                <div className="brand"><span className="logo">✓</span>ProcessChecker</div>

                <div className="nav-label">Work</div>
                <Item to="/" end icon="📊">Dashboard</Item>
                <Item to="/tasks" icon="✅">My Tasks</Item>
                <Item to="/requests" icon="📄">Requests</Item>

                <div className="nav-label">Setup</div>
                <Item to="/processes" icon="🧩">Processes</Item>
                {isAdmin && <Item to="/connections" icon="🔌">Connections</Item>}
                {isAdmin && <Item to="/users" icon="👥">Users</Item>}
                {isAdmin && <Item to="/designer" icon="🛠️">Designer</Item>}

                <div className="grow" />
                <div className="userbox">
                    <NavLink to="/account" className="avatar" title="My account">{initials}</NavLink>
                    <div className="who"><b>{me.name}</b><small>{me.role}</small></div>
                    <button onClick={logout}>Sign out</button>
                </div>
            </aside>

            <main className="content">
                <Routes>
                    <Route path="/" element={<Dashboard />} />
                    <Route path="/tasks" element={<Tasks />} />
                    <Route path="/requests" element={<Requests />} />
                    <Route path="/processes" element={<Processes />} />
                    <Route path="/instances/:id" element={<InstanceDetail />} />
                    <Route path="/connections" element={isAdmin ? <Connections /> : <p>Admins only.</p>} />
                    <Route path="/users" element={isAdmin ? <Users /> : <p>Admins only.</p>} />
                    <Route path="/account" element={<Account />} />
                    <Route path="/designer" element={isAdmin ? <Designer /> : <p>Admins only.</p>} />
                    <Route path="/designer/:key" element={isAdmin ? <Designer /> : <p>Admins only.</p>} />
                </Routes>
            </main>
        </div>
    );
}