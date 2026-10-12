import { useState } from "react";

export const STEP_MIME = "application/x-pc-step";
export const FIELD_MIME = "application/x-pc-field";

export function startDrag(e, mime, payload) {
    e.dataTransfer.setData(mime, JSON.stringify(payload));
    e.dataTransfer.effectAllowed = "copyMove";
    document.body.classList.add(mime === STEP_MIME ? "dnd-step" : "dnd-field");
}

export const endDrag = () => document.body.classList.remove("dnd-step", "dnd-field");

const accepts = (e, mime) => Array.from(e.dataTransfer.types || []).includes(mime);

export function DropZone({ mime, onDrop, label = "Drop here", big = false, children }) {
    const [over, setOver] = useState(false);
    return (
        <div
            className={`dz${over ? " over" : ""}${big ? " big" : ""}`}
            onDragOver={(e) => {
                if (accepts(e, mime)) {
                    e.preventDefault();
                    e.dataTransfer.dropEffect = "move";
                    setOver(true);
                }
            }}
            onDragLeave={() => setOver(false)}
            onDrop={(e) => {
                e.preventDefault();
                e.stopPropagation();
                setOver(false);
                endDrag();
                try { onDrop(JSON.parse(e.dataTransfer.getData(mime))); } catch { /* ignore */ }
            }}
        >
            {children}
            {over && !big && <span className="dz-label">{label}</span>}
        </div>
    );
}