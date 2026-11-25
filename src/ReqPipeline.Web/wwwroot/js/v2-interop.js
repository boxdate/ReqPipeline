window.reqPipelineV2 = {
    downloadTextFile: function (fileName, content, mimeType) {
        const blob = new Blob([content], { type: mimeType || "application/json;charset=utf-8" });
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement("a");
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    },
    copyToClipboard: async function (text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(text);
            return true;
        }
        const textarea = document.createElement("textarea");
        textarea.value = text;
        textarea.style.position = "fixed";
        textarea.style.opacity = "0";
        document.body.appendChild(textarea);
        textarea.select();
        const ok = document.execCommand("copy");
        document.body.removeChild(textarea);
        return ok;
    },
    _mermaidInitialized: false,
    _mermaidRenderSeq: 0,
    renderMermaid: async function (elementId, code) {
        const container = document.getElementById(elementId);
        if (!container) {
            return false;
        }
        if (typeof window.mermaid === "undefined") {
            container.textContent = "Mermaid.js が読み込まれていません（オフライン環境の可能性があります）。";
            return false;
        }
        try {
            if (!this._mermaidInitialized) {
                window.mermaid.initialize({
                    startOnLoad: false,
                    securityLevel: "strict",
                    theme: "default"
                });
                this._mermaidInitialized = true;
            }
            this._mermaidRenderSeq += 1;
            const renderId = "mermaid-svg-" + this._mermaidRenderSeq;
            const result = await window.mermaid.render(renderId, code);
            container.innerHTML = result.svg;
            return true;
        } catch (err) {
            container.textContent = "Mermaid 描画エラー: " + (err && err.message ? err.message : String(err));
            return false;
        }
    }
};
