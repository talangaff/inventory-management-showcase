(function () {
    function openBarcodePrintWindow(barcodeValue, label) {
        if (!window.InventoryBarcodePrint) {
            return;
        }

        window.InventoryBarcodePrint.openBarcodePrintWindow(barcodeValue, label);
    }

    document.querySelectorAll(".shelf-print-barcode-btn").forEach(function (button) {
        button.addEventListener("click", function () {
            openBarcodePrintWindow(button.dataset.barcode || "", button.dataset.label || "");
        });
    });

    document.querySelectorAll(".shelf-slot-print-barcode-btn").forEach(function (button) {
        button.addEventListener("click", function () {
            openBarcodePrintWindow(button.dataset.barcode || "", button.dataset.label || "");
        });
    });

    document.querySelectorAll(".shelf-print-all-btn").forEach(function (button) {
        button.addEventListener("click", function () {
            if (!window.InventoryBarcodePrint) return;
            var shelves = [];
            try { shelves = JSON.parse(button.dataset.shelves || "[]"); } catch (e) { return; }
            window.InventoryBarcodePrint.openBulkBarcodePrintWindow(shelves);
        });
    });

    var shelfSearchInput = document.getElementById("shelfSearchInput");
    var shelfRows = Array.from(document.querySelectorAll(".shelf-row"));

    if (shelfSearchInput) {
        shelfSearchInput.addEventListener("input", function () {
            var term = shelfSearchInput.value.trim().toLowerCase();
            shelfRows.forEach(function (row) {
                var searchable = row.dataset.search || "";
                var matches = !term || searchable.includes(term);
                row.classList.toggle("d-none", !matches);
            });
        });
    }
})();
