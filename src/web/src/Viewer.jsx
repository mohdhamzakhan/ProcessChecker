import { useEffect, useState } from "react";
import { downloadFile, fetchDocumentBlob } from "./api";
import Icon from "./designer/Icon";

/** source: { kind: "url", url, label }  or  { kind: "file", id, name } */
export default function Viewer({ source, onClose }) {
    const isFile = source.kind === "file";
    const [st, setSt] = useState({ loading: isFile, error: "", blobUrl: "", type: "" });

    useEffect(() => {
        if (!isFile) return undefined;
        let off = false;
        let objectUrl = "";
        fetchDocumentBlob(source.id)
            .then((blob) => {
                if (off) return;
                objectUrl = URL.createObjectURL(blob);
                setSt({ loading: false, error: "", blobUrl: objectUrl, type: blob.type });
            })
            .catch((e) => { if (!off) setSt({ loading: false, error: e.message, blobUrl: "", type: "" }); });
        return () => { off = true; if (objectUrl) URL.revokeObjectURL(objectUrl); };
    }, [isFile, source.id]);

    useEffect(() => {
        const h = (e) => e.key === "Escape" && onClose();
        document.addEventListener("keydown", h);
        return () => document.removeEventListener("keydown", h);
    }, [onClose]);

    const title = isFile ? source.name || "File" : source.label || source.url;
    const previewable = st.type === "application/pdf" || st.type.startsWith("image/") || st.type === "text/plain";

    return (
        <div className="vback" onClick={onClose}>
            <div className="vbox" onClick={(e) => e.stopPropagation()}>
                <div className="vhead">
                    <b title={title}>{title}</b>
                    {isFile
                        ? <button className="secondary" onClick={() => downloadFile(source.id, source.name).catch((e) => alert(e.message))}>Download</button>
                        : <a className="btn" href={source.url} target="_blank" rel="noopener noreferrer">Open in new tab</a>}
                    <button className="icon" onClick={onClose} title="Close"><Icon name="x" size={18} /></button>
                </div>

                <div className="vbody">
                    {!isFile && <iframe title={title} src={source.url} referrerPolicy="no-referrer" />}
                    {isFile && st.loading && <div className="vmsg">Loading…</div>}
                    {isFile && st.error && <div className="vmsg">{st.error}</div>}
                    {isFile && !st.loading && !st.error && previewable && st.type.startsWith("image/") && <img src={st.blobUrl} alt={title} />}
                    {isFile && !st.loading && !st.error && previewable && !st.type.startsWith("image/") && <iframe title={title} src={st.blobUrl} />}
                    {isFile && !st.loading && !st.error && !previewable && (
                        <div className="vmsg">A preview is not available for this kind of file. Use Download.</div>
                    )}
                </div>

                {!isFile && (
                    <div className="vnote">
                        If this stays blank, the other server does not allow being shown inside another page, or it needs a sign-in. Use “Open in new tab”.
                    </div>
                )}
            </div>
        </div>
    );
}