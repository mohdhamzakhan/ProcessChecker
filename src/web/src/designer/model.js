export const uid = () => Math.random().toString(36).slice(2, 10) + Date.now().toString(36).slice(-4);

export const STEP_TYPES = {
    Form: { label: "Form", icon: "form", color: "#2563eb", hint: "Collect details from a person" },
    Approval: { label: "Approval", icon: "approval", color: "#059669", hint: "Email approvers and wait" },
    Dispatch: { label: "Send to supplier", icon: "send", color: "#7c3aed", hint: "Email the collected data" },
    Upload: { label: "Upload file", icon: "upload", color: "#d97706", hint: "Person uploads a confirmation" },
    DataFetch: { label: "Fetch data", icon: "database", color: "#0891b2", hint: "Read rows from a database" },
    Notify: { label: "Notify", icon: "bell", color: "#e11d48", hint: "Send an email notice" },
    Link: { label: "Document link", icon: "link", color: "#0d9488", hint: "Build a link to a file or page" },
};

export const STEP_ORDER = ["Form", "Approval", "Dispatch", "Upload", "DataFetch", "Notify", "Link"];

export const STEP_GROUPS = [
    { label: "Collect", types: ["Form", "Upload"] },
    { label: "Decide", types: ["Approval"] },
    { label: "Communicate", types: ["Dispatch", "Notify"] },
    { label: "Data", types: ["DataFetch"] },
    { label: "Documents", types: ["Link"] },
];


export const FIELD_TYPES = {
    text: { label: "Text", icon: "type" },
    textarea: { label: "Text area", icon: "align" },
    number: { label: "Number", icon: "hash" },
    date: { label: "Date", icon: "calendar" },
    select: { label: "Dropdown", icon: "dropdown" },
    checkbox: { label: "Checkbox", icon: "checksq" },
};
export const FIELD_ORDER = ["text", "textarea", "number", "date", "select", "checkbox"];

export const slugKey = (s) =>
    (s || "").toLowerCase().normalize("NFKD").replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "").slice(0, 60);

export const slugName = (s) =>
    (s || "").toLowerCase().normalize("NFKD").replace(/[^a-z0-9]+/g, "_").replace(/^_+|_+$/g, "")
        .replace(/^(\d)/, "f_$1").slice(0, 60);

export function unique(base, taken, sep = "-") {
    let name = base, n = 2;
    while (taken.has(name)) name = `${base}${sep}${n++}`;
    return name;
}

export function newField(type, fields = []) {
    const meta = FIELD_TYPES[type];
    const taken = new Set(fields.map((f) => f.name));
    return {
        uid: uid(), type, label: meta.label, name: unique(slugName(meta.label), taken, "_"),
        nameAuto: true, required: false,
        options: type === "select" ? ["Option 1", "Option 2"] : [],
    };
}

export function newStep(type, steps) {
    const meta = STEP_TYPES[type];
    const same = steps.filter((s) => s.type === type).length;
    const name = same ? `${meta.label} ${same + 1}` : meta.label;
    const key = unique(slugKey(name) || "step", new Set(steps.map((s) => s.key)));
    const st = {
        uid: uid(), key, name, type, approvers: [], fields: [], allowUpload: false,
        sendTo: "", subject: "", body: "", url: "", linkLabel: "", connection: "", schema: "", query: "", columns: [], assignees: []
    };
    if (type === "Form") st.fields = [newField("text", [])];
    return st;
}

export const blankState = () => ({
    isNew: true, keyTouched: false, key: "new-process", name: "New process", description: "",
    trigger: { type: "Manual", connection: "", schema: "", query: "", keyColumn: "", everyMinutes: 5 },
    triggerColumns: [],
    steps: [],
    observers: [],
    requesters: [],
});

