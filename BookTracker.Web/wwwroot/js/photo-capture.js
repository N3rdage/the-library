// JS interop for capturing a photo from the device camera.
// Returns a base64-encoded JPEG image to Blazor.

let stream = null;
let videoElement = null;

window.PhotoCapture = {
    start: async function (videoElementId) {
        const video = document.getElementById(videoElementId);
        if (!video) return false;

        try {
            stream = await navigator.mediaDevices.getUserMedia({
                video: { facingMode: "environment", width: { ideal: 1280 }, height: { ideal: 720 } }
            });
            video.srcObject = stream;
            await video.play();
            videoElement = video;
            return true;
        } catch (err) {
            console.error("Camera access failed:", err);
            return false;
        }
    },

    capture: function (videoElementId) {
        const video = document.getElementById(videoElementId);
        if (!video || !video.srcObject) return null;

        // Scale down for OCR — we only need enough resolution to read text
        const maxWidth = 800;
        const scale = Math.min(1, maxWidth / video.videoWidth);
        const canvas = document.createElement("canvas");
        canvas.width = Math.round(video.videoWidth * scale);
        canvas.height = Math.round(video.videoHeight * scale);
        const ctx = canvas.getContext("2d");
        ctx.drawImage(video, 0, 0, canvas.width, canvas.height);

        // Return as base64 JPEG at moderate quality (strip the data URL prefix)
        const dataUrl = canvas.toDataURL("image/jpeg", 0.7);
        return dataUrl.replace(/^data:image\/jpeg;base64,/, "");
    },

    stop: function () {
        if (stream) {
            stream.getTracks().forEach(t => t.stop());
            stream = null;
        }
        if (videoElement) {
            videoElement.srcObject = null;
            videoElement = null;
        }
    }
};
