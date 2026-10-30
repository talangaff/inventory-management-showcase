// Bulk Reprice page: select-all/per-row checkboxes and the Apply run. Rows are posted one at a
// time, in order, to InventoryController.BulkRepriceItem (form-encoded, same antiforgery/fetch
// conventions as needs-attention.js) so a long run never hits the proxy timeout and every row
// shows its own result. The server recomputes each new price from the previewed rule.
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        const applyBtn = document.getElementById("repriceApplyBtn");
        const selectAll = document.getElementById("repriceSelectAll");
        const rule = document.getElementById("repriceRule");
        const status = document.getElementById("repriceStatus");
        const antiForgeryForm = document.getElementById("repriceAntiForgeryForm");
        if (!applyBtn || !rule) return;

        let running = false;

        function getAntiForgeryToken() {
            const input = antiForgeryForm ? antiForgeryForm.querySelector("input[name='__RequestVerificationToken']") : null;
            return input ? input.value : "";
        }

        function rowBoxes() {
            return Array.from(document.querySelectorAll(".reprice-select"));
        }

        function selectedBoxes() {
            return rowBoxes().filter(function (box) { return box.checked && !box.disabled; });
        }

        function refreshApplyButton() {
            const count = selectedBoxes().length;
            applyBtn.textContent = "Apply to " + count + " selected";
            applyBtn.disabled = running || count === 0;
            if (selectAll) {
                const enabled = rowBoxes().filter(function (box) { return !box.disabled; });
                selectAll.checked = enabled.length > 0 && count === enabled.length;
                selectAll.indeterminate = count > 0 && count < enabled.length;
                selectAll.disabled = running || enabled.length === 0;
            }
        }

        function showStatus(message, kind) {
            if (!status) return;
            status.textContent = message;
            status.classList.remove("d-none", "alert-info", "alert-success", "alert-warning", "alert-danger");
            status.classList.add("alert-" + kind);
        }

        function setRowStatus(row, text, badgeClass) {
            const cell = row.querySelector(".reprice-row-status");
            if (!cell) return;
            cell.textContent = "";
            const badge = document.createElement("span");
            badge.className = "badge " + badgeClass;
            badge.textContent = text;
            cell.appendChild(badge);
        }

        if (selectAll) {
            selectAll.addEventListener("change", function () {
                rowBoxes().forEach(function (box) {
                    if (!box.disabled) box.checked = selectAll.checked;
                });
                refreshApplyButton();
            });
        }

        rowBoxes().forEach(function (box) {
            box.addEventListener("change", refreshApplyButton);
        });

        applyBtn.addEventListener("click", async function () {
            const boxes = selectedBoxes();
            if (boxes.length === 0 || running) return;

            const confirmed = confirm(
                "Lower the price of " + boxes.length + " item" + (boxes.length === 1 ? "" : "s") +
                " on eBay (" + rule.dataset.ruleText + ")?\n\nThis changes live eBay prices.");
            if (!confirmed) return;

            running = true;
            refreshApplyButton();

            let done = 0, changed = 0, failed = 0;
            for (const box of boxes) {
                const row = box.closest(".reprice-row");
                setRowStatus(row, "Updating…", "bg-info text-dark");
                showStatus("Repricing " + (done + 1) + " of " + boxes.length + "…", "info");

                let result;
                try {
                    const response = await fetch(appUrl("/Inventory/BulkRepriceItem"), {
                        method: "POST",
                        headers: {
                            "Content-Type": "application/x-www-form-urlencoded",
                            "X-Requested-With": "XMLHttpRequest",
                            "RequestVerificationToken": getAntiForgeryToken()
                        },
                        body: new URLSearchParams({
                            listingId: row.dataset.listingId,
                            expectedPrice: row.dataset.currentPrice,
                            mode: rule.dataset.mode,
                            amount: rule.dataset.amount,
                            minPrice: rule.dataset.minPrice || ""
                        }).toString()
                    });
                    result = await response.json();
                } catch (err) {
                    result = { success: false, message: "Network error — " + err.message };
                }

                done++;
                box.checked = false;
                box.disabled = true;
                if (result.success) {
                    changed++;
                    setRowStatus(row, "Updated to $" + Number(result.newPrice).toFixed(2), "bg-success");
                } else {
                    failed++;
                    setRowStatus(row, result.message || "Failed", result.skipped ? "bg-secondary" : "bg-danger");
                    // Leave failed rows selectable so they can be retried after a refresh/fix.
                    if (!result.skipped) {
                        box.disabled = false;
                    }
                }
            }

            running = false;
            refreshApplyButton();
            showStatus(
                "Done — " + changed + " repriced" + (failed > 0 ? ", " + failed + " not changed (see Status column)" : "") + ".",
                failed > 0 ? "warning" : "success");
        });

        refreshApplyButton();
    });
})();
