import { useEffect, useState } from "react";
import { downloadFile, fetchDocumentBlob, fetchLinkBlob } from "./api";
import Icon from "./designer/Icon";

/** source: { kind: "file", id, name }  or  { kind: "link", instanceId, index, url, label } */
export default function Viewer({ source, onClose }) {
    import { useDialogs } from "./Dialogs";
    const isFile = source.kind === "file";
    const { alert } = useDialogs();
    const [st, setSt] = useState({ loading: true, error: "", blobUrl: "", type: "" });

    useEffect(() => {
        let off = false;
        let objectUrl = "";
        const get = isFile ? fetchDocumentBlob(source.id) : fetchLinkBlob(source.instanceId, source.index);
        get
            .then((blob) => {
                if (off) return;
                objectUrl = URL.createObjectURL(blob);
                setSt({ loading: false, error: "", blobUrl: objectUrl, type: blob.type });
            })
            .catch((e) => { if (!off) setSt({ loading: false, error: e.message, blobUrl: "", type: "" }); });
        return () => { off = true; if (objectUrl) URL.revokeObjectURL(objectUrl); };
    }, [isFile, source.id, source.instanceId, source.index]);

    useEffect(() => {
        const h = (e) => e.key === "Escape" && onClose();
        document.addEventListener("keydown", h);
        return () => document.removeEventListener("keydown", h);
    }, [onClose]);

    const title = isFile ? source.name || "File" : source.label || source.url;
    const ready = !st.loading && !st.error;
    const isImage = st.type.startsWith("image/");
    const isFrame = st.type === "application/pdf" || st.type === "text/plain";
    const ext = st.type === "application/pdf" ? ".pdf" : "";

    return (
        <div className="vback" onClick={onClose}>
            <div className="vbox" onClick={(e) => e.stopPropagation()}>
                <div className="vhead">
                    <b title={title}>{title}</b>
                    {isFile && (
                        <button className="secondary" onClick={() => downloadFile(source.id, source.name).catch((e) => alert(e.message))}>
                            Download
                        </button>
                    )}
                    {!isFile && ready && (
                        <a className="btn" href={st.blobUrl} download={(source.label || "document") + ext}>Download</a>
                    )}
                    {!isFile && (
                        <a className="btn" href={source.url} target="_blank" rel="noopener noreferrer">Open in new tab</a>
                    )}
                    <button className="icon" onClick={onClose} title="Close"><Icon name="x" size={18} /></button>
                </div>

                <div className="vbody">
                    {st.loading && <div className="vmsg">Loading…</div>}
                    {st.error && <div className="vmsg"><b>Could not show the document</b><br />{st.error}</div>}
                    {ready && isImage && <img src={st.blobUrl} alt={title} />}
                    {ready && isFrame && <iframe title={title} src={st.blobUrl} />}
                    {ready && !isImage && !isFrame && (
                        <div className="vmsg">A preview is not available for this kind of document. Use Download{!isFile && " or Open in new tab"}.</div>
                    )}
                </div>
            </div>
        </div>
    );
}