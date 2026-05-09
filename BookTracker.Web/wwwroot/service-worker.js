// Service worker for the BookTracker PWA.
//
// Strategy: network-first with cache fallback for same-origin GETs by
// default. Two route-specific overrides that need offline-first
// semantics for the bookshop mode (see docs/bookshop-mode-design.md):
//
//   - /api/catalog-snapshot — cache-first with stale-while-revalidate.
//     The bookshop offline cache must read this from disk when offline;
//     network-first would fail and break the killer use cases.
//
//   - /bookshop* navigations — cache-first. The /bookshop page is
//     Static SSR (no SignalR / Interactive Server) and must work
//     offline. This is the ONE navigation path the SW intercepts;
//     everything else still passes through (Blazor Server pages
//     need a live request to render).
//
// The Blazor SignalR hub at /_blazor is always passed straight through.
//
// Cache layout: two named caches.
//   booktracker-vN — assets (network-first), bumps invalidate cleanly.
//   booktracker-catalog-vM — catalog snapshot data (cache-first),
//     versioned independently so SW shape changes don't blow the
//     catalog cache, and vice versa.

const CACHE_VERSION = 'booktracker-v3';
const CATALOG_CACHE = 'booktracker-catalog-v1';

self.addEventListener('install', event => {
    // Activate as soon as possible — no need to wait for all tabs to close.
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(
            keys
                .filter(k => k !== CACHE_VERSION && k !== CATALOG_CACHE)
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

    // /api/catalog-snapshot — cache-first with stale-while-revalidate.
    // Returns cached bytes immediately when available; kicks off a
    // background refresh that updates the cache for next time. When
    // offline and uncached, surfaces the fetch error to the caller
    // (catalog-cache.js init() catches and degrades).
    if (request.method === 'GET'
        && url.origin === location.origin
        && url.pathname === '/api/catalog-snapshot') {
        event.respondWith(cacheFirstWithStaleWhileRevalidate(request, CATALOG_CACHE));
        return;
    }

    // /bookshop* navigations — cache-first. The page itself is
    // Static SSR + JS-only; doesn't need a live SignalR circuit.
    // First visit fetches + caches; subsequent visits serve from
    // cache instantly even offline.
    if ((request.mode === 'navigate' || request.destination === 'document')
        && url.pathname.startsWith('/bookshop')) {
        event.respondWith(cacheFirstWithStaleWhileRevalidate(request, CACHE_VERSION));
        return;
    }

    // Pass non-bookshop navigations through — Blazor Server pages need
    // a live request to render (the SignalR circuit alone can't serve
    // them from cache), and intercepting navigation fetches caused
    // (a) the post-form-POST redirect to /series/{id} surfacing as
    //     "Fetch failed and no cached response" because the SW couldn't
    //     reach the server's redirect handling cleanly, and
    // (b) any genuinely offline navigation falling into the same
    //     no-cached-fallback hole.
    if (request.mode === 'navigate' || request.destination === 'document') {
        return;
    }

    // Same-origin GET assets — network-first with cache fallback (existing).
    if (request.method !== 'GET' || url.origin !== location.origin) {
        return;
    }

    event.respondWith(networkFirstWithCacheFallback(request));
});

// Cache-first with background revalidate. Returns the cached response
// immediately if present; kicks off a non-blocking fetch that updates
// the cache for next time. On a true cache miss, awaits the network
// fetch and caches the result.
async function cacheFirstWithStaleWhileRevalidate(request, cacheName) {
    const cache = await caches.open(cacheName);
    const cached = await cache.match(request);
    if (cached) {
        // Background refresh — fire and forget. Failures are silent;
        // the cached response we just returned is the answer.
        fetch(request)
            .then(response => {
                if (response.ok) cache.put(request, response.clone());
            })
            .catch(() => { /* offline / 5xx / etc. — keep cached */ });
        return cached;
    }
    // No cache hit — try network, cache the result.
    try {
        const response = await fetch(request);
        if (response.ok) {
            cache.put(request, response.clone());
        }
        return response;
    } catch (err) {
        throw new Error(`Fetch failed and no cached response for ${request.url}`);
    }
}

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
