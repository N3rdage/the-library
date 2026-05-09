// catalog-cache.js — IndexedDB-backed offline cache for the bookshop mode.
//
// Pure JS module attached to window.catalogCache. No Blazor dependency;
// callable from Razor pages (PR 3+) and from the browser DevTools console
// for direct testing. Reads /api/catalog-snapshot via fetch (which the
// service worker caches with stale-while-revalidate semantics, see
// service-worker.js); writes the slim catalog into IndexedDB; serves
// lookup queries entirely client-side.
//
// Database layout (DB version 1):
//   - books store: keyPath=id, multiEntry index on `isbns`,
//     index on `primaryAuthor`.
//   - authors store: keyPath=id, index on `canonicalId`.
//   - meta store: keyPath=key. Holds version, syncedAt, bookCount,
//     authorCount entries.
//
// Public surface:
//   await catalogCache.init()                    — opens DB, populates if empty
//   await catalogCache.refresh()                 — refetch + repopulate from server
//   await catalogCache.lookupByIsbn(isbn)        — book or null
//   await catalogCache.lookupByAuthor(canonicalId) — books credited to canonical
//                                                    or any of its aliases
//   await catalogCache.searchAuthors(q, limit)   — canonical author rows matching name
//   await catalogCache.getMeta()                 — { version, syncedAt, bookCount, authorCount }
//
// Manual test (browser DevTools console, on any BookTracker page):
//   await catalogCache.init();
//   await catalogCache.getMeta();
//   await catalogCache.lookupByIsbn('9780553287899');
//   await catalogCache.searchAuthors('asimov', 5);
//   await catalogCache.refresh();

