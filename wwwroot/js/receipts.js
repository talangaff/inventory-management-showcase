// Receipt Details page: header edit, add/edit/remove line items, and the assign workflow
// (live search, quick-pick from recently-listed uncosted items, and scan-a-QR-then-Enter for
// barcode-scanner input). All state comes back from the server on every action (items +
// quickPickItems) and the item list / quick-pick strips are fully re-rendered from that state,
// so the client never has to reconcile partial updates. Mirrors the antiforgery/fetch
// conventions in needs-attention.js (form-encoded POST + RequestVerificationToken header).
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        const state = window.receiptState;
        if (!state) return;

        const antiForgeryForm = document.getElementById("receiptAntiForgeryForm");
        const alertRegion = document.getElementById("alertRegion");
        const itemsContainer = document.getElementById("itemsContainer");
        const emptyItemsState = document.getElementById("emptyItemsState");
        const itemCountLabel = document.getElementById("itemCountLabel");
        const totalSummary = document.getElementById("totalSummary");

        function getAntiForgeryToken() {
            if (!antiForgeryForm) return "";
            const input = antiForgeryForm.querySelector("input[name='__RequestVerificationToken']");
            return input ? input.value : "";
        }

        function showAlert(message, ok) {
            if (!alertRegion) return;
            alertRegion.textContent = message;
            alertRegion.classList.remove("d-none", "alert-success", "alert-danger");
            alertRegion.classList.add(ok ? "alert-success" : "alert-danger");
        }

        async function postForm(url, params) {
            const response = await fetch(url, {
                method: "POST",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded",
                    "X-Requested-With": "XMLHttpRequest",
                    "RequestVerificationToken": getAntiForgeryToken()
                },
                body: new URLSearchParams(params).toString()
            });
            return response.json();
        }

        function money(n) {
            return "$" + Number(n || 0).toFixed(2);
        }

        function escapeHtml(s) {
            const div = document.createElement("div");
            div.textContent = s || "";
            return div.innerHTML;
        }

        function applyResult(result) {
            if (!result || result.success !== true) {
                showAlert((result && result.message) || "Something went wrong.", false);
                return;
            }
            state.items = result.items || [];
            state.quickPickItems = result.quickPickItems || [];
            state.unassignedItems = result.unassignedItems || [];
            render();
        }

        function renderQuickPickChips(receiptItemId) {
            if (!state.quickPickItems.length) {
                return '<span class="text-muted small">No recently-listed uncosted items right now.</span>';
            }
            return state.quickPickItems.slice(0, 6).map(function (q) {
                return '<button type="button" class="btn btn-sm btn-outline-secondary quick-pick-chip" ' +
                    'data-receipt-item-id="' + receiptItemId + '" data-listing-id="' + q.listingId + '">' +
                    escapeHtml(q.title) + ' <span class="text-muted">(' + escapeHtml(q.itemId) + ')</span></button>';
            }).join(" ");
        }

        function renderAssignedRegion(item) {
            if (item.assignedListingId) {
                return '' +
                    '<label class="form-label small mb-1">Assigned inventory item</label>' +
                    '<div class="d-flex align-items-center gap-2 p-2 border rounded bg-light">' +
                    (item.assignedCoverPhotoUrl
                        ? '<img src="' + escapeHtml(item.assignedCoverPhotoUrl) + '" alt="" style="width:56px;height:56px;object-fit:cover;border-radius:4px;flex-shrink:0;" />'
                        : '<div style="width:56px;height:56px;background:var(--clr-surface-2);border-radius:4px;flex-shrink:0;" class="d-flex align-items-center justify-content-center"><span class="text-muted small">No img</span></div>') +
                    '<div style="min-width:0;flex:1 1 auto;">' +
                    '<a href="' + appUrl("/Inventory/Details/") + encodeURIComponent(item.assignedItemId) + '" class="fw-semibold text-decoration-none text-dark" style="word-break:break-word;">' + escapeHtml(item.assignedTitle) + '</a>' +
                    '<div class="small text-muted">' + escapeHtml(item.assignedItemId) + ' &middot; ' + escapeHtml(item.assignedBarcode) + '</div>' +
                    '</div>' +
                    '<button type="button" class="btn btn-sm btn-outline-secondary ms-auto flex-shrink-0 item-unassign-btn" data-receipt-item-id="' + item.id + '">Unassign</button>' +
                    '</div>';
            }

            return '' +
                '<label class="form-label small mb-1">Assign to inventory item</label>' +
                '<input type="text" class="form-control form-control-sm item-search-input" data-receipt-item-id="' + item.id + '" placeholder="Search name/ID, or scan QR + Enter" autocomplete="off" />' +
                '<div class="list-group item-search-results mt-1" data-receipt-item-id="' + item.id + '"></div>' +
                '<div class="small text-muted mt-2 mb-1">Quick pick — recently listed, no cost yet:</div>' +
                '<div class="d-flex flex-wrap gap-1 item-quick-pick">' + renderQuickPickChips(item.id) + '</div>';
        }

        function renderItemCard(item) {
            return '' +
                '<div class="card border-0 shadow-sm receipt-item-card" data-item-id="' + item.id + '">' +
                '<div class="card-body d-flex flex-wrap gap-3 align-items-start">' +
                '<div style="min-width:220px;flex:1 1 220px;">' +
                '<label class="form-label small mb-1">Name (optional)</label>' +
                '<input type="text" class="form-control form-control-sm item-name-input" value="' + escapeHtml(item.name || "") + '" placeholder="Unnamed item" />' +
                '<div class="d-flex gap-2 mt-2 flex-wrap">' +
                '<div><label class="form-label small mb-1">Pre-tax price</label>' +
                '<div class="input-group input-group-sm" style="max-width:130px;"><span class="input-group-text">$</span>' +
                '<input type="number" step="0.01" min="0" class="form-control item-price-input" value="' + Number(item.preTaxPrice).toFixed(2) + '" /></div></div>' +
                '<div><label class="form-label small mb-1">Allocated (incl. tax)</label>' +
                '<div class="form-control form-control-sm bg-light" style="max-width:130px;">' + money(item.allocatedTotalPrice) + '</div></div>' +
                '</div>' +
                '<div class="d-flex gap-2 mt-2">' +
                '<button type="button" class="btn btn-sm btn-outline-primary item-save-btn" data-receipt-item-id="' + item.id + '">Save</button>' +
                '<button type="button" class="btn btn-sm btn-outline-danger item-remove-btn" data-receipt-item-id="' + item.id + '">Remove</button>' +
                '</div>' +
                '</div>' +
                '<div style="min-width:260px;flex:1 1 260px;" class="item-assign-region">' + renderAssignedRegion(item) + '</div>' +
                '</div>' +
                '</div>';
        }

        // --- "Items" strip: every listing with no receipt yet, newest first. Clicking one
        // picks it for the next added line, which the server then assigns in the same call.
        const pickStrip = document.getElementById("pickStrip");
        const pickSearch = document.getElementById("pickSearch");
        const pickCount = document.getElementById("pickCount");
        const addItemPicked = document.getElementById("addItemPicked");
        const addItemCard = document.getElementById("addItemCard");
        let pickedListingId = null;
        let autoName = "";

        function pickMatches(q, terms) {
            const hay = ((q.title || "") + " " + (q.itemId || "") + " " + (q.barcode || "")).toLowerCase();
            return terms.every(function (t) { return hay.indexOf(t) !== -1; });
        }

        function renderPickStrip() {
            if (!pickStrip) return;
            const all = state.unassignedItems || [];
            if (pickedListingId && !all.some(function (q) { return q.listingId === pickedListingId; })) {
                setPicked(null);
            }
            const terms = (pickSearch ? pickSearch.value : "").toLowerCase().split(/\s+/).filter(Boolean);
            const shown = all.filter(function (q) { return pickMatches(q, terms); });
            if (pickCount) pickCount.textContent = all.length + " without a receipt";
            if (!all.length) {
                pickStrip.innerHTML = '<p class="text-muted small mb-0">Every listed item has a receipt (or is consignment).</p>';
                return;
            }
            if (!shown.length) {
                pickStrip.innerHTML = '<p class="text-muted small mb-0 pick-nomatch">No items match.</p>';
                return;
            }
            pickStrip.innerHTML = shown.map(function (q) {
                const selected = q.listingId === pickedListingId;
                return '<button type="button" class="pick-card" role="option" aria-selected="' + selected + '" data-listing-id="' + q.listingId + '">' +
                    (q.coverPhotoUrl
                        ? '<img src="' + escapeHtml(q.coverPhotoUrl) + '" alt="" loading="lazy" width="96" height="96" />'
                        : '<span class="pick-noimg" aria-hidden="true"></span>') +
                    '<span class="pick-title">' + escapeHtml(q.title) + '</span>' +
                    '<span class="pick-id font-monospace">' + escapeHtml(q.itemId) + '</span>' +
                    '</button>';
            }).join("");
        }

        function setPicked(q) {
            const nameInput = document.getElementById("newItemName");
            pickedListingId = q ? q.listingId : null;
            if (nameInput && (nameInput.value === "" || nameInput.value === autoName)) {
                autoName = q ? (q.title || "") : "";
                nameInput.value = autoName;
            }
            if (addItemPicked) {
                addItemPicked.textContent = q ? "→ " + q.title : "";
                addItemPicked.classList.toggle("d-none", !q);
            }
            if (addItemCard) addItemCard.classList.toggle("is-picked", !!q);
        }

        if (pickSearch) pickSearch.addEventListener("input", renderPickStrip);
        if (pickStrip) pickStrip.addEventListener("click", function (e) {
            const card = e.target.closest(".pick-card");
            if (!card) return;
            const id = card.dataset.listingId;
            const q = (state.unassignedItems || []).find(function (x) { return x.listingId === id; });
            setPicked(pickedListingId === id ? null : q);
            renderPickStrip();
            if (pickedListingId) {
                const priceInput = document.getElementById("newItemPrice");
                if (priceInput) priceInput.focus();
            }
        });

        function render() {
            renderPickStrip();
            itemsContainer.innerHTML = state.items.map(renderItemCard).join("");
            const count = state.items.length;
            itemCountLabel.textContent = count + " item" + (count !== 1 ? "s" : "");
            emptyItemsState.classList.toggle("d-none", count !== 0);
            itemsContainer.classList.toggle("d-none", count === 0);
        }

        // --- Header ---
        const saveHeaderBtn = document.getElementById("saveHeaderBtn");
        if (saveHeaderBtn) {
            saveHeaderBtn.addEventListener("click", async function () {
                const receiptDate = document.getElementById("headerDate").value;
                const totalPrice = document.getElementById("headerTotal").value;
                const result = await postForm(appUrl("/Receipts/UpdateHeader"), {
                    id: state.receiptId,
                    receiptDate: receiptDate,
                    totalPrice: totalPrice
                });
                if (result.success) {
                    totalSummary.textContent = "Total: " + money(totalPrice) + " (incl. tax)";
                    showAlert("Receipt updated.", true);
                }
                applyResult(result);
            });
        }

        // --- Add item ---
        const addItemBtn = document.getElementById("addItemBtn");
        if (addItemBtn) {
            addItemBtn.addEventListener("click", async function () {
                const priceInput = document.getElementById("newItemPrice");
                const nameInput = document.getElementById("newItemName");
                if (!priceInput.value) {
                    showAlert("Enter a pre-tax price first.", false);
                    return;
                }
                const params = {
                    receiptId: state.receiptId,
                    preTaxPrice: priceInput.value,
                    name: nameInput.value
                };
                if (pickedListingId) params.listingId = pickedListingId;
                const result = await postForm(appUrl("/Receipts/AddItem"), params);
                if (result.success) {
                    priceInput.value = "";
                    nameInput.value = "";
                    autoName = "";
                    setPicked(null);
                }
                applyResult(result);
            });
        }

        // --- Delegated events across the items container ---
        itemsContainer.addEventListener("click", async function (e) {
            const saveBtn = e.target.closest(".item-save-btn");
            if (saveBtn) {
                const card = saveBtn.closest(".receipt-item-card");
                const price = card.querySelector(".item-price-input").value;
                const name = card.querySelector(".item-name-input").value;
                if (!price) {
                    showAlert("Price can't be blank.", false);
                    return;
                }
                const result = await postForm(appUrl("/Receipts/UpdateItem"), {
                    itemId: saveBtn.dataset.receiptItemId,
                    preTaxPrice: price,
                    name: name
                });
                applyResult(result);
                return;
            }

            const removeBtn = e.target.closest(".item-remove-btn");
            if (removeBtn) {
                if (!confirm("Remove this item from the receipt? If it's assigned, that inventory item's cost/ownership classification will be cleared too.")) return;
                const result = await postForm(appUrl("/Receipts/RemoveItem"), { itemId: removeBtn.dataset.receiptItemId });
                applyResult(result);
                return;
            }

            const unassignBtn = e.target.closest(".item-unassign-btn");
            if (unassignBtn) {
                if (!confirm("Unassign this item? The inventory item's cost/ownership classification will be cleared too.")) return;
                const result = await postForm(appUrl("/Receipts/UnassignItem"), { receiptItemId: unassignBtn.dataset.receiptItemId });
                applyResult(result);
                return;
            }

            const quickPickChip = e.target.closest(".quick-pick-chip");
            if (quickPickChip) {
                const result = await postForm(appUrl("/Receipts/AssignItem"), {
                    receiptItemId: quickPickChip.dataset.receiptItemId,
                    listingId: quickPickChip.dataset.listingId
                });
                applyResult(result);
                return;
            }

            const searchResult = e.target.closest(".item-search-result");
            if (searchResult) {
                const result = await postForm(appUrl("/Receipts/AssignItem"), {
                    receiptItemId: searchResult.dataset.receiptItemId,
                    listingId: searchResult.dataset.listingId
                });
                applyResult(result);
                return;
            }
        });

        let searchDebounce = null;
        itemsContainer.addEventListener("input", function (e) {
            const input = e.target.closest(".item-search-input");
            if (!input) return;

            clearTimeout(searchDebounce);
            const receiptItemId = input.dataset.receiptItemId;
            const resultsBox = itemsContainer.querySelector('.item-search-results[data-receipt-item-id="' + receiptItemId + '"]');
            const term = input.value.trim();

            if (!term) {
                if (resultsBox) resultsBox.innerHTML = "";
                return;
            }

            searchDebounce = setTimeout(async function () {
                const response = await fetch(appUrl("/Receipts/SearchItems?q=") + encodeURIComponent(term));
                const results = await response.json();
                if (!resultsBox) return;
                if (!results.length) {
                    resultsBox.innerHTML = '<div class="list-group-item small text-muted">No matches</div>';
                    return;
                }
                resultsBox.innerHTML = results.map(function (r) {
                    return '<button type="button" class="list-group-item list-group-item-action item-search-result" ' +
                        'data-receipt-item-id="' + receiptItemId + '" data-listing-id="' + r.listingId + '">' +
                        escapeHtml(r.title) + ' <span class="text-muted small">(' + escapeHtml(r.itemId) + ')</span>' +
                        (r.alreadyOwned ? ' <span class="badge text-bg-secondary">has a cost</span>' : '') +
                        '</button>';
                }).join("");
            }, 250);
        });

        itemsContainer.addEventListener("keydown", async function (e) {
            const input = e.target.closest(".item-search-input");
            if (!input || e.key !== "Enter") return;
            e.preventDefault();

            const receiptItemId = input.dataset.receiptItemId;
            const scanned = input.value.trim();
            if (!scanned) return;

            // Scanner input: a QR/barcode value entered and Enter pressed should assign
            // instantly on an exact match, without waiting on the debounced search results.
            const response = await fetch(appUrl("/Receipts/ResolveScan?barcode=") + encodeURIComponent(scanned));
            const scanResult = await response.json();
            if (scanResult.found) {
                const result = await postForm(appUrl("/Receipts/AssignItem"), {
                    receiptItemId: receiptItemId,
                    listingId: scanResult.item.listingId
                });
                applyResult(result);
                return;
            }

            const resultsBox = itemsContainer.querySelector('.item-search-results[data-receipt-item-id="' + receiptItemId + '"]');
            const singleResult = resultsBox ? resultsBox.querySelector(".item-search-result") : null;
            const onlyOneVisible = resultsBox && resultsBox.querySelectorAll(".item-search-result").length === 1;
            if (singleResult && onlyOneVisible) {
                const result = await postForm(appUrl("/Receipts/AssignItem"), {
                    receiptItemId: receiptItemId,
                    listingId: singleResult.dataset.listingId
                });
                applyResult(result);
                return;
            }

            showAlert("No exact barcode match — refine the search or pick a result below.", false);
        });

        render();
    });
})();
