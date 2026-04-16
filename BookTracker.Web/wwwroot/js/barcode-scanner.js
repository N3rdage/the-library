// JS interop for html5-qrcode barcode scanning.
// Called from Blazor via IJSRuntime.

let scanner = null;

window.BarcodeScanner = {
    start: async function (elementId, dotNetRef) {
        if (scanner) {
            try { await scanner.stop(); } catch { }
            scanner = null;
        }

        scanner = new Html5Qrcode(elementId);

        const config = {
            fps: 10,
            qrbox: { width: 250, height: 150 },
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
