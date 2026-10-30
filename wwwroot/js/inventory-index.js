(function () {
    const inventoryConfig = document.getElementById("inventoryConfig");
    if (!inventoryConfig) return;

    const inventoryMode = inventoryConfig.dataset.mode || "active";
    const inventoryIsConnected = inventoryConfig.dataset.isConnected === "true";
    const inventorySort = inventoryConfig.dataset.sort || "title";
    let inventoryHasMore = inventoryConfig.dataset.hasMore === "true";
    let inventoryCursor = Number(inventoryConfig.dataset.cursor || "1");
    let inventoryLoadingMore = false;

    const inventoryAjaxStatus = document.getElementById("inventoryAjaxStatus");
    const inventorySearchInput = document.getElementById("inventorySearchInput");
    const inventoryList = document.getElementById("inventoryList");
    const inventoryLoadSentinel = document.getElementById("inventoryLoadSentinel");
    const inventoryLoadMoreWrap = document.getElementById("inventoryLoadMoreWrap");
    const inventoryLoadMoreBtn = document.getElementById("inventoryLoadMoreBtn");

    function showAjaxStatus(message, isSuccess) {
        if (!inventoryAjaxStatus) {
            return;
        }

        inventoryAjaxStatus.classList.remove("d-none", "alert-success", "alert-danger");
        inventoryAjaxStatus.classList.add(isSuccess ? "alert-success" : "alert-danger");
        inventoryAjaxStatus.textContent = message;
    }

    function truncateAtWordBoundary(input, maxLength) {
        const normalized = (input || "").trim().replace(/\s+/g, " ");
        if (normalized.length <= maxLength) {
            return normalized;
        }

        const cutAt = normalized.lastIndexOf(" ", maxLength);
        if (cutAt > 0) {
            return normalized.slice(0, cutAt);
        }

        return normalized.slice(0, maxLength);
    }

    function refreshShelfNumbersForSelect(unitSelect) {
        const itemId = unitSelect?.dataset?.itemId;
        if (!itemId) {
            return;
        }

        const shelfSelect = document.querySelector(`.inventory-shelf-number-select[data-item-id="${itemId}"]`);
        if (!shelfSelect) {
            return;
        }

        const selectedOption = unitSelect.options[unitSelect.selectedIndex];
        const shelfCount = Number(selectedOption?.dataset?.shelfCount || "0");
        const previousValue = shelfSelect.value;

        shelfSelect.innerHTML = "";
        for (let i = shelfCount; i >= 1; i--) {
            const option = document.createElement("option");
            option.value = String(i);
            option.textContent = String(i);
            shelfSelect.appendChild(option);
        }

        if (previousValue && Number(previousValue) <= shelfCount) {
            shelfSelect.value = previousValue;
        }
    }

    function initializeInventoryRowControls(rootElement) {
        const root = rootElement || document;
        root.querySelectorAll(".inventory-shelf-unit-select").forEach((select) => {
            refreshShelfNumbersForSelect(select);
            if (!select.dataset.boundChange) {
                select.addEventListener("change", () => refreshShelfNumbersForSelect(select));
                select.dataset.boundChange = "true";
            }
        });
    }

    function openBarcodePrintWindow(barcodeValue, title) {
        if (window.InventoryBarcodePrint) {
            window.InventoryBarcodePrint.openBarcodePrintWindow(barcodeValue, title);
        }
    }

    async function submitInventoryForm(form, mode) {
        try {
            const formData = new FormData(form);
            const response = await fetch(form.action, {
                method: "POST",
                body: formData,
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            const result = await response.json();
            if (!response.ok || !result.success) {
                showAjaxStatus(result.message || "Save failed.", false);
                return;
            }

            const itemRow = form.closest(".inventory-list-item");
            if (!itemRow) {
                if (result.message) {
                    showAjaxStatus(result.message, true);
                }
                return;
            }

            const shelfBadge = itemRow.querySelector(".inventory-shelf-badge");
            const boxBadge = itemRow.querySelector(".inventory-box-badge");
            const boxLocationBadge = itemRow.querySelector(".inventory-box-location-badge");

            if (mode === "shelf") {
                if (shelfBadge) {
                    shelfBadge.textContent = `${result.shelfUnitIdentifier}-${result.shelfNumber} (${result.shelfBarcode})`;
                    shelfBadge.classList.remove("d-none");
                }
                if (boxBadge) {
                    boxBadge.classList.add("d-none");
                }
                if (boxLocationBadge) {
                    boxLocationBadge.classList.add("d-none");
                }
            } else if (mode === "box") {
                if (boxBadge) {
                    boxBadge.textContent = `Box ${result.boxBarcode}`;
                    boxBadge.classList.remove("d-none");
                }
                if (boxLocationBadge) {
                    boxLocationBadge.textContent = `Box Location: ${result.shelfUnitIdentifier}-${result.shelfNumber} (${result.shelfBarcode})`;
                    boxLocationBadge.classList.remove("d-none");
                }
                if (shelfBadge) {
                    shelfBadge.classList.add("d-none");
                }
            }

            if (result.removeFromMissingData) {
                itemRow.remove();
            }

            if (result.message) {
                showAjaxStatus(result.message, true);
            }
        } catch (error) {
            showAjaxStatus("Save failed.", false);
            console.error(error);
        }
    }

    async function submitRemoveFromStorageForm(form) {
        try {
            const formData = new FormData(form);
            const response = await fetch(form.action, {
                method: "POST",
                body: formData,
                headers: {
                    "X-Requested-With": "XMLHttpRequest"
                }
            });

            const result = await response.json();
            if (!response.ok || !result.success) {
                showAjaxStatus(result.message || "Remove from storage failed.", false);
                return;
            }

            const itemRow = form.closest(".inventory-list-item");
            if (itemRow && result.addToMissingData) {
                const shelfBadge = itemRow.querySelector(".inventory-shelf-badge");
                const boxBadge = itemRow.querySelector(".inventory-box-badge");
                const boxLocationBadge = itemRow.querySelector(".inventory-box-location-badge");
                if (shelfBadge) shelfBadge.classList.add("d-none");
                if (boxBadge) boxBadge.classList.add("d-none");
                if (boxLocationBadge) boxLocationBadge.classList.add("d-none");
                form.remove();
            }

            if (result.message) {
                showAjaxStatus(result.message, true);
            }
        } catch (error) {
            showAjaxStatus("Remove from storage failed.", false);
            console.error(error);
        }
    }

    function applyInventorySearchVisibility() {
        const term = (inventorySearchInput?.value || "").trim().toLowerCase();
        const words = term ? term.split(/\s+/).filter(Boolean) : [];
        const rows = Array.from(document.querySelectorAll(".inventory-list-item"));
        rows.forEach((item) => {
            const titleText = (item.querySelector("a.fw-bold")?.textContent || item.querySelector("span.fw-semibold")?.textContent || "").toLowerCase();
            const itemIdText = item.querySelector(".small.text-muted")?.textContent?.toLowerCase() || "";
            const barcodeText = item.querySelector("[data-barcode]")?.dataset?.barcode?.toLowerCase() || "";
            const shelfBadgeText = item.querySelector(".inventory-shelf-badge")?.textContent?.toLowerCase() || "";
            const boxBadgeText = item.querySelector(".inventory-box-badge")?.textContent?.toLowerCase() || "";
            const boxLocationBadgeText = item.querySelector(".inventory-box-location-badge")?.textContent?.toLowerCase() || "";
            const searchable = `${titleText} ${itemIdText} ${barcodeText} ${shelfBadgeText} ${boxBadgeText} ${boxLocationBadgeText}`;

            const matches = !term || words.every((word) => searchable.includes(word));
            item.classList.toggle("d-none", !matches);
        });

        if (term) {
            inventoryLoadSentinel?.classList.add("d-none");
            inventoryLoadMoreWrap?.classList.add("d-none");
        } else if (inventoryHasMore) {
            inventoryLoadSentinel?.classList.remove("d-none");
            inventoryLoadMoreWrap?.classList.remove("d-none");
        }
    }

    async function loadMoreInventory(force) {
        if (!inventoryHasMore || inventoryLoadingMore || !inventoryList) {
            return;
        }

        if (!force && (inventorySearchInput?.value || "").trim().length > 0) {
            return;
        }

        inventoryLoadingMore = true;

        try {
            const response = await fetch(appUrl(`/Inventory/LoadMore?mode=${encodeURIComponent(inventoryMode)}&cursor=${inventoryCursor}&sort=${encodeURIComponent(inventorySort)}`), {
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });

            if (!response.ok) {
                return;
            }

            const html = await response.text();
            const temp = document.createElement("div");
            temp.innerHTML = html;
            const newItems = Array.from(temp.querySelectorAll(".inventory-list-item"));
            var selectAllActive = bulkSelectAllCheckbox && bulkSelectAllCheckbox.checked;
            newItems.forEach((item) => {
                inventoryList.appendChild(item);
                if (selectAllActive) {
                    var cb = item.querySelector(".bulk-item-checkbox");
                    if (cb) cb.checked = true;
                }
            });
            newItems.forEach((item) => initializeInventoryRowControls(item));

            inventoryHasMore = (response.headers.get("X-Has-More") || "false") === "true";
            const nextCursorHeader = response.headers.get("X-Next-Cursor");
            if (nextCursorHeader) {
                inventoryCursor = Number(nextCursorHeader);
            }

            if (!inventoryHasMore && inventoryLoadSentinel) {
                inventoryLoadSentinel.classList.add("d-none");
            }
            if (!inventoryHasMore && inventoryLoadMoreWrap) {
                inventoryLoadMoreWrap.classList.add("d-none");
            }

            applyInventorySearchVisibility();
        } finally {
            inventoryLoadingMore = false;
        }
    }

    let inventorySearchDebounceTimer = null;
    let inventorySearchAbortController = null;
    let inventoryInSearchMode = false;

    async function performSearch(term) {
        if (inventorySearchAbortController) {
            inventorySearchAbortController.abort();
            inventorySearchAbortController = null;
        }
        if (!term || !inventoryList) return;

        const controller = new AbortController();
        inventorySearchAbortController = controller;
        try {
            const url = appUrl(`/Inventory/Search?q=${encodeURIComponent(term)}&mode=${encodeURIComponent(inventoryMode)}&sort=${encodeURIComponent(inventorySort)}`);
            const response = await fetch(url, {
                headers: { "X-Requested-With": "XMLHttpRequest" },
                signal: controller.signal
            });
            if (!response.ok) return;
            const html = await response.text();
            inventoryList.innerHTML = html;
            initializeInventoryRowControls(inventoryList);
            updateBulkSelectionUI();
        } catch (e) {
            if (e.name !== "AbortError") console.error(e);
        } finally {
            if (inventorySearchAbortController === controller) inventorySearchAbortController = null;
        }
    }

    function handleSearchInput() {
        const term = (inventorySearchInput?.value || "").trim();
        clearTimeout(inventorySearchDebounceTimer);
        applyInventorySearchVisibility();

        if (!term) {
            if (inventoryInSearchMode) {
                inventoryInSearchMode = false;
                if (inventoryList) {
                    inventoryList.innerHTML = "";
                    inventoryCursor = 1;
                    inventoryHasMore = true;
                    inventoryLoadingMore = false;
                }
                loadMoreInventory(true);
            }
            return;
        }

        inventoryInSearchMode = true;
        inventorySearchDebounceTimer = setTimeout(() => performSearch(term), 300);
    }

    function openBulkBarcodePrintWindow(items) {
        if (window.InventoryBarcodePrint) {
            window.InventoryBarcodePrint.openBulkBarcodePrintWindow(items);
        }
    }

    // Removes a single unit's print row; if that was the listing's last remaining unprinted
    // unit, removes the whole listing row too (it's done, matching the "unprinted" filter).
    function removeUnitPrintRow(unitId) {
        var btn = document.querySelector('.print-barcode-btn[data-unit-id="' + unitId + '"]');
        if (!btn) return;
        var unitRow = btn.closest(".inventory-unit-print-row");
        var listingRow = btn.closest(".inventory-list-item");
        if (unitRow) unitRow.remove();
        if (listingRow && !listingRow.querySelector(".inventory-unit-print-row")) {
            listingRow.remove();
        }
    }

    async function markLabelsPrinted(unitIds, onSuccess) {
        if (!unitIds || unitIds.length === 0) return;
        const token = document.querySelector('#bulkAntiForgeryForm input[name="__RequestVerificationToken"]')?.value || "";
        try {
            await fetch(appUrl("/Inventory/MarkLabelPrinted"), {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "RequestVerificationToken": token
                },
                body: JSON.stringify({ unitIds: unitIds })
            });
        } catch (e) {
            console.error("Failed to mark labels printed:", e);
        }
        if (typeof onSuccess === "function") onSuccess();
    }

    var printAllUnprintedBtn = document.getElementById("printAllUnprintedBtn");
    if (printAllUnprintedBtn) {
        printAllUnprintedBtn.addEventListener("click", async function () {
            printAllUnprintedBtn.disabled = true;
            printAllUnprintedBtn.textContent = "Loading...";
            try {
                var response = await fetch(appUrl("/Inventory/UnprintedBarcodes"), {
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });
                var items = await response.json();
                if (items.length === 0) {
                    showAjaxStatus("No unprinted items found.", false);
                    return;
                }
                openBulkBarcodePrintWindow(items);
                var unitIds = items.map(function (i) { return i.unitId; });
                await markLabelsPrinted(unitIds, function () {
                    unitIds.forEach(removeUnitPrintRow);
                });
            } catch (e) {
                showAjaxStatus("Failed to load barcodes for printing.", false);
                console.error(e);
            } finally {
                printAllUnprintedBtn.disabled = false;
                printAllUnprintedBtn.textContent = "Print All QR Codes & Mark Printed";
            }
        });
    }

    var printAllBtn = document.getElementById("printAllBarcodesBtn");
    if (printAllBtn) {
        printAllBtn.addEventListener("click", async function () {
            printAllBtn.disabled = true;
            printAllBtn.textContent = "Loading...";
            try {
                var response = await fetch(appUrl("/Inventory/MissingDataBarcodes"), {
                    headers: { "X-Requested-With": "XMLHttpRequest" }
                });
                var items = await response.json();
                if (items.length === 0) {
                    showAjaxStatus("No missing data items to print.", false);
                    return;
                }
                openBulkBarcodePrintWindow(items);
            } catch (e) {
                showAjaxStatus("Failed to load barcodes for printing.", false);
                console.error(e);
            } finally {
                printAllBtn.disabled = false;
                printAllBtn.textContent = "Print All QR Codes";
            }
        });
    }

    document.addEventListener("click", (event) => {
        const target = event.target;
        if (!(target instanceof Element)) {
            return;
        }

        const printButton = target.closest(".print-barcode-btn");
        if (printButton) {
            const barcodeNumber = printButton.dataset.barcode || "";
            const unitId = printButton.dataset.unitId || "";
            const itemRow = printButton.closest(".inventory-list-item");
            const rawTitle = (printButton.dataset.itemTitle || itemRow?.querySelector("strong")?.textContent || "").trim();
            const titleLabel = truncateAtWordBoundary(rawTitle, 40);
            openBarcodePrintWindow(barcodeNumber, titleLabel);
            if (unitId) {
                markLabelsPrinted([unitId], inventoryMode === "unprinted" ? function () { removeUnitPrintRow(unitId); } : null);
            }
        }
    });

    document.addEventListener("submit", (event) => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) {
            return;
        }

        if (form.classList.contains("inventory-assign-location-form")) {
            event.preventDefault();
            submitInventoryForm(form, "shelf");
            return;
        }

        if (form.classList.contains("inventory-assign-box-form")) {
            event.preventDefault();
            submitInventoryForm(form, "box");
            return;
        }

        if (form.classList.contains("inventory-remove-storage-form")) {
            event.preventDefault();
            submitRemoveFromStorageForm(form);
        }
    });

    if (inventorySearchInput) {
        inventorySearchInput.addEventListener("input", handleSearchInput);
    }
    if (inventoryLoadMoreBtn) {
        inventoryLoadMoreBtn.addEventListener("click", () => loadMoreInventory(false));
    }

    function tryLoadMoreAtBottom() {
        if (!inventoryHasMore || inventoryLoadingMore) {
            return;
        }

        const nearBottom = window.innerHeight + window.scrollY >= document.body.offsetHeight - 300;
        if (nearBottom) {
            loadMoreInventory(false);
        }
    }

    let inventoryObserver = null;
    if (inventoryLoadSentinel && "IntersectionObserver" in window) {
        inventoryObserver = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (entry.isIntersecting) {
                    loadMoreInventory(false);
                }
            }
        }, { root: null, rootMargin: "600px 0px 600px 0px", threshold: 0.01 });

        inventoryObserver.observe(inventoryLoadSentinel);
    } else {
        window.addEventListener("scroll", tryLoadMoreAtBottom);
    }

    window.addEventListener("scroll", tryLoadMoreAtBottom);
    document.querySelector(".dashboard-content main")?.addEventListener("scroll", tryLoadMoreAtBottom);

    // ─── Bulk Selection Logic ───────────────────────────────────────────
    const bulkConfig = document.getElementById("bulkConfig");
    const bulkActionBar = document.getElementById("bulkActionBar");
    const bulkSelectAllCheckbox = document.getElementById("bulkSelectAllCheckbox");
    const bulkSelectedCount = document.getElementById("bulkSelectedCount");
    const bulkBarCount = document.getElementById("bulkBarCount");
    const bulkShelfUnitSelect = document.getElementById("bulkShelfUnitSelect");
    const bulkShelfNumberSelect = document.getElementById("bulkShelfNumberSelect");
    const bulkBoxSelect = document.getElementById("bulkBoxSelect");
    const bulkAssignShelfBtn = document.getElementById("bulkAssignShelfBtn");
    const bulkAssignBoxBtn = document.getElementById("bulkAssignBoxBtn");
    const bulkRemoveLocationBtn = document.getElementById("bulkRemoveLocationBtn");
    const bulkCancelBtn = document.getElementById("bulkCancelBtn");
    const bulkAntiForgeryForm = document.getElementById("bulkAntiForgeryForm");

    let bulkShelfUnits = [];
    let bulkBoxes = [];
    let bulkReturnAction = "";

    if (bulkConfig) {
        try { bulkShelfUnits = JSON.parse(bulkConfig.dataset.shelfUnits || "[]"); } catch (e) { /* ignore */ }
        try { bulkBoxes = JSON.parse(bulkConfig.dataset.boxes || "[]"); } catch (e) { /* ignore */ }
        bulkReturnAction = bulkConfig.dataset.returnAction || "";
    }

    function populateBulkDropdowns() {
        if (bulkShelfUnitSelect) {
            bulkShelfUnitSelect.innerHTML = "";
            bulkShelfUnits.forEach(function (u) {
                var opt = document.createElement("option");
                opt.value = u.Id || u.id;
                opt.dataset.shelfCount = String(u.ShelfCount || u.shelfCount || 0);
                opt.dataset.barcodePrefix = u.BarcodePrefix || u.barcodePrefix || "";
                opt.textContent = (u.Identifier || u.identifier) + " (" + (u.ShelfCount || u.shelfCount) + " shelves)";
                bulkShelfUnitSelect.appendChild(opt);
            });
            refreshBulkShelfNumbers();
        }
        if (bulkBoxSelect) {
            bulkBoxSelect.innerHTML = "";
            bulkBoxes.forEach(function (b) {
                var opt = document.createElement("option");
                opt.value = b.Id || b.id;
                opt.textContent = (b.BoxIdentifier || b.boxIdentifier) + " (" + (b.BoxBarcode || b.boxBarcode) + ")";
                bulkBoxSelect.appendChild(opt);
            });
        }
    }

    function refreshBulkShelfNumbers() {
        if (!bulkShelfUnitSelect || !bulkShelfNumberSelect) return;
        var selected = bulkShelfUnitSelect.options[bulkShelfUnitSelect.selectedIndex];
        var count = Number(selected?.dataset?.shelfCount || "0");
        var prev = bulkShelfNumberSelect.value;
        bulkShelfNumberSelect.innerHTML = "";
        for (var i = count; i >= 1; i--) {
            var opt = document.createElement("option");
            opt.value = String(i);
            opt.textContent = String(i);
            bulkShelfNumberSelect.appendChild(opt);
        }
        if (prev && Number(prev) <= count) {
            bulkShelfNumberSelect.value = prev;
        }
    }

    if (bulkShelfUnitSelect) {
        bulkShelfUnitSelect.addEventListener("change", refreshBulkShelfNumbers);
    }

    function getCheckedItemIds() {
        return Array.from(document.querySelectorAll(".bulk-item-checkbox:checked"))
            .map(function (cb) { return cb.dataset.itemId; })
            .filter(Boolean);
    }

    function getVisibleCheckboxes() {
        return Array.from(document.querySelectorAll(".inventory-list-item:not(.d-none) .bulk-item-checkbox"));
    }

    function updateBulkSelectionUI() {
        var count = getCheckedItemIds().length;
        var label = count + " selected";
        if (bulkSelectedCount) {
            bulkSelectedCount.textContent = label;
            bulkSelectedCount.classList.toggle("d-none", count === 0);
        }
        if (bulkBarCount) {
            bulkBarCount.textContent = label;
        }
        if (bulkActionBar) {
            bulkActionBar.classList.toggle("d-none", count === 0);
            document.body.style.paddingBottom = count > 0 ? "80px" : "";
        }
        // Update select-all checkbox state
        if (bulkSelectAllCheckbox) {
            var visible = getVisibleCheckboxes();
            var checkedVisible = visible.filter(function (cb) { return cb.checked; });
            bulkSelectAllCheckbox.checked = visible.length > 0 && checkedVisible.length === visible.length;
            bulkSelectAllCheckbox.indeterminate = checkedVisible.length > 0 && checkedVisible.length < visible.length;
        }
    }

    if (bulkSelectAllCheckbox) {
        bulkSelectAllCheckbox.addEventListener("change", function () {
            var checked = bulkSelectAllCheckbox.checked;
            getVisibleCheckboxes().forEach(function (cb) { cb.checked = checked; });
            updateBulkSelectionUI();
        });
    }

    // Delegated listener for individual checkboxes
    document.addEventListener("change", function (e) {
        if (e.target && e.target.classList.contains("bulk-item-checkbox")) {
            updateBulkSelectionUI();
        }
    });

    function getBulkAntiForgeryToken() {
        if (!bulkAntiForgeryForm) return "";
        var input = bulkAntiForgeryForm.querySelector("input[name='__RequestVerificationToken']");
        return input ? input.value : "";
    }

    async function bulkAssignShelf() {
        var itemIds = getCheckedItemIds();
        if (itemIds.length === 0) return;
        if (!bulkShelfUnitSelect || !bulkShelfNumberSelect) return;

        var unitLabel = bulkShelfUnitSelect.options[bulkShelfUnitSelect.selectedIndex]?.textContent || "the selected shelf";
        var confirmed = confirm(
            "Assign " + itemIds.length + " item" + (itemIds.length === 1 ? "" : "s") +
            " to " + unitLabel + "-" + bulkShelfNumberSelect.value + "?");
        if (!confirmed) return;

        bulkAssignShelfBtn.disabled = true;
        try {
            var response = await fetch(appUrl("/Inventory/BulkAssignLocation"), {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": getBulkAntiForgeryToken()
                },
                body: JSON.stringify({
                    ItemIds: itemIds,
                    ShelfUnitId: bulkShelfUnitSelect.value,
                    ShelfNumber: Number(bulkShelfNumberSelect.value)
                })
            });
            var result = await response.json();
            if (!result.success) {
                showAjaxStatus(result.message || "Bulk assign failed.", false);
                return;
            }
            var isMissing = inventoryMode === "missing";
            itemIds.forEach(function (id) {
                var cb = document.querySelector('.bulk-item-checkbox[data-item-id="' + id + '"]');
                var row = cb ? cb.closest(".inventory-list-item") : null;
                if (!row) return;
                if (isMissing) {
                    row.remove();
                } else {
                    var shelfBadge = row.querySelector(".inventory-shelf-badge");
                    var boxBadge = row.querySelector(".inventory-box-badge");
                    var boxLocBadge = row.querySelector(".inventory-box-location-badge");
                    if (shelfBadge) {
                        shelfBadge.textContent = result.shelfUnitIdentifier + "-" + result.shelfNumber + " (" + result.shelfBarcode + ")";
                        shelfBadge.classList.remove("d-none");
                    }
                    if (boxBadge) boxBadge.classList.add("d-none");
                    if (boxLocBadge) boxLocBadge.classList.add("d-none");
                }
            });
            bulkClearSelection();
            showAjaxStatus("Assigned " + result.assignedCount + " item(s) to " + result.shelfUnitIdentifier + "-" + result.shelfNumber + ".", true);
        } catch (e) {
            showAjaxStatus("Bulk assign failed.", false);
            console.error(e);
        } finally {
            bulkAssignShelfBtn.disabled = false;
        }
    }

    async function bulkAssignBox() {
        var itemIds = getCheckedItemIds();
        if (itemIds.length === 0) return;
        if (!bulkBoxSelect) return;

        var boxLabel = bulkBoxSelect.options[bulkBoxSelect.selectedIndex]?.textContent || "the selected box";
        var confirmed = confirm(
            "Assign " + itemIds.length + " item" + (itemIds.length === 1 ? "" : "s") +
            " to box " + boxLabel + "?");
        if (!confirmed) return;

        bulkAssignBoxBtn.disabled = true;
        try {
            var response = await fetch(appUrl("/Inventory/BulkAssignBox"), {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": getBulkAntiForgeryToken()
                },
                body: JSON.stringify({
                    ItemIds: itemIds,
                    BoxId: bulkBoxSelect.value
                })
            });
            var result = await response.json();
            if (!result.success) {
                showAjaxStatus(result.message || "Bulk assign failed.", false);
                return;
            }
            var isMissing = inventoryMode === "missing";
            itemIds.forEach(function (id) {
                var cb = document.querySelector('.bulk-item-checkbox[data-item-id="' + id + '"]');
                var row = cb ? cb.closest(".inventory-list-item") : null;
                if (!row) return;
                if (isMissing) {
                    row.remove();
                } else {
                    var shelfBadge = row.querySelector(".inventory-shelf-badge");
                    var boxBadge = row.querySelector(".inventory-box-badge");
                    var boxLocBadge = row.querySelector(".inventory-box-location-badge");
                    if (boxBadge) {
                        boxBadge.textContent = "Box " + result.boxIdentifier + " (" + result.boxBarcode + ")";
                        boxBadge.classList.remove("d-none");
                    }
                    if (boxLocBadge) {
                        boxLocBadge.textContent = "Box Location: " + result.shelfUnitIdentifier + "-" + result.shelfNumber + " (" + result.shelfBarcode + ")";
                        boxLocBadge.classList.remove("d-none");
                    }
                    if (shelfBadge) shelfBadge.classList.add("d-none");
                }
            });
            bulkClearSelection();
            showAjaxStatus("Assigned " + result.assignedCount + " item(s) to Box " + result.boxIdentifier + ".", true);
        } catch (e) {
            showAjaxStatus("Bulk assign failed.", false);
            console.error(e);
        } finally {
            bulkAssignBoxBtn.disabled = false;
        }
    }

    async function bulkRemoveLocation() {
        var itemIds = getCheckedItemIds();
        if (itemIds.length === 0) return;

        var confirmed = confirm(
            "Remove location for " + itemIds.length + " item" + (itemIds.length === 1 ? "" : "s") +
            "? They will show as having no storage location.");
        if (!confirmed) return;

        bulkRemoveLocationBtn.disabled = true;
        try {
            var response = await fetch(appUrl("/Inventory/BulkRemoveLocation"), {
                method: "POST",
                headers: {
                    "Content-Type": "application/json",
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": getBulkAntiForgeryToken()
                },
                body: JSON.stringify({ ItemIds: itemIds })
            });
            var result = await response.json();
            if (!result.success) {
                showAjaxStatus(result.message || "Bulk remove failed.", false);
                return;
            }
            var isMissing = inventoryMode === "missing";
            itemIds.forEach(function (id) {
                var cb = document.querySelector('.bulk-item-checkbox[data-item-id="' + id + '"]');
                var row = cb ? cb.closest(".inventory-list-item") : null;
                if (!row) return;
                if (isMissing) {
                    return; // already has no location, nothing to update on this page
                }
                var shelfBadge = row.querySelector(".inventory-shelf-badge");
                var boxBadge = row.querySelector(".inventory-box-badge");
                var boxLocBadge = row.querySelector(".inventory-box-location-badge");
                if (shelfBadge) shelfBadge.classList.add("d-none");
                if (boxBadge) boxBadge.classList.add("d-none");
                if (boxLocBadge) boxLocBadge.classList.add("d-none");
            });
            bulkClearSelection();
            showAjaxStatus("Removed location for " + itemIds.length + " item(s).", true);
        } catch (e) {
            showAjaxStatus("Bulk remove failed.", false);
            console.error(e);
        } finally {
            bulkRemoveLocationBtn.disabled = false;
        }
    }

    function bulkClearSelection() {
        document.querySelectorAll(".bulk-item-checkbox:checked").forEach(function (cb) { cb.checked = false; });
        if (bulkSelectAllCheckbox) bulkSelectAllCheckbox.checked = false;
        updateBulkSelectionUI();
    }

    if (bulkAssignShelfBtn) bulkAssignShelfBtn.addEventListener("click", bulkAssignShelf);
    if (bulkAssignBoxBtn) bulkAssignBoxBtn.addEventListener("click", bulkAssignBox);
    if (bulkRemoveLocationBtn) bulkRemoveLocationBtn.addEventListener("click", bulkRemoveLocation);
    if (bulkCancelBtn) bulkCancelBtn.addEventListener("click", bulkClearSelection);

    populateBulkDropdowns();

    // Patch applyInventorySearchVisibility to update bulk UI after filtering
    var _origApplySearch = applyInventorySearchVisibility;
    applyInventorySearchVisibility = function () {
        _origApplySearch();
        updateBulkSelectionUI();
    };

    // ─── End Bulk Selection Logic ────────────────────────────────────────

    initializeInventoryRowControls(document);
    applyInventorySearchVisibility();
    setTimeout(tryLoadMoreAtBottom, 100);

    async function triggerEbaySync() {
        const syncIndicator = document.getElementById("syncIndicator");
        if (syncIndicator) syncIndicator.classList.remove("d-none");
        try {
            const response = await fetch(appUrl("/Inventory/TriggerSync"), {
                headers: { "X-Requested-With": "XMLHttpRequest" }
            });
            const result = await response.json();
            if (result.ok) {
                if (inventoryList) {
                    inventoryList.innerHTML = "";
                    inventoryCursor = 1;
                    inventoryHasMore = true;
                    inventoryLoadingMore = false;
                    await loadMoreInventory(true);
                } else {
                    window.location.reload();
                }
            }
        } catch (e) {
            // silent — stale DB data is still usable
        } finally {
            if (syncIndicator) syncIndicator.classList.add("d-none");
        }
    }

    if (inventoryIsConnected) {
        triggerEbaySync();
    }
}());