export function fromDefinition(def) {
    const t = def.trigger || {};
    return {
        isNew: false, keyTouched: true, key: def.key, name: def.name, description: def.description || "",
        trigger: {
            type: t.type === "DatabasePoll" ? "DatabasePoll" : "Manual",
            connection: t.connection || "", schema: t.schema || "", query: t.query || "",
            keyColumn: t.keyColumn || "", everyMinutes: t.everyMinutes || 5,
        },
        triggerColumns: [],
        steps: (def.steps || []).map((s) => ({
            uid: uid(), key: s.key, name: s.name, type: s.type,
            approvers: s.approvers || [], allowUpload: !!s.allowUpload,
            sendTo: s.sendTo || "", subject: s.subject || "", body: s.body || "",
            url: s.url || "", linkLabel: s.linkLabel || "",
            connection: s.connection || "", schema: s.schema || "", query: s.query || "", columns: [],
            assignees: s.assignees || [],
            observers: def.observers || [],
            requesters: def.requesters || [],
            fields: (s.fields || []).map((f) => ({
                uid: uid(), name: f.name, label: f.label || f.name, type: f.type || "text",
                required: !!f.required, options: f.options || [], nameAuto: false,
            })),
        })),
    };
}

/** The JSON the engine understands. */
export function toDefinition(s) {
    const t = s.trigger;
    const trigger = t.type === "DatabasePoll"
        ? {
            type: "DatabasePoll", connection: t.connection, schema: t.schema || undefined, query: t.query,
            keyColumn: t.keyColumn || undefined, everyMinutes: Number(t.everyMinutes) || 5,
        }
        : { type: "Manual" };

    const steps = s.steps.map((st) => {
        const base = { key: st.key, name: st.name.trim(), type: st.type };
        switch (st.type) {
            case "Approval":
                return { ...base, approvers: st.approvers };
            case "Form":
                return {
                    ...base, allowUpload: !!st.allowUpload,
                    assignees: st.assignees,
                    fields: st.fields.map((f) => ({
                        name: f.name, label: f.label, type: f.type, required: !!f.required,
                        options: f.type === "select" ? f.options.map((o) => o.trim()).filter(Boolean) : [],
                    })),
                };
            case "Dispatch":
            case "Notify":
                return { ...base, sendTo: st.sendTo, subject: st.subject || undefined, body: st.body || undefined };
            case "DataFetch":
                return { ...base, connection: st.connection, schema: st.schema || undefined, query: st.query };
            case "Link":
                return { ...base, url: st.url.trim(), linkLabel: st.linkLabel || undefined };
            case "Upload":
                return { ...base, assignees: st.assignees };
            default:
                return base;
        }
    });

    return {
        key: s.key, name: s.name.trim(), description: s.description || undefined, trigger, steps, links,
        observers: s.observers || [], requesters: s.requesters || [],
    };
}

/** Values collected before step `index`: usable as {{name}} in later steps. */
export function availableFields(state, index) {
    const seen = new Map();
    const add = (name, label, source, type) => {
        if (name && !seen.has(name.toLowerCase())) seen.set(name.toLowerCase(), { name, label: label || name, source, type });
    };
    if (state.trigger.type === "DatabasePoll")
        state.triggerColumns.forEach((c) => add(c, c, "Trigger"));
    state.steps.slice(0, index).forEach((s) => {
        if (s.type === "Form") s.fields.forEach((f) => add(f.name, f.label, s.name, f.type));
        if (s.type === "DataFetch") (s.columns || []).forEach((c) => add(c, c, s.name));
    });
    return [...seen.values()];
}