(function () {
    const DB_NAME = 'booktracker-catalog';
    const DB_VERSION = 1;
    const STORE_BOOKS = 'books';
    const STORE_AUTHORS = 'authors';
    const STORE_META = 'meta';
    const SNAPSHOT_URL = '/api/catalog-snapshot';

    let dbInstance = null;

    function openDb() {
        if (dbInstance) return Promise.resolve(dbInstance);
        return new Promise((resolve, reject) => {
            const req = indexedDB.open(DB_NAME, DB_VERSION);
            req.onerror = () => reject(req.error);
            req.onupgradeneeded = (e) => {
                const db = e.target.result;
                // Create stores + indexes for v1. Future schema changes
                // bump DB_VERSION and add migration logic here.
                if (!db.objectStoreNames.contains(STORE_BOOKS)) {
                    const books = db.createObjectStore(STORE_BOOKS, { keyPath: 'id' });
                    books.createIndex('isbns', 'isbns', { multiEntry: true, unique: false });
                    books.createIndex('primaryAuthor', 'primaryAuthor', { unique: false });
                }
                if (!db.objectStoreNames.contains(STORE_AUTHORS)) {
                    const authors = db.createObjectStore(STORE_AUTHORS, { keyPath: 'id' });
                    authors.createIndex('canonicalId', 'canonicalId', { unique: false });
                }
                if (!db.objectStoreNames.contains(STORE_META)) {
                    db.createObjectStore(STORE_META, { keyPath: 'key' });
                }
            };
            req.onsuccess = () => {
                dbInstance = req.result;
                resolve(dbInstance);
            };
        });
    }

    function getAll(db, storeName) {
        return new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, 'readonly');
            const req = tx.objectStore(storeName).getAll();
            req.onsuccess = () => resolve(req.result || []);
            req.onerror = () => reject(req.error);
        });
    }

    async function init() {
        await openDb();
        const meta = await getMeta();
        if (!meta || !meta.syncedAt) {
            // First-time setup: try to populate. Don't fail loudly if
            // offline — caller will see empty results until next refresh.
            try {
                await refresh();
            } catch (err) {
                console.warn('[catalog-cache] Initial fetch failed (likely offline):', err);
            }
        }
    }

    async function refresh() {
        const response = await fetch(SNAPSHOT_URL, { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error(`Catalog snapshot fetch failed: ${response.status} ${response.statusText}`);
        }
        const snapshot = await response.json();
        await populate(snapshot);
        return await getMeta();
    }

    function populate(snapshot) {
        return new Promise(async (resolve, reject) => {
            const db = await openDb();
            const tx = db.transaction([STORE_BOOKS, STORE_AUTHORS, STORE_META], 'readwrite');
            tx.oncomplete = () => resolve();
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error || new Error('Transaction aborted'));

            // Wipe existing rows so a shrink (e.g. Drew deletes a book)
            // doesn't leave stale entries. Snapshot is the source of truth.
            tx.objectStore(STORE_BOOKS).clear();
            tx.objectStore(STORE_AUTHORS).clear();
            tx.objectStore(STORE_META).clear();

            const booksStore = tx.objectStore(STORE_BOOKS);
            for (const book of snapshot.books || []) {
                booksStore.put(book);
            }

            const authorsStore = tx.objectStore(STORE_AUTHORS);
            for (const author of snapshot.authors || []) {
                authorsStore.put(author);
            }

            const metaStore = tx.objectStore(STORE_META);
            metaStore.put({ key: 'version', value: snapshot.version || null });
            metaStore.put({ key: 'syncedAt', value: snapshot.syncedAt || new Date().toISOString() });
            metaStore.put({ key: 'bookCount', value: (snapshot.books || []).length });
            metaStore.put({ key: 'authorCount', value: (snapshot.authors || []).length });
        });
    }

    async function lookupByIsbn(isbn) {
        if (!isbn) return null;
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE_BOOKS, 'readonly');
            const idx = tx.objectStore(STORE_BOOKS).index('isbns');
            const req = idx.get(isbn);
            req.onsuccess = () => resolve(req.result || null);
            req.onerror = () => reject(req.error);
        });
    }

    async function lookupByAuthor(canonicalId) {
        if (canonicalId == null) return [];
        const db = await openDb();

        // Names to match against book.allAuthors: the canonical's own
        // name plus every alias's name. Walks the authors store once.
        const allAuthors = await getAll(db, STORE_AUTHORS);
        const matchNames = new Set(
            allAuthors
                .filter(a => a.canonicalId === canonicalId)
                .map(a => a.name)
        );
        if (matchNames.size === 0) return [];

        // Walk books, intersect on author names. At the 3000+ books target,
        // this is a low-millisecond JS filter — well below the 200ms
        // bookshop-mode lookup budget. If profiling ever shows this on the
        // critical path, switch to storing canonical-id arrays per book +
        // a multiEntry index.
        const allBooks = await getAll(db, STORE_BOOKS);
        return allBooks
            .filter(b => Array.isArray(b.allAuthors) && b.allAuthors.some(n => matchNames.has(n)))
            .sort((a, b) => a.title.localeCompare(b.title));
    }

    async function searchAuthors(query, limit) {
        const cap = (typeof limit === 'number' && limit > 0) ? limit : 20;
        if (!query || !query.trim()) return [];
        const lowerQuery = query.trim().toLowerCase();

        const db = await openDb();
        const allAuthors = await getAll(db, STORE_AUTHORS);

        // Match by substring on the typed name (canonical OR alias).
        // A "Bachman" search hits the alias row, which we resolve to King.
        const matches = allAuthors.filter(a =>
            typeof a.name === 'string' && a.name.toLowerCase().includes(lowerQuery)
        );

        // Resolve each hit to its canonical row, dedupe by canonicalId.
        const seenCanonical = new Set();
        const result = [];
        for (const match of matches) {
            if (seenCanonical.has(match.canonicalId)) continue;
            seenCanonical.add(match.canonicalId);
            const canonical = (match.id === match.canonicalId)
                ? match
                : allAuthors.find(a => a.id === match.canonicalId);
            if (canonical) result.push(canonical);
            if (result.length >= cap) break;
        }
        return result.sort((a, b) => a.name.localeCompare(b.name));
    }

    async function getMeta() {
        const db = await openDb();
        const all = await getAll(db, STORE_META);
        if (all.length === 0) return null;
        const lookup = name => {
            const row = all.find(x => x.key === name);
            return row ? row.value : null;
        };
        return {
            version: lookup('version'),
            syncedAt: lookup('syncedAt'),
            bookCount: lookup('bookCount') || 0,
            authorCount: lookup('authorCount') || 0,
        };
    }

    window.catalogCache = {
        init,
        refresh,
        lookupByIsbn,
        lookupByAuthor,
        searchAuthors,
        getMeta,
    };
})();
