(function () {
    const config = document.getElementById("draftEditConfig");
    if (!config) return;

    const draftId = config.dataset.draftId;
    const antiForgeryToken = document.querySelector("#draftAntiForgeryForm input[name='__RequestVerificationToken']")?.value ?? "";

    // Condition description character count
    const condDesc = document.getElementById("draftConditionDesc");
    const condDescCount = document.getElementById("condDescCount");

    function updateCondDescCount() {
        if (condDesc && condDescCount) {
            condDescCount.textContent = condDesc.value.length;
        }
    }
    condDesc?.addEventListener("input", updateCondDescCount);
    updateCondDescCount();

    // Apply template to description
    const applyTemplateBtn = document.getElementById("applyTemplateBtn");
    const descriptionEl = document.getElementById("draftDescription");
    const templateEl = document.getElementById("descriptionTemplate");

    applyTemplateBtn?.addEventListener("click", function () {
        if (!descriptionEl || !templateEl) return;
        const template = templateEl.value.trim();
        if (!template) return;
        if (descriptionEl.value.trim() && !confirm("Replace the current description with the template?")) return;
        descriptionEl.value = template;
    });

    // Save template
    const saveTemplateBtn = document.getElementById("saveTemplateBtn");
    const templateSaveStatus = document.getElementById("templateSaveStatus");

    saveTemplateBtn?.addEventListener("click", async function () {
        if (!templateEl) return;
        saveTemplateBtn.disabled = true;
        if (templateSaveStatus) templateSaveStatus.textContent = "Saving…";

        try {
            const formData = new FormData();
            formData.append("template", templateEl.value);
            formData.append("__RequestVerificationToken", antiForgeryToken);

            const resp = await fetch(appUrl("/Drafts/SaveTemplate"), { method: "POST", body: formData });
            const data = await resp.json();
            if (templateSaveStatus) {
                templateSaveStatus.textContent = data.ok ? "Saved!" : "Error saving template.";
                setTimeout(() => { if (templateSaveStatus) templateSaveStatus.textContent = ""; }, 2000);
            }
        } catch (_) {
            if (templateSaveStatus) templateSaveStatus.textContent = "Error saving template.";
        } finally {
            saveTemplateBtn.disabled = false;
        }
    });

    // Save for Later
    const saveForLaterBtn = document.getElementById("saveForLaterBtn");
    const saveStatus = document.getElementById("saveStatus");

    async function saveDraft() {
        if (saveStatus) saveStatus.textContent = "Saving…";
        if (saveForLaterBtn) saveForLaterBtn.disabled = true;

        try {
            const formData = new FormData();
            formData.append("DraftId", draftId);
            formData.append("__RequestVerificationToken", antiForgeryToken);

            const price = document.getElementById("draftPrice")?.value;
            const condition = document.getElementById("draftCondition")?.value;
            const conditionDesc = document.getElementById("draftConditionDesc")?.value;
            const weightPounds = document.getElementById("draftWeightPounds")?.value;
            const length = document.getElementById("draftLength")?.value;
            const width = document.getElementById("draftWidth")?.value;
            const height = document.getElementById("draftHeight")?.value;
            const description = document.getElementById("draftDescription")?.value;

            if (price) formData.append("Price", price);
            if (condition) formData.append("ConditionId", condition);
            if (conditionDesc) formData.append("ConditionDescription", conditionDesc);
            if (weightPounds) formData.append("WeightPounds", weightPounds);
            if (length) formData.append("LengthIn", length);
            if (width) formData.append("WidthIn", width);
            if (height) formData.append("HeightIn", height);
            if (description) formData.append("Description", description);

            const resp = await fetch(appUrl("/Drafts/Save"), { method: "POST", body: formData });
            const data = await resp.json();

            if (saveStatus) {
                saveStatus.textContent = data.ok ? "Saved!" : ("Error: " + (data.error ?? "Unknown error"));
                setTimeout(() => { if (saveStatus) saveStatus.textContent = ""; }, 3000);
            }
        } catch (ex) {
            if (saveStatus) saveStatus.textContent = "Save failed.";
        } finally {
            if (saveForLaterBtn) saveForLaterBtn.disabled = false;
        }
    }

    saveForLaterBtn?.addEventListener("click", saveDraft);

    // Auto-save before publish: save current edits first, then submit the publish form
    const publishBtn = document.getElementById("publishBtn");
    const publishForm = document.getElementById("publishForm");

    publishBtn?.addEventListener("click", async function (e) {
        e.preventDefault();
        publishBtn.disabled = true;
        publishBtn.textContent = "Saving…";

        // Save edits first
        await saveDraft();

        publishBtn.textContent = "Publishing…";
        publishForm?.submit();
    });
})();
