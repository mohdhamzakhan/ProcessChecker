import { useEffect, useState } from "react";
import { NavLink, Route, Routes, useLocation } from "react-router-dom";
import { useUser } from "./user";
import Dashboard from "./pages/Dashboard";
import Dashboards from "./pages/Dashboards";
import Tasks from "./pages/Tasks";
import Requests from "./pages/Requests";
import Processes from "./pages/Processes";
import InstanceDetail from "./pages/InstanceDetail";
import Connections from "./pages/Connections";
import Users from "./pages/Users";
import EmailLog from "./pages/EmailLog";
import Account from "./pages/Account";
import Login from "./pages/Login";
import Designer from "./designer/Designer";
import Icon from "./designer/Icon";
import EmailTemplates from "./pages/EmailTemplates";

const Item = ({ to, icon, end, children }) => (
    <NavLink to={to} end={end} className="nav"><Icon name={icon} size={18} />{children}</NavLink>
);

export default function App() {
    const { me, loading, logout, isAdmin } = useUser();
    const { pathname } = useLocation();
    const [menu, setMenu] = useState(false);

    useEffect(() => { setMenu(false); }, [pathname]);

    useEffect(() => {
        document.body.style.overflow = menu ? "hidden" : "";
        const esc = (e) => e.key === "Escape" && setMenu(false);
        document.addEventListener("keydown", esc);
        return () => { document.body.style.overflow = ""; document.removeEventListener("keydown", esc); };
    }, [menu]);

    if (loading) return <p className="muted" style={{ padding: 16 }}>Loading...</p>;
    if (!me) return <Login />;

    const initials = (me.name || me.email).split(/[\s@.]+/).filter(Boolean).slice(0, 2)
        .map((w) => w[0]).join("").toUpperCase();
    const wide = pathname.startsWith("/designer");

    return (
        <div className="app">
            <header className="mbar">
                <button className="icon" aria-label="Open the menu" aria-expanded={menu} onClick={() => setMenu(true)}>
                    <Icon name="menu" size={22} />
                </button>
                <span className="logo"><Icon name="check" size={16} stroke={3} /></span>
                <b>ProcessChecker</b>
                <NavLink to="/account" className="avatar" title="My account">{initials}</NavLink>
            </header>

            {menu && <div className="backdrop" onClick={() => setMenu(false)} />}

            <aside className={`sidebar${menu ? " open" : ""}`}>
                <div className="brand">
                    <span className="logo"><Icon name="check" size={18} stroke={3} /></span>ProcessChecker
                    <button className="icon sclose" aria-label="Close the menu" onClick={() => setMenu(false)}>
                        <Icon name="x" size={18} />
                    </button>
                </div>

                <div className="nav-label">Work</div>
                <Item to="/" end icon="dashboard">Overview</Item>
                <Item to="/dashboards" icon="chart">My dashboards</Item>
                <Item to="/tasks" icon="tasks">My Tasks</Item>
                <Item to="/requests" icon="requests">Requests</Item>

                <div className="nav-label">Setup</div>
                <Item to="/processes" icon="workflow">Processes</Item>
                {isAdmin && <Item to="/designer" icon="designer">Designer</Item>}
                {isAdmin && <Item to="/connections" icon="plug">Connections</Item>}
                {isAdmin && <Item to="/users" icon="users">Users</Item>}
                {isAdmin && <Item to="/emails" icon="mail">Email log</Item>}

                <div className="grow" />
                <div className="userbox">
                    <NavLink to="/account" className="avatar" title="My account">{initials}</NavLink>
                    <div className="who"><b>{me.name}</b><small>{me.role}</small></div>
                    <button onClick={logout} title="Sign out"><Icon name="logout" size={15} /></button>
                </div>
            </aside>

            <main className={`content${wide ? " wide" : ""}`}>
                <Routes>
                    <Route path="/" element={<Dashboard />} />
                    <Route path="/dashboards" element={<Dashboards />} />
                    <Route path="/tasks" element={<Tasks />} />
                    <Route path="/requests" element={<Requests />} />
                    <Route path="/processes" element={<Processes />} />
                    <Route path="/instances/:id" element={<InstanceDetail />} />
                    <Route path="/designer" element={isAdmin ? <Designer /> : <p>Admins only.</p>} />
                    <Route path="/designer/:key" element={isAdmin ? <Designer /> : <p>Admins only.</p>} />
                    <Route path="/connections" element={isAdmin ? <Connections /> : <p>Admins only.</p>} />
                    <Route path="/users" element={isAdmin ? <Users /> : <p>Admins only.</p>} />
                    <Route path="/emails/templates" element={isAdmin ? <EmailTemplates /> : <p>Admins only.</p>} />
                    <Route path="/emails" element={isAdmin ? <EmailLog /> : <p>Admins only.</p>} />
                    <Route path="/account" element={<Account />} />
                </Routes>
            </main>
        </div>
    );
}