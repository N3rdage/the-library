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

        const canvas = document.createElement("canvas");
        canvas.width = video.videoWidth;
        canvas.height = video.videoHeight;
        const ctx = canvas.getContext("2d");
        ctx.drawImage(video, 0, 0);

        // Return as base64 JPEG (strip the data URL prefix)
        const dataUrl = canvas.toDataURL("image/jpeg", 0.85);
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
