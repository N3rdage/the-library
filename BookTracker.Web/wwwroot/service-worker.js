// Service worker for the BookTracker PWA.
//
// Strategy: network-first with cache fallback for same-origin GETs. The
// Blazor SignalR hub at /_blazor is always passed straight through — we
// never want to cache hub traffic. Caching the static assets + HTML shell
// means the install screen still shows when the network's flaky, and
// subsequent loads get an instant-feeling paint while the network response
// refreshes the cache in the background.
//
// Bump CACHE_VERSION whenever cache semantics need to invalidate cleanly.

// v2: pass navigations through entirely (see fetch handler). Bumping
// the cache name forces the activate handler to drop the v1 cache,
// which had been storing HTML pages alongside assets — those are
// stale-prone for an authenticated server-rendered app.
const CACHE_VERSION = 'booktracker-v2';

self.addEventListener('install', event => {
    // Activate as soon as possible — no need to wait for all tabs to close.
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(
            keys
                .filter(k => k !== CACHE_VERSION)
                .map(k => caches.delete(k))
        );
        await self.clients.claim();
    })());
});

self.addEventListener('fetch', event => {
    const request = event.request;
    const url = new URL(request.url);

    // Pass-through SignalR hub traffic entirely.
    if (url.pathname.startsWith('/_blazor')) {
        return;
    }

    // Pass navigations through — Blazor Server pages need a live
    // request to render (the SignalR circuit alone can't serve them
    // from cache), and intercepting navigation fetches caused
    // (a) the post-form-POST redirect to /series/{id} surfacing as
    //     "Fetch failed and no cached response" because the SW couldn't
    //     reach the server's redirect handling cleanly, and
    // (b) any genuinely offline navigation falling into the same
    //     no-cached-fallback hole.
    // Service worker is for static-asset caching only.
    if (request.mode === 'navigate' || request.destination === 'document') {
        return;
    }

    // Only handle same-origin GET requests for assets.
    if (request.method !== 'GET' || url.origin !== location.origin) {
        return;
    }

    event.respondWith(networkFirstWithCacheFallback(request));
});

async function networkFirstWithCacheFallback(request) {
    const cache = await caches.open(CACHE_VERSION);
    try {
        const response = await fetch(request);
        // Cache successful same-origin responses for future offline use.
        if (response.ok) {
            cache.put(request, response.clone());
        }
        return response;
    } catch {
        const cached = await cache.match(request);
        if (cached) return cached;
        // No network, no cache — let the browser surface the error.
        throw new Error(`Fetch failed and no cached response for ${request.url}`);
    }
}
