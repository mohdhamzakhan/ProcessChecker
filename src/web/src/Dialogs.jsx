import { createContext, useCallback, useContext, useEffect, useState } from "react";
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

const Ctx = createContext(null);

/** confirm("Delete it?", { title, okLabel, danger }) -> Promise<boolean> */
export function DialogProvider({ children }) {
    const [state, setState] = useState(null);
    const confirm = useCallback(
        (message, opts = {}) => new Promise((resolve) => setState({ message, opts, resolve })), []);
    const close = (value) => { state.resolve(value); setState(null); };

    return (
        <Ctx.Provider value={{ confirm }}>
            {children}
            {state && (
                <Modal title={state.opts.title || "Please confirm"} onClose={() => close(false)}
                    footer={
                        <>
                            <button className="secondary" autoFocus={!!state.opts.danger} onClick={() => close(false)}>Cancel</button>
                            <button className={state.opts.danger ? "dangerfill" : ""} autoFocus={!state.opts.danger}
                                onClick={() => close(true)}>{state.opts.okLabel || "OK"}</button>
                        </>
                    }>
                    <p style={{ margin: 0 }}>{state.message}</p>
                </Modal>
            )}
        </Ctx.Provider>
    );
}

export const useDialogs = () => useContext(Ctx);