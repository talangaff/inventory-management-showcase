(function () {
    function getCsrfToken() {
        var el = document.querySelector('input[name="__RequestVerificationToken"]');
        return el ? el.value : "";
    }

    function truncateAtWordBoundary(input, maxLength) {
        var normalized = (input || "").trim().replace(/\s+/g, " ");
        if (normalized.length <= maxLength) return normalized;
        var cutAt = normalized.lastIndexOf(" ", maxLength);
        return cutAt > 0 ? normalized.slice(0, cutAt) : normalized.slice(0, maxLength);
    }

    function openBarcodePrintWindow(barcodeValue, title) {
        if (!window.InventoryBarcodePrint) return;

        window.InventoryBarcodePrint.openBarcodePrintWindow(barcodeValue, title);
    }

    document.addEventListener("click", function (event) {
        var target = event.target;
        if (!(target instanceof Element)) return;

        // ── Print Barcode button ──────────────────────────────────────────────
        var printButton = target.closest(".orders-print-barcode-btn");
        if (printButton) {
            var barcodeNumber = printButton.dataset.barcode || "";
            var titleLabel = truncateAtWordBoundary(printButton.dataset.itemTitle || "", 40);
            openBarcodePrintWindow(barcodeNumber, titleLabel);
            return;
        }

        // ── "Which one shipped?" unit picker confirm (multi-unit items only) ──
        var unitPickBtn = target.closest(".unit-pick-confirm-btn");
        if (unitPickBtn) {
            var pickForm = unitPickBtn.closest(".unit-pick-form");
            if (!pickForm) return;
            var itemId = pickForm.dataset.itemId || "";
            var select = pickForm.querySelector(".unit-pick-select");
            var unitId = select ? select.value : "";
            if (!itemId || !unitId) return;

            unitPickBtn.disabled = true;
            unitPickBtn.textContent = "Saving…";

            fetch(appUrl("/Orders/MarkUnitSold"), {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": getCsrfToken()
                },
                body: JSON.stringify({ itemId: itemId, unitId: unitId })
            })
                .then(function (r) { return r.json(); })
                .then(function (data) {
                    if (data.success) {
                        pickForm.outerHTML = select.options[select.selectedIndex].textContent;
                    } else {
                        unitPickBtn.disabled = false;
                        unitPickBtn.textContent = "Confirm";
                    }
                })
                .catch(function () {
                    unitPickBtn.disabled = false;
                    unitPickBtn.textContent = "Confirm";
                });
        }
    });
}());
