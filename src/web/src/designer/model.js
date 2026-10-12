export const uid = () => Math.random().toString(36).slice(2, 10) + Date.now().toString(36).slice(-4);

export const STEP_TYPES = {
    Form: { label: "Form", icon: "📝", color: "#2563eb", hint: "Collect details from a person" },
    Approval: { label: "Approval", icon: "✅", color: "#16a34a", hint: "Email approvers and wait" },
    Dispatch: { label: "Send to supplier", icon: "📤", color: "#9333ea", hint: "Email the collected data" },
    Upload: { label: "Upload file", icon: "📎", color: "#d97706", hint: "Person uploads a confirmation" },
    DataFetch: { label: "Fetch data", icon: "🗄️", color: "#0891b2", hint: "Read rows from a database" },
    Notify: { label: "Notify", icon: "🔔", color: "#e11d48", hint: "Send an email notice" },
};
export const STEP_ORDER = ["Form", "Approval", "Dispatch", "Upload", "DataFetch", "Notify"];

export const FIELD_TYPES = {
    text: { label: "Text", icon: "Aa" },
    textarea: { label: "Text area", icon: "¶" },
    number: { label: "Number", icon: "#" },
    date: { label: "Date", icon: "📅" },
    select: { label: "Dropdown", icon: "▾" },
    checkbox: { label: "Checkbox", icon: "☑" },
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
        sendTo: "", subject: "", body: "", connection: "", schema: "", query: "", columns: [],
    };
    if (type === "Form") st.fields = [newField("text", [])];
    return st;
}

export const blankState = () => ({
    isNew: true, keyTouched: false, key: "new-process", name: "New process", description: "",
    trigger: { type: "Manual", connection: "", schema: "", query: "", keyColumn: "", everyMinutes: 5 },
    triggerColumns: [],
    steps: [],
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
            connection: s.connection || "", schema: s.schema || "", query: s.query || "", columns: [],
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
            default:
                return base;
        }
    });

    return { key: s.key, name: s.name.trim(), description: s.description || undefined, trigger, steps };
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
        default:
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
    if (state.steps.length === 0) p("Add at least one step.");

    state.steps.forEach((s) =>
        stepIssues(s, state.steps).forEach((text) => out.push({ scope: s.uid, text: `${s.name || "Step"}: ${text}` })));
    return out;
}