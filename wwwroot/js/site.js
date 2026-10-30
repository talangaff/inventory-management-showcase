// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// ─── Shared delete-confirmation modal wiring ────────────────────────────────
// Pairs with Views/Shared/_DeleteConfirmModal.cshtml. Trigger buttons set
// data-id / data-name (plus any extra data-* attrs listed in `extraFields`)
// and this copies them onto the modal's hidden id field + name span when the
// modal opens, mirroring the pattern shipping-boxes-index.js used to do by
// hand for a single page.
window.InventoryDeleteModal = {
    wire: function (modalId, options) {
        options = options || {};
        var idField = options.idField || "id";
        var nameField = options.nameField || "name";
        var extraFields = options.extraFields || [];

        var modal = document.getElementById(modalId);
        if (!modal) return;

        modal.addEventListener("show.bs.modal", function (event) {
            var btn = event.relatedTarget;
            if (!btn) return;

            var id = btn.getAttribute("data-id");
            var name = btn.getAttribute("data-name");

            var idInput = document.getElementById(modalId + idField.charAt(0).toUpperCase() + idField.slice(1));
            var nameEl = document.getElementById(modalId + "Name");

            if (idInput) idInput.value = id;
            if (nameEl) nameEl.textContent = name;

            extraFields.forEach(function (field) {
                var value = btn.getAttribute("data-" + field);
                var target = document.getElementById(modalId + field.charAt(0).toUpperCase() + field.slice(1));
                if (target) target.value = value;
            });
        });
    }
};
