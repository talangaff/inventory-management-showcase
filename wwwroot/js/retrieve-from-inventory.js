(function () {
    const configEl = document.getElementById("retrieveConfig");
    if (!configEl) return;

    const itemsJson = configEl.dataset.items || "[]";
    let items;
    try {
        items = JSON.parse(itemsJson);
    } catch (e) {
        items = [];
    }

    // scanCounts[itemId] = number of times scanned so far
    const scanCounts = {};
    items.forEach(function (item) {
        scanCounts[item.itemId] = 0;
    });

    // Build a lookup: barcode -> itemId
    const barcodeToItemId = {};
    items.forEach(function (item) {
        if (item.barcode) barcodeToItemId[item.barcode] = item.itemId;
    });

    const feedbackEl = document.getElementById("retrieveFeedback");
    const itemListEl = document.getElementById("retrieveItemList");
    const keyboardInput = document.getElementById("retrieveKeyboardInput");

    function showFeedback(message, isSuccess) {
        if (!feedbackEl) return;
        feedbackEl.classList.remove("d-none", "alert-success", "alert-danger", "alert-warning");
        feedbackEl.classList.add(isSuccess === true ? "alert-success" : isSuccess === false ? "alert-danger" : "alert-warning");
        feedbackEl.textContent = message;
    }

    function getItemRow(itemId) {
        return itemListEl ? itemListEl.querySelector('[data-item-id="' + itemId + '"]') : null;
    }

    function updateProgressDisplay(itemId) {
        const row = getItemRow(itemId);
        if (!row) return;

        const item = items.find(function (x) { return x.itemId === itemId; });
        if (!item) return;

        const scanned = scanCounts[itemId] || 0;
        const total = item.totalQuantity;

        const progressEl = row.querySelector(".retrieve-progress");
        if (progressEl) {
            progressEl.textContent = scanned + " / " + total + " scanned";
        }

        const progressBar = row.querySelector(".retrieve-progress-bar");
        if (progressBar) {
            const pct = total > 0 ? Math.round((scanned / total) * 100) : 0;
            progressBar.style.width = pct + "%";
            progressBar.setAttribute("aria-valuenow", String(scanned));
        }

        if (scanned >= total) {
            // Animate out then remove
            row.classList.add("opacity-0");
            setTimeout(function () { row.remove(); }, 350);

            const remaining = itemListEl ? itemListEl.querySelectorAll(".retrieve-list-item").length : 0;
            if (remaining <= 1) {
                setTimeout(function () {
                    const emptyEl = document.getElementById("retrieveEmptyMsg");
                    if (emptyEl) emptyEl.classList.remove("d-none");
                }, 400);
            }
        }
    }

    function processBarcode(barcode) {
        barcode = (barcode || "").trim();
        if (!barcode) return;

        const itemId = barcodeToItemId[barcode];
        if (!itemId) {
            showFeedback("Barcode not recognised: " + barcode, false);
            playInvalidScanSound();
            return;
        }

        const item = items.find(function (x) { return x.itemId === itemId; });
        if (!item) return;

        const current = scanCounts[itemId] || 0;
        if (current >= item.totalQuantity) {
            showFeedback(item.itemName + " is already fully retrieved.", null);
            return;
        }

        scanCounts[itemId] = current + 1;
        const newCount = scanCounts[itemId];
        const remaining = item.totalQuantity - newCount;

        if (newCount >= item.totalQuantity) {
            showFeedback("All " + item.totalQuantity + " of \"" + item.itemName + "\" retrieved!", true);
            persistRetrieved(itemId);
        } else {
            showFeedback("Scanned \"" + item.itemName + "\". " + remaining + " more needed.", true);
        }

        playValidScanSound();
        updateProgressDisplay(itemId);
    }

    function persistRetrieved(itemId) {
        var csrfToken = document.querySelector('input[name="__RequestVerificationToken"]');
        fetch(appUrl("/Orders/MarkRetrieved"), {
            method: "POST",
            headers: {
                "Content-Type": "application/json",
                "X-Requested-With": "XMLHttpRequest",
                "RequestVerificationToken": csrfToken ? csrfToken.value : ""
            },
            body: JSON.stringify({ itemId: itemId })
        }).catch(function () {});
    }

    // Audio
    var audioContext = null;
    var audioUnlocked = false;

    // Builds a data URI for a minimal 1-sample silent WAV.
    // Playing this through an <audio> element promotes the iOS audio session to
    // "Playback" category, which bypasses the hardware silent switch.
    function buildSilentWavDataUri() {
        var buf = new ArrayBuffer(45);
        var v = new DataView(buf);
        var s = function (o, c) { for (var i = 0; i < c.length; i++) v.setUint8(o + i, c.charCodeAt(i)); };
        s(0,  "RIFF"); v.setUint32(4,  37, true);
        s(8,  "WAVE");
        s(12, "fmt "); v.setUint32(16, 16, true);
        v.setUint16(20, 1, true);   // PCM
        v.setUint16(22, 1, true);   // mono
        v.setUint32(24, 8000, true); // sample rate
        v.setUint32(28, 8000, true); // byte rate
        v.setUint16(32, 1, true);   // block align
        v.setUint16(34, 8, true);   // bits per sample
        s(36, "data"); v.setUint32(40, 1, true);
        v.setUint8(44, 128); // 1 sample of silence (unsigned 8-bit midpoint)
        var bytes = new Uint8Array(buf);
        var bin = "";
        for (var i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
        return "data:audio/wav;base64," + btoa(bin);
    }

    var silentWavUri = null;

    function ensureAudioContext() {
        if (!audioContext) {
            var AudioCtx = window.AudioContext || window.webkitAudioContext;
            if (!AudioCtx) return null;
            audioContext = new AudioCtx();
        }
        return audioContext;
    }

    function playTone(frequency, durationMs, type, startDelaySec) {
        var ctx = ensureAudioContext();
        if (!ctx) return;
        try {
            var oscillator = ctx.createOscillator();
            var gain = ctx.createGain();
            oscillator.type = type || "sine";
            oscillator.frequency.setValueAtTime(frequency, ctx.currentTime + startDelaySec);
            gain.gain.setValueAtTime(0.0001, ctx.currentTime + startDelaySec);
            gain.gain.linearRampToValueAtTime(0.35, ctx.currentTime + startDelaySec + 0.01);
            gain.gain.linearRampToValueAtTime(0.0001, ctx.currentTime + startDelaySec + (durationMs / 1000));
            oscillator.connect(gain);
            gain.connect(ctx.destination);
            oscillator.start(ctx.currentTime + startDelaySec);
            oscillator.stop(ctx.currentTime + startDelaySec + (durationMs / 1000));
        } catch (err) {}
    }

    // Must be called synchronously inside a user gesture handler to unlock audio on iOS.
    function unlockAudio() {
        if (audioUnlocked) return;
        audioUnlocked = true;

        // Play a silent <audio> element — this promotes the iOS audio session from
        // "Ambient" (muted by silent switch) to "Playback" (bypasses silent switch).
        try {
            if (!silentWavUri) silentWavUri = buildSilentWavDataUri();
            var silentEl = new Audio(silentWavUri);
            silentEl.play().catch(function () {});
        } catch (e) {}

        // Also unlock the Web Audio context with a BufferSource node.
        var ctx = ensureAudioContext();
        if (!ctx) return;
        try {
            var silentBuf = ctx.createBuffer(1, 1, 22050);
            var src = ctx.createBufferSource();
            src.buffer = silentBuf;
            src.connect(ctx.destination);
            src.start(0);
        } catch (e) {}
        ctx.resume().catch(function () {});
    }

    function playValidScanSound() {
        playTone(980, 95, "square", 0);
        playTone(1320, 95, "square", 0.11);
        if (navigator.vibrate) navigator.vibrate(35);
    }

    function playInvalidScanSound() {
        playTone(280, 140, "sawtooth", 0);
        playTone(190, 150, "sawtooth", 0.16);
        if (navigator.vibrate) navigator.vibrate([45, 30, 45]);
    }

    // Camera scanner
    var codeReader = null;
    var scannerControls = null;
    var lastScannedValue = "";
    var lastScannedAtMs = 0;

    var startBtn = document.getElementById("retrieveStartScan");
    var stopBtn = document.getElementById("retrieveStopScan");
    var scannerVideo = document.getElementById("retrieveScannerVideo");
    var scanStatus = document.getElementById("retrieveScanStatus");

    async function startCameraScan() {
        if (scannerControls) return;
        if (!window.ZXingBrowser) {
            if (scanStatus) scanStatus.textContent = "Scanner library not loaded.";
            return;
        }

        try {
            unlockAudio(); // must be called synchronously inside this click handler
            if (scanStatus) scanStatus.textContent = "Starting camera...";
            codeReader = new ZXingBrowser.BrowserMultiFormatReader();

            var devices = await ZXingBrowser.BrowserCodeReader.listVideoInputDevices();
            var preferredDeviceId = null;
            for (var i = 0; i < devices.length; i++) {
                if (/back|rear|environment/i.test(devices[i].label)) {
                    preferredDeviceId = devices[i].deviceId;
                    break;
                }
            }

            scannerControls = await codeReader.decodeFromVideoDevice(
                preferredDeviceId,
                scannerVideo,
                function (result) {
                    if (!result) return;
                    var now = Date.now();
                    var value = (result.text || "").trim();
                    if (!value) return;
                    if (value === lastScannedValue && now - lastScannedAtMs < 1200) return;
                    lastScannedValue = value;
                    lastScannedAtMs = now;
                    processBarcode(value);
                }
            );

            if (scanStatus) scanStatus.textContent = "Camera active. Point at an item barcode.";
        } catch (err) {
            if (scanStatus) scanStatus.textContent = "Could not access camera. Use HTTPS and allow camera permission.";
        }
    }

    function stopCameraScan() {
        if (scannerControls) {
            scannerControls.stop();
            scannerControls = null;
        }
        if (scanStatus) scanStatus.textContent = "Camera is off.";
    }

    if (startBtn) startBtn.addEventListener("click", startCameraScan);
    if (stopBtn) stopBtn.addEventListener("click", stopCameraScan);

    // Keyboard / USB scanner input
    if (keyboardInput) {
        keyboardInput.addEventListener("keydown", function (e) {
            unlockAudio(); // unlock on any key press (user gesture)
            if (e.key === "Enter") {
                e.preventDefault();
                var val = keyboardInput.value.trim();
                keyboardInput.value = "";
                processBarcode(val);
            }
        });
    }

    // Fallback: unlock audio on first touch anywhere on the page (covers iOS edge cases)
    document.addEventListener("touchstart", function onFirstTouch() {
        unlockAudio();
        document.removeEventListener("touchstart", onFirstTouch);
    }, { passive: true });
}());
