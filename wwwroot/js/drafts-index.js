(function () {
    const config = document.getElementById("draftsConfig");
    if (!config) return;

    const isConnected = config.dataset.isConnected === "true";
    if (!isConnected) return;

    const syncIndicator = document.getElementById("syncIndicator");
    const syncNowBtn = document.getElementById("syncNowBtn");
    const syncError = document.getElementById("syncError");

    function showSyncError(msg) {
        if (!syncError) return;
        syncError.textContent = "Sync failed: " + msg;
        syncError.classList.remove("d-none");
    }

    function hideSyncError() {
        if (!syncError) return;
        syncError.classList.add("d-none");
    }

    async function triggerSync() {
        if (syncIndicator) {
            syncIndicator.classList.remove("d-none");
        }
        hideSyncError();
        try {
            const resp = await fetch(appUrl("/Drafts/TriggerSync"));
            const data = await resp.json();
            if (data.ok) {
                window.location.reload();
            } else if (data.error) {
                showSyncError(data.error);
            }
        } catch (e) {
            showSyncError(e.message || "Unknown error");
        } finally {
            if (syncIndicator) {
                syncIndicator.classList.add("d-none");
            }
        }
    }

    syncNowBtn?.addEventListener("click", triggerSync);

    // Auto-sync on first load
    triggerSync();
})();
