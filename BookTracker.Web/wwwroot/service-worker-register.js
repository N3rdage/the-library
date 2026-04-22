// Registers the BookTracker service worker. Kept as a separate file so it
// can be referenced via @Assets[] in App.razor (so the registration stub
// itself is cache-busted per deploy); the service-worker.js URL it points
// at is deliberately left unversioned — browsers need a stable URL to
// detect updates.
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/service-worker.js').catch(err => {
            console.warn('Service worker registration failed:', err);
        });
    });
}
