import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import { useUser } from "../user";
import Icon from "../designer/Icon";
import { STEP_TYPES } from "../designer/model";
import { ErrorBox, ago } from "../ui";

function TaskRow({ t, showWho }) {
    const m = STEP_TYPES[t.stepType] || STEP_TYPES.Form;
    const verb = t.stepType === "Approval" ? "Approve or reject" : t.stepType === "Upload" ? "Upload a file" : "Fill in the form";
    return (
        <Link to={`/instances/${t.instanceId}`} className="task" style={{ "--c": m.color }}>
            <span className="tbadge"><Icon name={m.icon} size={20} /></span>
            <div className="tmain">
                <b>{t.title}</b>
                <small>{t.processName} · {t.stepName}</small>
                {showWho && <small className="muted">Assigned to {t.assignees}</small>}
            </div>
            <div className="tmeta">
                <span className="tverb">{verb}</span>
                <small>{t.since ? `Waiting ${ago(t.since)}` : ""}</small>
            </div>
            <span className="tgo">Open →</span>
        </Link>
    );
}

function ObservedRow({ i }) {
    return (
        <Link to={`/instances/${i.id}`} className="task" style={{ "--c": "#64748b" }}>
            <span className="tbadge"><Icon name="eye" size={20} /></span>
            <div className="tmain">
                <b>{i.title}</b>
                <small>{i.processName}{i.status === "Running" ? ` · now at ${i.currentStep || "–"}` : ""}</small>
            </div>
            <div className="tmeta">
                <span className="tverb">{i.status === "Running" ? `${i.completedSteps} of ${i.totalSteps} steps` : i.status}</span>
                <small>Started {ago(i.createdAt)}</small>
            </div>
            <span className="tgo">View →</span>
        </Link>
    );
}

export default function Tasks() {
    const { isAdmin } = useUser();
    const [data, setData] = useState(null);
    const [error, setError] = useState(null);
    const [tab, setTab] = useState("mine");

    useEffect(() => {
        const load = () => api.get("/api/tasks").then((d) => { setData(d); setError(null); }).catch(setError);
        load();
        const t = setInterval(load, 30000);
        return () => clearInterval(t);
    }, []);

    const tabs = [
        ["mine", "Assigned to me", data?.mine?.length],
        ["observing", "I am observing", data?.observing?.length],
        ["open", "Nobody assigned", data?.open?.length],
        ["requested", "My requests", data?.requested?.length],
        ...(isAdmin ? [["others", "Assigned to others", data?.others?.length]] : []),
    ];
    const list = data?.[tab] || [];
    const text = {
        mine: ["You are all caught up", "No tasks are waiting for you."],
        observing: ["Nothing to observe", "You are not observing any running request."],
        open: ["Nothing here", "Tasks that nobody is assigned to, in requests you started, appear here."],
        others: ["Nothing here", "No tasks are assigned to other people."],
        requested: ["No requests", "Requests made for you appear here."],
    }[tab];

    return (
        <>
            <div className="page-head">
                <div><h2>My Tasks</h2><p>Things that are waiting for a person to act, and requests you follow.</p></div>
            </div>

            <ErrorBox error={error} />

            <div className="tabs">
                {tabs.map(([k, label, n]) => (
                    <button key={k} className={`tab${tab === k ? " active" : ""}`} onClick={() => setTab(k)}>
                        {label}{n > 0 && <span className="count">{n}</span>}
                    </button>
                ))}
            </div>

            {data && list.length === 0 && <div className="empty"><b>{text[0]}</b>{text[1]}</div>}
            {tab === "observing" || tab === "requested"
                ? list.map((i) => <ObservedRow key={i.id} i={i} />)
                : list.map((t) => <TaskRow key={t.instanceId + t.stepKey} t={t} showWho={tab === "others"} />)}
        </>
    );
}