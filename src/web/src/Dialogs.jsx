import { createContext, useCallback, useContext, useEffect, useMemo, useState } from "react";
import Icon from "./designer/Icon";

export function Modal({ title, onClose, children, footer, wide }) {
    useEffect(() => {
        const h = (e) => e.key === "Escape" && onClose();
        document.addEventListener("keydown", h);
        return () => document.removeEventListener("keydown", h);
    }, [onClose]);

    return (
        <div className="vback" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
            <div className={`dialog${wide ? " wide" : ""}`} role="dialog" aria-modal="true" aria-label={title}>
                <div className="dhead">
                    <h3>{title}</h3>
                    <button className="icon" onClick={onClose} aria-label="Close"><Icon name="x" size={18} /></button>
                </div>
                <div className="dbodyc">{children}</div>
                {footer && <div className="dfoot">{footer}</div>}
            </div>
        </div>
    );
}

function DialogBox({ dialog, close }) {
    const { kind, message, opts } = dialog;
    const [value, setValue] = useState(opts.defaultValue ?? "");
    const cancel = () => close(kind === "prompt" ? null : kind === "confirm" ? false : undefined);
    const ok = () => close(kind === "prompt" ? value : kind === "confirm" ? true : undefined);
    const title = opts.title || (kind === "alert" ? "Notice" : kind === "prompt" ? "Enter a value" : "Please confirm");

    return (
        <Modal title={title} onClose={cancel}
            footer={
                <>
                    {kind !== "alert" && (
                        <button className="secondary" autoFocus={!!opts.danger} onClick={cancel}>Cancel</button>
                    )}
                    <button className={opts.danger ? "dangerfill" : ""} autoFocus={kind !== "prompt" && !opts.danger}
                        onClick={ok}>{opts.okLabel || "OK"}</button>
                </>
            }>
            <p style={{ margin: 0, whiteSpace: "pre-wrap" }}>{message}</p>
            {kind === "prompt" && (
                <input autoFocus style={{ width: "100%", marginTop: 12 }} value={value}
                    onChange={(e) => setValue(e.target.value)} onKeyDown={(e) => e.key === "Enter" && ok()} />
            )}
        </Modal>
    );
}

const Ctx = createContext(null);

/** confirm(message, opts) -> Promise<boolean>; alert(message, opts) -> Promise<void>; prompt(message, opts) -> Promise<string|null>
    opts: { title, okLabel, danger, defaultValue } */
export function DialogProvider({ children }) {
    const [dialog, setDialog] = useState(null);

    const ask = useCallback(
        (kind, message, opts = {}) => new Promise((resolve) => setDialog({ id: Math.random(), kind, message, opts, resolve })),
        []);
    const api = useMemo(() => ({
        confirm: (m, o) => ask("confirm", m, o),
        alert: (m, o) => ask("alert", m, o),
        prompt: (m, o) => ask("prompt", m, o),
    }), [ask]);

    const close = (value) => { dialog.resolve(value); setDialog(null); };

    return (
        <Ctx.Provider value={api}>
            {children}
            {dialog && <DialogBox key={dialog.id} dialog={dialog} close={close} />}
        </Ctx.Provider>
    );
}

export const useDialogs = () => useContext(Ctx);