(function () {
    function generateQrDataUrl(value) {
        return new Promise(function (resolve, reject) {
            try {
                var qr = qrcode(2, 'M');
                qr.addData(value);
                qr.make();
                var svgTag = qr.createSvgTag({ scalable: true });
                var dataUrl = 'data:image/svg+xml;base64,' + btoa(svgTag);
                resolve(dataUrl);
            } catch (e) {
                reject(e);
            }
        });
    }

    // Compatibility shim — existing callers use QRCode.toDataURL()
    window.QRCode = { toDataURL: generateQrDataUrl };

    function buildPrintHtml(labelsHtml) {
        return [
            "<!doctype html><html><head>",
            "<meta charset=\"utf-8\">",
            "<title>Print QR Code</title>",
            "<style>",
            "@page { size: 2in 1in landscape; margin: 0; }",
            "*, *::before, *::after { box-sizing: border-box; }",
            "html, body { margin: 0; padding: 0; background: #fff; }",
            "body { -webkit-print-color-adjust: exact; print-color-adjust: exact; }",
            ".label { width: 2in; height: 1in; display: flex; flex-direction: row; align-items: center; break-after: page; overflow: hidden; padding: 3px; gap: 4px; }",
            ".label:last-child { break-after: avoid; }",
            ".label img { height: 0.88in; width: 0.88in; flex: 0 0 auto; object-fit: contain; display: block; }",
            ".label-text { display: flex; flex-direction: column; justify-content: center; flex: 1 1 auto; overflow: hidden; }",
            ".label-title { font-family: Arial, sans-serif; font-size: 8pt; font-weight: bold; overflow: hidden; text-overflow: ellipsis; display: -webkit-box; -webkit-line-clamp: 3; -webkit-box-orient: vertical; word-break: break-word; margin-bottom: 4px; }",
            ".label-number { font-family: 'Courier New', monospace; font-size: 7pt; word-break: break-all; }",
            "</style>",
            "</head><body>",
            labelsHtml,
            "</body></html>"
        ].join("");
    }

    function openPrintFrame(html) {
        var printFrame = document.createElement("iframe");
        printFrame.style.cssText = "position:fixed;right:0;bottom:0;width:0;height:0;border:0;";
        document.body.appendChild(printFrame);
        var printWindow = printFrame.contentWindow;
        var printDoc = printWindow && printWindow.document;
        if (!printDoc) { printFrame.remove(); return; }

        var cleanedUp = false;
        function cleanup() {
            if (cleanedUp) return;
            cleanedUp = true;
            if (printFrame.parentNode) printFrame.parentNode.removeChild(printFrame);
        }

        printDoc.open();
        printDoc.write(html);
        printDoc.close();

        if (printWindow) {
            printWindow.onafterprint = cleanup;
        }

        setTimeout(function () {
            if (printWindow) {
                printWindow.focus();
                printWindow.print();
            }
            setTimeout(cleanup, 1500);
        }, 150);
    }

    function escapeHtml(str) {
        return (str || "").replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");
    }

    async function buildLabelHtml(barcodeValue, title) {
        var pngUrl = await generateQrDataUrl(barcodeValue);
        var titleHtml = title ? "<div class=\"label-title\">" + escapeHtml(title) + "</div>" : "";
        return "<div class=\"label\"><img src=\"" + pngUrl + "\" /><div class=\"label-text\">" + titleHtml + "<div class=\"label-number\">" + escapeHtml(barcodeValue) + "</div></div></div>";
    }

    async function openBarcodePrintWindow(barcodeValue, title) {
        if (!barcodeValue || typeof qrcode === "undefined") return;
        var labelHtml = await buildLabelHtml(barcodeValue, title || "");
        openPrintFrame(buildPrintHtml(labelHtml));
    }

    async function openBulkBarcodePrintWindow(items) {
        if (!items || items.length === 0 || typeof qrcode === "undefined") return;
        var labelParts = await Promise.all(items.map(function (item) {
            return buildLabelHtml(item.barcode, item.title || "");
        }));
        openPrintFrame(buildPrintHtml(labelParts.join("")));
    }

    window.InventoryBarcodePrint = {
        openBarcodePrintWindow: openBarcodePrintWindow,
        openBulkBarcodePrintWindow: openBulkBarcodePrintWindow,
        buildQrDataUrl: generateQrDataUrl
    };
})();
