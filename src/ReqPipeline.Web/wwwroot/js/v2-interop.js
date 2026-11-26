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
    downloadBase64File: function (fileName, base64, mimeType) {
        const binaryString = atob(base64);
        const bytes = new Uint8Array(binaryString.length);
        for (let i = 0; i < binaryString.length; i++) {
            bytes[i] = binaryString.charCodeAt(i);
        }
        const blob = new Blob([bytes], { type: mimeType || "application/octet-stream" });
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
                    theme: "base",
                    themeVariables: {
                        background: "#FAF9F6",
                        primaryColor: "#FFFFFF",
                        primaryTextColor: "#1C1917",
                        primaryBorderColor: "#2563EB",
                        secondaryColor: "#F3F1EC",
                        secondaryTextColor: "#1C1917",
                        secondaryBorderColor: "#D6D3CD",
                        tertiaryColor: "#EFF6FF",
                        tertiaryTextColor: "#1D4ED8",
                        tertiaryBorderColor: "#BFDBFE",
                        lineColor: "#57534E",
                        textColor: "#1C1917",
                        edgeLabelBackground: "#FAF9F6",
                        labelBackgroundColor: "#FAF9F6",
                        transitionColor: "#57534E",
                        transitionLabelColor: "#1C1917",
                        stateBkg: "#FFFFFF",
                        stateLabelColor: "#1C1917",
                        compositeBackground: "#F3F1EC",
                        specialStateColor: "#1D4ED8",
                        fontFamily: '-apple-system, BlinkMacSystemFont, "Segoe UI", "Hiragino Kaku Gothic ProN", "Yu Gothic UI", Meiryo, sans-serif',
                        fontSize: "13px"
                    }
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
