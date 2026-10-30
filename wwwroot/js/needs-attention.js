// Needs Attention page: quick price update, ignore-for-30-days, ignore-permanently (stale
// items), relist-from-backstock (classic form, no JS needed — full page reload), and
// which-unit-sold resolution. Mirrors the antiforgery/fetch conventions in
// bulk-item-actions.js, but posts form-encoded bodies (not JSON) to match
// InventoryController.UpdatePrice/IgnoreAttention's existing scalar-parameter signatures,
// which the item Details page's classic <form> also uses; MarkUnitSold is JSON, matching
// Orders/MarkUnitSold and the Details-page picker.
(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        const antiForgeryForm = document.getElementById("attentionAntiForgeryForm");
        const ajaxStatus = document.getElementById("attentionAjaxStatus");
        const cardList = document.getElementById("attentionCardList");
        const emptyState = document.getElementById("attentionEmptyState");
        const pageCount = document.getElementById("attentionPageCount");
        const ignoreAllBtn = document.getElementById("attentionIgnoreAllBtn");
        const unitResolutionCardList = document.getElementById("unitResolutionCardList");
        const unitResolutionSection = document.getElementById("unitResolutionSection");
        const unclassifiedCardList = document.getElementById("unclassifiedCardList");
        const unclassifiedSection = document.getElementById("unclassifiedSection");
        const missingShippingCostCardList = document.getElementById("missingShippingCostCardList");
        const missingShippingCostSection = document.getElementById("missingShippingCostSection");

        function getAntiForgeryToken() {
            if (!antiForgeryForm) return "";
            const input = antiForgeryForm.querySelector("input[name='__RequestVerificationToken']");
            return input ? input.value : "";
        }

        function showStatus(message, ok) {
            if (!ajaxStatus) return;
            ajaxStatus.textContent = message;
            ajaxStatus.classList.remove("d-none", "alert-success", "alert-danger");
            ajaxStatus.classList.add(ok ? "alert-success" : "alert-danger");
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

        // Single source of truth for the header badge/count and the empty state, counting
        // every card tagged .attention-item regardless of which section (or any future
        // section) it belongs to — so a new Needs Attention category only has to tag its
        // cards with this class to be counted correctly, with no further JS changes needed.
        function recomputeAttentionCount() {
            const total = document.querySelectorAll(".attention-item").length;
            if (pageCount) {
                pageCount.textContent = total + " item" + (total === 1 ? "" : "s");
            }
            if (total === 0) {
                if (emptyState) emptyState.classList.remove("d-none");
            } else {
                if (emptyState) emptyState.classList.add("d-none");
            }
            if (ignoreAllBtn) {
                const staleRemaining = cardList ? cardList.querySelectorAll(".attention-card").length : 0;
                ignoreAllBtn.classList.toggle("d-none", staleRemaining === 0);
            }
        }

        function removeCard(card) {
            card.remove();
            recomputeAttentionCount();
        }

        if (ignoreAllBtn && cardList) {
            ignoreAllBtn.addEventListener("click", async function () {
                const total = cardList.querySelectorAll(".attention-card").length;
                if (total === 0) return;
                const confirmed = confirm(
                    "Ignore all " + total + " item" + (total === 1 ? "" : "s") +
                    " on this page for 30 days?");
                if (!confirmed) return;

                ignoreAllBtn.disabled = true;
                try {
                    const result = await postForm(appUrl("/Inventory/IgnoreAllAttention"), {});
                    if (!result.success) {
                        showStatus(result.message || "Failed to ignore items.", false);
                        return;
                    }
                    showStatus("Ignored " + result.count + " item(s) for 30 days.", true);
                    cardList.querySelectorAll(".attention-card").forEach(function (card) { card.remove(); });
                    recomputeAttentionCount();
                } catch (err) {
                    showStatus("Failed to ignore items.", false);
                    console.error(err);
                } finally {
                    ignoreAllBtn.disabled = false;
                }
            });
        }

        // Live margin readout for Owned items (see margin-calculator.js) — only forms carrying
        // a data-purchase-cost attribute have a readout element to update.
        document.querySelectorAll(".attention-price-form").forEach(function (form) {
            const priceInput = form.querySelector(".attention-price-input");
            const readout = form.querySelector(".attention-margin-readout");
            if (!priceInput || !readout) return;
            const update = function () { window.InventoryMargin.updateReadout(form, priceInput, readout); };
            priceInput.addEventListener("input", update);
            update();
        });

        if (cardList) {
            cardList.addEventListener("click", async function (e) {
                const target = e.target;
                if (!(target instanceof Element)) return;

                const card = target.closest(".attention-card");
                if (!card) return;
                const itemId = card.dataset.itemId;

                const updateBtn = target.closest(".attention-update-price-btn");
                if (updateBtn) {
                    const input = card.querySelector(".attention-price-input");
                    const newPrice = input ? parseFloat(input.value) : NaN;
                    if (!newPrice || newPrice <= 0) {
                        showStatus("Enter a valid price.", false);
                        return;
                    }

                    const priceForm = updateBtn.closest(".attention-price-form");
                    const costAttr = priceForm ? priceForm.getAttribute("data-purchase-cost") : null;
                    if (costAttr !== null && costAttr !== "") {
                        const margin = window.InventoryMargin.compute(newPrice, parseFloat(costAttr));
                        if (margin && margin.marginPercent <= 0) {
                            const confirmed = confirm(
                                "This price gives a margin of " + margin.marginPercent.toFixed(1) +
                                "% (at or below your cost). Save it anyway?");
                            if (!confirmed) return;
                        }
                    }

                    updateBtn.disabled = true;
                    try {
                        const result = await postForm(appUrl("/Inventory/UpdatePrice"), { itemId, newPrice });
                        if (!result.success) {
                            showStatus(result.message || "Price update failed.", false);
                            return;
                        }
                        showStatus("Price updated to " + newPrice.toFixed(2) + " on eBay.", true);
                        removeCard(card);
                    } catch (err) {
                        showStatus("Price update failed.", false);
                        console.error(err);
                    } finally {
                        updateBtn.disabled = false;
                    }
                    return;
                }

                const ignore30Btn = target.closest(".attention-ignore-30-btn");
                if (ignore30Btn) {
                    ignore30Btn.disabled = true;
                    try {
                        const result = await postForm(appUrl("/Inventory/IgnoreAttention"), { itemId, permanent: false });
                        if (!result.success) {
                            showStatus(result.message || "Failed to ignore item.", false);
                            return;
                        }
                        showStatus("Ignored for 30 days.", true);
                        removeCard(card);
                    } catch (err) {
                        showStatus("Failed to ignore item.", false);
                        console.error(err);
                    } finally {
                        ignore30Btn.disabled = false;
                    }
                    return;
                }

                const ignorePermanentBtn = target.closest(".attention-ignore-permanent-btn");
                if (ignorePermanentBtn) {
                    const confirmed = confirm("Ignore this item permanently? You can undo this later from the item's Details page.");
                    if (!confirmed) return;
                    ignorePermanentBtn.disabled = true;
                    try {
                        const result = await postForm(appUrl("/Inventory/IgnoreAttention"), { itemId, permanent: true });
                        if (!result.success) {
                            showStatus(result.message || "Failed to ignore item.", false);
                            return;
                        }
                        showStatus("Ignored permanently.", true);
                        removeCard(card);
                    } catch (err) {
                        showStatus("Failed to ignore item.", false);
                        console.error(err);
                    } finally {
                        ignorePermanentBtn.disabled = false;
                    }
                }
            });
        }

        // ── "Needs unit resolved" section: pick which unit a pending sale shipped ──
        if (unitResolutionCardList) {
            unitResolutionCardList.addEventListener("click", async function (e) {
                const target = e.target;
                if (!(target instanceof Element)) return;

                const confirmBtn = target.closest(".unit-resolution-confirm-btn");
                if (!confirmBtn) return;

                const card = confirmBtn.closest(".unit-resolution-card");
                const select = card ? card.querySelector(".unit-resolution-select") : null;
                const itemId = card ? card.dataset.itemId : "";
                const unitId = select ? select.value : "";
                if (!itemId || !unitId) return;

                confirmBtn.disabled = true;
                try {
                    const response = await fetch(appUrl("/Inventory/MarkUnitSold"), {
                        method: "POST",
                        headers: {
                            "Content-Type": "application/json",
                            "X-Requested-With": "XMLHttpRequest",
                            "RequestVerificationToken": getAntiForgeryToken()
                        },
                        body: JSON.stringify({ itemId: itemId, unitId: unitId })
                    });
                    const result = await response.json();
                    if (!result.success) {
                        showStatus(result.message || "Couldn't save that.", false);
                        return;
                    }
                    showStatus("Unit marked sold.", true);
                    card.remove();
                    if (unitResolutionSection && unitResolutionCardList.querySelectorAll(".unit-resolution-card").length === 0) {
                        unitResolutionSection.remove();
                    }
                    recomputeAttentionCount();
                } catch (err) {
                    showStatus("Couldn't save that.", false);
                    console.error(err);
                } finally {
                    confirmBtn.disabled = false;
                }
            });
        }

        // ── "Needs classification" section: mark Owned (with cost) or Consignment ──
        function removeUnclassifiedCard(card) {
            card.remove();
            if (unclassifiedSection && unclassifiedCardList && unclassifiedCardList.querySelectorAll(".unclassified-card").length === 0) {
                unclassifiedSection.remove();
            }
            recomputeAttentionCount();
        }

        // Shared by the Save button click and pressing Enter in the cost box. An empty box
        // (including pressing Enter without typing anything) assumes $0 — a legitimate purchase
        // cost (free item, or already owned) — rather than blocking with an error.
        async function saveOwnedClassification(card, saveBtn) {
            const itemId = card.dataset.itemId;
            const costInput = card.querySelector(".unclassified-cost-input");
            const raw = costInput ? costInput.value.trim() : "";
            const purchaseCost = raw === "" ? 0 : parseFloat(raw);
            if (isNaN(purchaseCost) || purchaseCost < 0) {
                showStatus("Enter a valid purchase cost.", false);
                return;
            }
            saveBtn.disabled = true;
            try {
                const result = await postForm(appUrl("/Inventory/ClassifyOwned"), { itemId, purchaseCost });
                if (!result.success) {
                    showStatus(result.message || "Couldn't save classification.", false);
                    return;
                }
                showStatus("Marked as Owned.", true);
                removeUnclassifiedCard(card);
            } catch (err) {
                showStatus("Couldn't save classification.", false);
                console.error(err);
            } finally {
                saveBtn.disabled = false;
            }
        }

        if (unclassifiedCardList) {
            unclassifiedCardList.addEventListener("keydown", function (e) {
                if (e.key !== "Enter") return;
                const target = e.target;
                if (!(target instanceof Element) || !target.classList.contains("unclassified-cost-input")) return;
                e.preventDefault();
                const card = target.closest(".unclassified-card");
                const saveBtn = card ? card.querySelector(".unclassified-owned-save-btn") : null;
                if (card && saveBtn) {
                    saveOwnedClassification(card, saveBtn);
                }
            });

            unclassifiedCardList.addEventListener("click", async function (e) {
                const target = e.target;
                if (!(target instanceof Element)) return;

                const card = target.closest(".unclassified-card");
                if (!card) return;
                const itemId = card.dataset.itemId;

                if (target.closest(".unclassified-owned-toggle-btn")) {
                    card.querySelector(".unclassified-owned-form")?.classList.remove("d-none");
                    card.querySelector(".unclassified-consignment-form")?.classList.add("d-none");
                    return;
                }

                if (target.closest(".unclassified-consignment-toggle-btn")) {
                    card.querySelector(".unclassified-consignment-form")?.classList.remove("d-none");
                    card.querySelector(".unclassified-owned-form")?.classList.add("d-none");
                    return;
                }

                const ownedSaveBtn = target.closest(".unclassified-owned-save-btn");
                if (ownedSaveBtn) {
                    await saveOwnedClassification(card, ownedSaveBtn);
                    return;
                }

                const consignmentSaveBtn = target.closest(".unclassified-consignment-save-btn");
                if (consignmentSaveBtn) {
                    const select = card.querySelector(".unclassified-consignor-select");
                    const consignorId = select ? select.value : "";
                    if (!consignorId) {
                        showStatus("Choose a consignor.", false);
                        return;
                    }
                    consignmentSaveBtn.disabled = true;
                    try {
                        const result = await postForm(appUrl("/Inventory/ClassifyConsignment"), { itemId, consignorId });
                        if (!result.success) {
                            showStatus(result.message || "Couldn't save classification.", false);
                            return;
                        }
                        showStatus("Marked as Consignment.", true);
                        removeUnclassifiedCard(card);
                    } catch (err) {
                        showStatus("Couldn't save classification.", false);
                        console.error(err);
                    } finally {
                        consignmentSaveBtn.disabled = false;
                    }
                    return;
                }

                const quickConsignorBtn = target.closest(".unclassified-quick-consignor-btn");
                if (quickConsignorBtn) {
                    const consignorId = quickConsignorBtn.dataset.consignorId;
                    if (!consignorId) return;
                    quickConsignorBtn.disabled = true;
                    try {
                        const result = await postForm(appUrl("/Inventory/ClassifyConsignment"), { itemId, consignorId });
                        if (!result.success) {
                            showStatus(result.message || "Couldn't save classification.", false);
                            return;
                        }
                        showStatus("Marked as Consignment.", true);
                        removeUnclassifiedCard(card);
                    } catch (err) {
                        showStatus("Couldn't save classification.", false);
                        console.error(err);
                    } finally {
                        quickConsignorBtn.disabled = false;
                    }
                }
            });
        }

        // ── "Missing shipping cost" section: manual entry when the Finance API hasn't
        // returned an actual shipping cost for a shipped order yet ──
        if (missingShippingCostCardList) {
            missingShippingCostCardList.addEventListener("keydown", function (e) {
                if (e.key !== "Enter") return;
                const target = e.target;
                if (!(target instanceof Element) || !target.classList.contains("missing-shipping-cost-input")) return;
                e.preventDefault();
                const card = target.closest(".missing-shipping-cost-card");
                const saveBtn = card ? card.querySelector(".missing-shipping-cost-save-btn") : null;
                if (card && saveBtn) {
                    saveShippingCost(card, saveBtn);
                }
            });

            missingShippingCostCardList.addEventListener("click", async function (e) {
                const target = e.target;
                if (!(target instanceof Element)) return;
                const saveBtn = target.closest(".missing-shipping-cost-save-btn");
                if (!saveBtn) return;
                const card = saveBtn.closest(".missing-shipping-cost-card");
                if (card) {
                    await saveShippingCost(card, saveBtn);
                }
            });
        }

        async function saveShippingCost(card, saveBtn) {
            const orderId = card.dataset.orderId;
            const costInput = card.querySelector(".missing-shipping-cost-input");
            const raw = costInput ? costInput.value.trim() : "";
            const shippingLabelCost = parseFloat(raw);
            if (raw === "" || isNaN(shippingLabelCost) || shippingLabelCost < 0) {
                showStatus("Enter a valid shipping cost.", false);
                return;
            }
            saveBtn.disabled = true;
            try {
                const result = await postForm(appUrl("/Inventory/SetShippingCost"), { orderId, shippingLabelCost });
                if (!result.success) {
                    showStatus(result.message || "Couldn't save shipping cost.", false);
                    return;
                }
                showStatus("Shipping cost saved.", true);
                card.remove();
                if (missingShippingCostSection && missingShippingCostCardList &&
                        missingShippingCostCardList.querySelectorAll(".missing-shipping-cost-card").length === 0) {
                    missingShippingCostSection.remove();
                }
                recomputeAttentionCount();
            } catch (err) {
                showStatus("Couldn't save shipping cost.", false);
                console.error(err);
            } finally {
                saveBtn.disabled = false;
            }
        }
    });
})();
