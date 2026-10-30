// Shared margin calculator for Owned items' price editors — mirrors
// Services/OwnershipRules.cs::ComputeMargin exactly (>=25% green, 0-25% yellow, <=0% red).
// Used by both Views/Inventory/Details.cshtml and needs-attention.js so the two surfaces can't
// disagree on thresholds. Keep in sync with the C# version if these thresholds ever change.
window.InventoryMargin = (function () {
    "use strict";

    function compute(salePrice, cost) {
        if (cost === null || cost === undefined || isNaN(cost) || !salePrice || salePrice <= 0) {
            return null;
        }
        const margin = ((salePrice - cost) / salePrice) * 100;
        let colorClass;
        if (margin >= 25) {
            colorClass = "text-success";
        } else if (margin > 0) {
            colorClass = "text-warning";
        } else {
            colorClass = "text-danger";
        }
        return { marginPercent: margin, colorClass: colorClass };
    }

    // Updates a readout element's text/color from a price input + a data-purchase-cost
    // attribute on the given container. Returns the computed margin (or null) for callers that
    // need to gate a confirm() warning on it.
    function updateReadout(container, priceInput, readoutEl) {
        if (!container || !priceInput || !readoutEl) return null;
        const costAttr = container.getAttribute("data-purchase-cost");
        if (costAttr === null || costAttr === "") {
            readoutEl.textContent = "";
            return null;
        }
        const cost = parseFloat(costAttr);
        const price = parseFloat(priceInput.value);
        const result = compute(price, cost);
        if (!result) {
            readoutEl.textContent = "";
            return null;
        }
        readoutEl.textContent = "Margin: " + result.marginPercent.toFixed(1) + "%";
        readoutEl.className = readoutEl.className.replace(/\btext-(success|warning|danger)\b/g, "").trim();
        readoutEl.classList.add(result.colorClass);
        return result;
    }

    return { compute: compute, updateReadout: updateReadout };
})();
