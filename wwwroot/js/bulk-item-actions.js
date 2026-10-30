// Shared bulk-select toolbar logic for storage-location Details pages (Box, Shelf,
// StorageUnit). Pairs with Views/Shared/_BulkItemActionsBar.cshtml. Each selectable item must
// be an ".bulk-item-checkbox" input with data-item-id, inside a ".bulk-select-item" wrapper
// that gets removed from the DOM once the item's placement changes.
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        const config = document.getElementById("bulkDetailConfig");
        const actionBar = document.getElementById("bulkDetailActionBar");
        const barCount = document.getElementById("bulkDetailBarCount");
        const shelfUnitSelect = document.getElementById("bulkDetailShelfUnitSelect");
        const shelfNumberSelect = document.getElementById("bulkDetailShelfNumberSelect");
        const boxSelect = document.getElementById("bulkDetailBoxSelect");
        const assignShelfBtn = document.getElementById("bulkDetailAssignShelfBtn");
        const assignBoxBtn = document.getElementById("bulkDetailAssignBoxBtn");
        const removeLocationBtn = document.getElementById("bulkDetailRemoveLocationBtn");
        const cancelBtn = document.getElementById("bulkDetailCancelBtn");
        const antiForgeryForm = document.getElementById("bulkDetailAntiForgeryForm");

        if (!actionBar) return; // page didn't include the bulk toolbar partial

        let shelfUnits = [];
        let boxes = [];
        if (config) {
            try { shelfUnits = JSON.parse(config.dataset.shelfUnits || "[]"); } catch (e) { /* ignore */ }
            try { boxes = JSON.parse(config.dataset.boxes || "[]"); } catch (e) { /* ignore */ }
        }

        function populateDropdowns() {
            if (shelfUnitSelect) {
                shelfUnitSelect.innerHTML = "";
                shelfUnits.forEach(function (u) {
                    const opt = document.createElement("option");
                    opt.value = u.Id || u.id;
                    opt.dataset.shelfCount = String(u.ShelfCount || u.shelfCount || 0);
                    opt.textContent = (u.Identifier || u.identifier) + " (" + (u.ShelfCount || u.shelfCount) + " shelves)";
                    shelfUnitSelect.appendChild(opt);
                });
                refreshShelfNumbers();
            }
            if (boxSelect) {
                boxSelect.innerHTML = "";
                boxes.forEach(function (b) {
                    const opt = document.createElement("option");
                    opt.value = b.Id || b.id;
                    opt.textContent = (b.BoxIdentifier || b.boxIdentifier) + " (" + (b.BoxBarcode || b.boxBarcode) + ")";
                    boxSelect.appendChild(opt);
                });
            }
        }

        function refreshShelfNumbers() {
            if (!shelfUnitSelect || !shelfNumberSelect) return;
            const selected = shelfUnitSelect.options[shelfUnitSelect.selectedIndex];
            const count = Number(selected?.dataset?.shelfCount || "0");
            const prev = shelfNumberSelect.value;
            shelfNumberSelect.innerHTML = "";
            for (let i = count; i >= 1; i--) {
                const opt = document.createElement("option");
                opt.value = String(i);
                opt.textContent = String(i);
                shelfNumberSelect.appendChild(opt);
            }
            if (prev && Number(prev) <= count) {
                shelfNumberSelect.value = prev;
            }
        }

        if (shelfUnitSelect) shelfUnitSelect.addEventListener("change", refreshShelfNumbers);

        function getCheckedItems() {
            return Array.from(document.querySelectorAll(".bulk-item-checkbox:checked"));
        }

        function getCheckedItemIds() {
            return getCheckedItems().map(function (cb) { return cb.dataset.itemId; }).filter(Boolean);
        }

        function updateSelectionUI() {
            const count = getCheckedItemIds().length;
            if (barCount) barCount.textContent = count + " selected";
            actionBar.classList.toggle("d-none", count === 0);
            document.body.style.paddingBottom = count > 0 ? "80px" : "";
        }

        document.addEventListener("change", function (e) {
            if (e.target && e.target.classList.contains("bulk-item-checkbox")) {
                updateSelectionUI();
            }
        });

        function getAntiForgeryToken() {
            if (!antiForgeryForm) return "";
            const input = antiForgeryForm.querySelector("input[name='__RequestVerificationToken']");
            return input ? input.value : "";
        }

        function clearSelection() {
            document.querySelectorAll(".bulk-item-checkbox:checked").forEach(function (cb) { cb.checked = false; });
            updateSelectionUI();
        }

        function removeSelectedRowsFromDom(itemIds) {
            itemIds.forEach(function (id) {
                const cb = document.querySelector('.bulk-item-checkbox[data-item-id="' + id + '"]');
                const row = cb ? cb.closest(".bulk-select-item") : null;
                if (row) row.remove();
            });
        }

        async function postJson(url, body) {
            const response = await fetch(url, {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": getAntiForgeryToken()
                },
                body: JSON.stringify(body)
            });
            return response.json();
        }

        async function bulkAssignShelf() {
            const itemIds = getCheckedItemIds();
            if (itemIds.length === 0 || !shelfUnitSelect || !shelfNumberSelect) return;
            const unitLabel = shelfUnitSelect.options[shelfUnitSelect.selectedIndex]?.textContent || "the selected shelf";
            const confirmed = confirm(
                "Assign " + itemIds.length + " item" + (itemIds.length === 1 ? "" : "s") +
                " to " + unitLabel + "-" + shelfNumberSelect.value + "?");
            if (!confirmed) return;

            assignShelfBtn.disabled = true;
            try {
                const result = await postJson(appUrl("/Inventory/BulkAssignLocation"), {
                    ItemIds: itemIds,
                    ShelfUnitId: shelfUnitSelect.value,
                    ShelfNumber: Number(shelfNumberSelect.value)
                });
                if (!result.success) {
                    alert(result.message || "Bulk assign failed.");
                    return;
                }
                removeSelectedRowsFromDom(itemIds);
                clearSelection();
            } catch (e) {
                alert("Bulk assign failed.");
                console.error(e);
            } finally {
                assignShelfBtn.disabled = false;
            }
        }

        async function bulkAssignBox() {
            const itemIds = getCheckedItemIds();
            if (itemIds.length === 0 || !boxSelect) return;
            const boxLabel = boxSelect.options[boxSelect.selectedIndex]?.textContent || "the selected box";
            const confirmed = confirm(
                "Assign " + itemIds.length + " item" + (itemIds.length === 1 ? "" : "s") +
                " to box " + boxLabel + "?");
            if (!confirmed) return;

            assignBoxBtn.disabled = true;
            try {
                const result = await postJson(appUrl("/Inventory/BulkAssignBox"), {
                    ItemIds: itemIds,
                    BoxId: boxSelect.value
                });
                if (!result.success) {
                    alert(result.message || "Bulk assign failed.");
                    return;
                }
                removeSelectedRowsFromDom(itemIds);
                clearSelection();
            } catch (e) {
                alert("Bulk assign failed.");
                console.error(e);
            } finally {
                assignBoxBtn.disabled = false;
            }
        }

        async function bulkRemoveLocation() {
            const itemIds = getCheckedItemIds();
            if (itemIds.length === 0) return;
            const confirmed = confirm(
                "Remove location for " + itemIds.length + " item" + (itemIds.length === 1 ? "" : "s") +
                "? They will show as having no storage location.");
            if (!confirmed) return;

            removeLocationBtn.disabled = true;
            try {
                const result = await postJson(appUrl("/Inventory/BulkRemoveLocation"), { ItemIds: itemIds });
                if (!result.success) {
                    alert(result.message || "Bulk remove failed.");
                    return;
                }
                removeSelectedRowsFromDom(itemIds);
                clearSelection();
            } catch (e) {
                alert("Bulk remove failed.");
                console.error(e);
            } finally {
                removeLocationBtn.disabled = false;
            }
        }

        if (assignShelfBtn) assignShelfBtn.addEventListener("click", bulkAssignShelf);
        if (assignBoxBtn) assignBoxBtn.addEventListener("click", bulkAssignBox);
        if (removeLocationBtn) removeLocationBtn.addEventListener("click", bulkRemoveLocation);
        if (cancelBtn) cancelBtn.addEventListener("click", clearSelection);

        populateDropdowns();
        updateSelectionUI();
    });
})();