// ---------------------------------------------------------------- validation
const EMAIL = /^[^\s@;,{}]+@[^\s@;,{}]+\.[^\s@;,{}]+$/;
const PLACE = /^\{\{\s*[\w.\-]+\s*\}\}$/;
const KEY = /^[a-z0-9-]+$/;
const SCHEMA = /^[A-Za-z][A-Za-z0-9_$#]{0,127}$/;

export const isEmail = (t) => EMAIL.test(t);
export const isPlaceholder = (t) => PLACE.test(t);
const recipients = (s) => (s || "").split(/[;,]/).map((x) => x.trim()).filter(Boolean);
const recipientOk = (t) => EMAIL.test(t) || t.includes("{{");

export function stepIssues(st, all) {
    const out = [];
    if (!st.name.trim()) out.push("Give this step a name.");
    if (!KEY.test(st.key)) out.push("The step key may only contain a-z, 0-9 and '-'.");
    else if (all.filter((x) => x.key === st.key).length > 1) out.push("Another step has the same key.");
    const badWho = (st.assignees || []).filter((a) => !(isEmail(a) || isPlaceholder(a)));
    if (badWho.length) out.push(`Not an email or {{field}}: ${badWho.join(", ")}.`);
    switch (st.type) {
        case "Form": {
            if (st.fields.length === 0) out.push("Add at least one field.");
            const names = new Set();
            for (const f of st.fields) {
                const label = f.label || f.name || "A field";
                if (!f.name || !/^[\w.\-]+$/.test(f.name)) out.push(`"${label}" needs a valid field name.`);
                else if (names.has(f.name)) out.push(`Two fields share the name "${f.name}".`);
                else names.add(f.name);
                if (f.type === "select" && f.options.map((o) => o.trim()).filter(Boolean).length === 0)
                    out.push(`"${label}" needs at least one option.`);
            }
            break;
        }
        case "Approval":
            if (st.approvers.length === 0) out.push("Add at least one approver.");
            break;
        case "Dispatch":
        case "Notify": {
            const to = recipients(st.sendTo);
            if (to.length === 0) out.push("Say who to send to.");
            else if (!to.every(recipientOk)) out.push("The recipients must be email addresses or {{fields}}.");
            if (st.type === "Notify" && !st.subject.trim()) out.push("Notify needs a subject.");
            break;
        }
        case "DataFetch":
            if (!st.connection) out.push("Choose a data source.");
            if (!st.query.trim()) out.push("Write the query.");
            if (st.schema && !SCHEMA.test(st.schema) && !PLACE.test(st.schema)) out.push("The schema name is not valid.");
            break;
        case "Link":
            if (!st.url.trim()) out.push("Write the link address.");
            else if (!/^https?:\/\/[^/?#{}\s]+(?=[/?#]|$)/i.test(st.url.trim()))
                out.push("The address must start with http:// or https:// and a fixed server name.");
            break;
        default:
    }
    return out;
}
/** The names used inside {{ }} in a text, with any "+1" or "-2" removed. */
export function placeholderNames(text, known) {
    const out = [];
    const re = /\{\{\s*([^{}]+?)\s*\}\}/g;
    let m;
    while ((m = re.exec(text || "")) !== null) {
        let token = m[1].trim();
        if (!known.has(token.toLowerCase())) {
            const a = /^([\w.\-]+?)\s*[+-]\s*\d+$/.exec(token);
            if (a) token = a[1];
        }
        if (!out.includes(token)) out.push(token);
    }
    return out;
}
export function allIssues(state) {
    const out = [];
    const p = (text) => out.push({ scope: "process", text });
    if (!state.name.trim()) p("Give the process a name.");
    if (!KEY.test(state.key)) p("The process key may only contain a-z, 0-9 and '-'.");

    const t = state.trigger;
    if (t.type === "DatabasePoll") {
        if (!t.connection) p("Start trigger: choose a data source.");
        if (!t.query.trim()) p("Start trigger: write the query that finds new rows.");
        if (!(Number(t.everyMinutes) >= 1)) p("Start trigger: check at least every 1 minute.");
        if (t.schema && !SCHEMA.test(t.schema)) p("Start trigger: the schema name is not valid.");
    }
    const badObs = (state.observers || []).filter((o) => !(isEmail(o) || isPlaceholder(o)));
    if (badObs.length) p(`Observers: not an email or {{field}}: ${badObs.join(", ")}.`);
    const badReq = (state.requesters || []).filter((r) => !(isEmail(r) || isPlaceholder(r)));
    if (badReq.length) p(`Requester: not an email or {{field}}: ${badReq.join(", ")}.`);
    if (state.steps.length === 0) p("Add at least one step.");

    state.steps.forEach((s) =>
        stepIssues(s, state.steps).forEach((text) => out.push({ scope: s.uid, text: `${s.name || "Step"}: ${text}` })));
    return out;
}