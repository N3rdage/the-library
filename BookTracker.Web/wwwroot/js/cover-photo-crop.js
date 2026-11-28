// Crop box for the cover photo picker (Components/Shared/CoverPhotoPicker.razor).
//
// The chosen file never crosses SignalR: a listener on the file input keeps it in
// the browser, the crop happens on a canvas, and only the cropped JPEG goes back
// to .NET as a JS stream reference. Drawing an <img> applies its EXIF orientation,
// which is why the crop is done here and not on the server.
//
// The crop box is stored as fractions of the displayed image, so it stays put when
// the layout resizes. Elements are found by data-* attributes under the root.

window.CoverPhotoCrop = (function () {
    const MAX_EDGE = 2400;
    const MIN_FRACTION = 0.1;
    const states = new WeakMap();

    const clamp = (v, lo, hi) => Math.min(Math.max(v, lo), hi);
    const find = (root, name) => root.querySelector(`[data-crop-${name}]`);

    function paint(st, box) {
        box.style.left = `${st.box.x * 100}%`;
        box.style.top = `${st.box.y * 100}%`;
        box.style.width = `${st.box.w * 100}%`;
        box.style.height = `${st.box.h * 100}%`;
    }

    function wire(st, img, box) {
        if (st.wired.has(box)) return;
        st.wired.add(box);
        const handle = box.querySelector('[data-crop-handle]');
        let drag = null;

        box.addEventListener('pointerdown', e => {
            const rect = img.getBoundingClientRect();
            drag = { resize: e.target === handle, x: e.clientX, y: e.clientY, start: { ...st.box }, w: rect.width, h: rect.height };
            box.setPointerCapture(e.pointerId);
            e.preventDefault();
        });
        box.addEventListener('pointermove', e => {
            if (!drag) return;
            const dx = (e.clientX - drag.x) / drag.w;
            const dy = (e.clientY - drag.y) / drag.h;
            const b = drag.start;
            st.box = drag.resize
                ? { x: b.x, y: b.y, w: clamp(b.w + dx, MIN_FRACTION, 1 - b.x), h: clamp(b.h + dy, MIN_FRACTION, 1 - b.y) }
                : { x: clamp(b.x + dx, 0, 1 - b.w), y: clamp(b.y + dy, 0, 1 - b.h), w: b.w, h: b.h };
            paint(st, box);
        });
        const end = () => { drag = null; };
        box.addEventListener('pointerup', end);
        box.addEventListener('pointercancel', end);
    }

    return {
        // Remember the file the moment it is chosen; MudFileUpload may reset the input afterwards.
        attach(root) {
            const input = root.querySelector('input[type=file]');
            if (!input || states.has(root)) return;
            const st = { file: null, box: { x: 0, y: 0, w: 1, h: 1 }, wired: new WeakSet(), sourceUrl: null, previewUrl: null };
            states.set(root, st);
            input.addEventListener('change', () => {
                if (input.files && input.files[0]) st.file = input.files[0];
            });
        },

        // Shows the remembered file in the crop stage with the box covering the whole image.
        async open(root) {
            const st = states.get(root);
            const img = find(root, 'img');
            const box = find(root, 'box');
            if (!st || !st.file || !img || !box) throw new Error('No photo chosen.');

            if (st.sourceUrl) URL.revokeObjectURL(st.sourceUrl);
            st.sourceUrl = URL.createObjectURL(st.file);
            await new Promise((resolve, reject) => {
                img.onload = resolve;
                img.onerror = () => reject(new Error('Not a readable image.'));
                img.src = st.sourceUrl;
            });
            st.box = { x: 0, y: 0, w: 1, h: 1 };
            wire(st, img, box);
            paint(st, box);
        },

        // Draws the selected region at source resolution (long edge capped) and returns it as a JPEG stream.
        async crop(root) {
            const st = states.get(root);
            const img = find(root, 'img');
            const sw = Math.round(st.box.w * img.naturalWidth);
            const sh = Math.round(st.box.h * img.naturalHeight);
            const scale = Math.min(1, MAX_EDGE / Math.max(sw, sh));
            const canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(sw * scale));
            canvas.height = Math.max(1, Math.round(sh * scale));
            const ctx = canvas.getContext('2d');
            ctx.fillStyle = '#fff'; // JPEG has no alpha
            ctx.fillRect(0, 0, canvas.width, canvas.height);
            ctx.drawImage(img, Math.round(st.box.x * img.naturalWidth), Math.round(st.box.y * img.naturalHeight), sw, sh,
                0, 0, canvas.width, canvas.height);

            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/jpeg', 0.9));
            if (!blob) throw new Error('Could not crop the photo.');
            if (st.previewUrl) URL.revokeObjectURL(st.previewUrl);
            st.previewUrl = URL.createObjectURL(blob);
            return DotNet.createJSStreamReference(blob);
        },

        showPreview(root) {
            const st = states.get(root);
            const preview = find(root, 'preview');
            if (st && st.previewUrl && preview) preview.src = st.previewUrl;
        },

        release(root) {
            const st = states.get(root);
            if (!st) return;
            if (st.sourceUrl) URL.revokeObjectURL(st.sourceUrl);
            if (st.previewUrl) URL.revokeObjectURL(st.previewUrl);
            states.delete(root);
        },
    };
})();
