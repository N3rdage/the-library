// Small JS helper for scrolling a specific element into view. Currently
// used by the Authors page to jump to an author pre-expanded via
// /authors?expand=<id>, but kept generic so future pages that support
// deep-link-to-row (Series, Library groupings, etc.) can reuse it.
window.ScrollTo = {
    element: function (id) {
        const el = document.getElementById(id);
        if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};
