// bookshop-page.js — page-specific glue for /bookshop.
//
// Responsibility: wire DOM events on the /bookshop page to
// window.catalogCache (IndexedDB) + window.BarcodeScanner. Pure JS,
// no Blazor dependency — the page has no Blazor circuit even when
// online (it inherits the project's InteractiveServer rendermode but
// uses no @onclick / @bind / @code), so everything has to flow
// through native DOM events. Loaded globally from App.razor; gates
// itself on path so non-/bookshop pages pay nothing but the script
// download.

(function () {
    function isBookshopPage() {
        return window.location.pathname.toLowerCase().startsWith('/bookshop');
    }

    function $(id) { return document.getElementById(id); }

    // HTML escape — all dynamic strings (titles, author names, ISBNs)
    // pass through this before being injected into innerHTML. Cheap
    // belt-and-braces against a malformed catalog row taking out the
    // page; the snapshot endpoint already returns plain JSON, so the
    // realistic risk is a quote / angle bracket in a book title.
    function esc(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function showError(msg) {
        const el = $('bookshop-scanner-error');
        if (!el) return;
        el.textContent = msg;
        el.classList.remove('d-none');
    }

    function clearError() {
        const el = $('bookshop-scanner-error');
        if (!el) return;
        el.textContent = '';
        el.classList.add('d-none');
    }

    function setScannerActive(active) {
        const toggle = $('bookshop-scan-toggle');
        const panel = $('bookshop-scanner-panel');
        if (!toggle || !panel) return;
        const idle = toggle.querySelector('[data-state="idle"]');
        const live = toggle.querySelector('[data-state="active"]');
        if (active) {
            toggle.classList.remove('btn-primary');
            toggle.classList.add('btn-danger');
            idle.classList.add('d-none');
            live.classList.remove('d-none');
            panel.classList.remove('d-none');
        } else {
            toggle.classList.remove('btn-danger');
            toggle.classList.add('btn-primary');
            idle.classList.remove('d-none');
            live.classList.add('d-none');
            panel.classList.add('d-none');
        }
    }

    let scannerActive = false;

    async function startScanner() {
        clearError();
        scannerActive = true;
        setScannerActive(true);

        try {
            await window.BarcodeScanner.startJs(
                'bookshop-barcode-reader',
                onBarcodeScanned,
                onScannerError
            );
        } catch (err) {
            scannerActive = false;
            setScannerActive(false);
            showError('Camera error: ' + (err && err.toString ? err.toString() : err));
        }
    }

    async function stopScanner() {
        scannerActive = false;
        setScannerActive(false);
        try { await window.BarcodeScanner.stop(); } catch { /* ignore */ }
    }

    async function onBarcodeScanned(decodedText) {
        // Stop the scanner on a successful scan — same UX as
        // /shopping. Avoids re-firing while the user reads the result.
        await stopScanner();
        const isbn = (decodedText || '').trim();
        if (!isbn) return;
        await lookupAndRender(isbn);
    }

    function onScannerError(err) {
        scannerActive = false;
        setScannerActive(false);
        showError('Camera error: ' + err);
    }

    async function manualLookup() {
        const input = $('bookshop-isbn-input');
        if (!input) return;
        const raw = (input.value || '').trim();
        // Strip non-alphanumeric (allow trailing X for ISBN-10 check
        // digits) so "978-0-553-28789-9" pasted from a website still
        // resolves. The cache stores ISBNs unhyphenated.
        const cleaned = raw.replace(/[^0-9Xx]/g, '');
        if (cleaned.length < 10 || cleaned.length > 13) {
            renderResult({
                kind: 'error',
                message: 'Enter a 10- or 13-digit ISBN.'
            });
            return;
        }
        await lookupAndRender(cleaned);
    }

    async function lookupAndRender(isbn) {
        try {
            const book = await window.catalogCache.lookupByIsbn(isbn);
            if (book) {
                renderResult({ kind: 'found', isbn, book });
            } else {
                renderResult({ kind: 'notfound', isbn });
            }
        } catch (err) {
            renderResult({
                kind: 'error',
                message: 'Lookup failed: ' + (err && err.message ? err.message : err)
            });
        }
    }

    function renderStarRow(rating) {
        if (!rating || rating < 1) return '';
        const filled = '★'.repeat(Math.max(0, Math.min(5, rating)));
        const empty = '☆'.repeat(Math.max(0, 5 - rating));
        return `<span class="text-warning" aria-label="Rating ${rating} of 5">${filled}<span class="text-muted">${empty}</span></span>`;
    }

    function renderResult(result) {
        const container = $('bookshop-result');
        if (!container) return;

        if (result.kind === 'error') {
            container.innerHTML = `
                <div class="card border-warning">
                    <div class="card-body">
                        <div class="alert alert-warning mb-0 py-2">${esc(result.message)}</div>
                    </div>
                </div>`;
            return;
        }

        if (result.kind === 'notfound') {
            container.innerHTML = `
                <div class="card">
                    <div class="card-body text-center">
                        <span class="badge bg-secondary fs-6 mb-2">Not in your library</span>
                        <p class="text-muted small mb-2">ISBN <span class="font-monospace">${esc(result.isbn)}</span> isn't in your collection.</p>
                        <div class="d-flex gap-2 justify-content-center flex-wrap">
                            <a href="/books/add" class="btn btn-primary btn-sm">Add to library</a>
                            <a href="/shopping" class="btn btn-outline-secondary btn-sm">Add to wishlist</a>
                        </div>
                        <p class="text-muted small mt-2 mb-0">(both require an online connection)</p>
                    </div>
                </div>`;
            return;
        }

        // Found
        const b = result.book;
        const status = b.status != null ? String(b.status) : '';
        const authorLine = (b.allAuthors && b.allAuthors.length > 1)
            ? b.allAuthors.join(', ')
            : (b.primaryAuthor || '');
        const ratingHtml = renderStarRow(b.rating);
        const statusBadge = status ? `<span class="badge bg-light text-dark border ms-1">${esc(status)}</span>` : '';

        container.innerHTML = `
            <div class="card border-success">
                <div class="card-body">
                    <span class="badge bg-success mb-2">In your library</span>
                    <div class="fw-semibold fs-5">${esc(b.title || '(untitled)')}</div>
                    <div class="text-muted">${esc(authorLine)}</div>
                    <div class="mt-1">${ratingHtml}${statusBadge}</div>
                    <div class="text-muted small mt-2">ISBN: <span class="font-monospace">${esc(result.isbn)}</span></div>
                    <div class="mt-3">
                        <a href="/books/${encodeURIComponent(b.id)}" class="btn btn-outline-primary btn-sm">Open in app →</a>
                    </div>
                </div>
            </div>`;
    }

    function formatRelative(iso) {
        if (!iso) return 'never';
        const then = new Date(iso).getTime();
        if (isNaN(then)) return 'unknown';
        const diff = Date.now() - then;
        const mins = Math.floor(diff / 60000);
        if (mins < 1) return 'just now';
        if (mins < 60) return `${mins}m ago`;
        const hours = Math.floor(mins / 60);
        if (hours < 24) return `${hours}h ago`;
        const days = Math.floor(hours / 24);
        return `${days}d ago`;
    }

    async function refreshFooter() {
        const meta = $('bookshop-meta');
        if (!meta) return;
        try {
            const m = await window.catalogCache.getMeta();
            if (!m || !m.syncedAt) {
                meta.textContent = 'Catalog not synced yet — connect online to sync.';
                return;
            }
            const offline = (typeof navigator !== 'undefined' && navigator.onLine === false);
            const syncStr = formatRelative(m.syncedAt);
            const statePrefix = offline ? '📵 Offline · ' : '';
            meta.textContent = `${statePrefix}Catalog: ${m.bookCount || 0} books · synced ${syncStr}`;
        } catch (err) {
            meta.textContent = 'Catalog status unavailable.';
        }
    }

    async function init() {
        if (!isBookshopPage()) return;

        // Init the cache. If offline + first-ever visit this throws
        // internally and surfaces empty results — the user sees the
        // "not synced yet" footer message and can't look anything up,
        // which is the correct state.
        try {
            await window.catalogCache.init();
        } catch (err) {
            console.warn('[bookshop] catalog init failed:', err);
        }

        await refreshFooter();

        const toggle = $('bookshop-scan-toggle');
        if (toggle) {
            toggle.addEventListener('click', async () => {
                if (scannerActive) {
                    await stopScanner();
                } else {
                    await startScanner();
                }
            });
        }

        const lookup = $('bookshop-isbn-lookup');
        if (lookup) {
            lookup.addEventListener('click', () => { manualLookup(); });
        }

        const input = $('bookshop-isbn-input');
        if (input) {
            input.addEventListener('keyup', (e) => {
                if (e.key === 'Enter') manualLookup();
            });
        }

        // Online/offline transitions update the footer label so the
        // user sees the state change live. Doesn't trigger a refresh
        // by itself — that's PR 5 polish.
        window.addEventListener('online', refreshFooter);
        window.addEventListener('offline', refreshFooter);
    }

    // Self-init. Loaded globally from App.razor; on non-/bookshop
    // pages the isBookshopPage() guard makes init() a no-op.
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    // Expose for DevTools poking — same pattern as catalogCache.
    window.BookshopPage = { init };
})();
