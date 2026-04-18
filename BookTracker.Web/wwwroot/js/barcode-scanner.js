// JS interop for html5-qrcode barcode scanning.
// Called from Blazor via IJSRuntime.

let scanner = null;
let lastScannedCode = null;
let lastScannedTime = 0;
const DEBOUNCE_MS = 3000; // ignore duplicate scans within 3 seconds

window.BarcodeScanner = {
    start: async function (elementId, dotNetRef) {
        if (scanner) {
            try { await scanner.stop(); } catch { }
            scanner = null;
        }

        lastScannedCode = null;
        lastScannedTime = 0;

        scanner = new Html5Qrcode(elementId);

        const config = {
            fps: 10,
            qrbox: { width: 280, height: 80 },
            aspectRatio: 2.0, // wide and short — matches book barcode shape
            formatsToSupport: [
                Html5QrcodeSupportedFormats.EAN_13,
                Html5QrcodeSupportedFormats.EAN_8
            ]
        };

        try {
            await scanner.start(
                { facingMode: "environment" },
                config,
                async (decodedText) => {
                    const now = Date.now();
                    if (decodedText === lastScannedCode && (now - lastScannedTime) < DEBOUNCE_MS) {
                        return; // debounce: same barcode scanned too quickly
                    }
                    lastScannedCode = decodedText;
                    lastScannedTime = now;
                    await dotNetRef.invokeMethodAsync("OnBarcodeScanned", decodedText);
                },
                (_errorMessage) => {
                    // Ignore per-frame scan failures — they're normal while
                    // the user positions the barcode.
                }
            );
        } catch (err) {
            await dotNetRef.invokeMethodAsync("OnScannerError", err.toString());
        }
    },

    stop: async function () {
        if (scanner) {
            try { await scanner.stop(); } catch { }
            scanner = null;
        }
    }
};
