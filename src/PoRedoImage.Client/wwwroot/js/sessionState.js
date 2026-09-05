// bulkStateManager — persists bulk-generate results across Blazor SignalR circuit reconnections.
window.bulkStateManager = (function () {
    const KEY = 'poRedoImage_bulkState';

    return {
        save: function (json) {
            try { localStorage.setItem(KEY, json); } catch { /* quota exceeded — non-fatal */ }
        },
        load: function () {
            return localStorage.getItem(KEY); // returns null if not set
        },
        clear: function () {
            try { localStorage.removeItem(KEY); } catch {}
        }
    };
})();

// imageSession — persists the active image across Blazor circuit reconnects (full-page navigations).
// Uses sessionStorage so the image is cleared when the browser tab closes.
window.imageSession = (function () {
    const KEY = 'poRedoImage_activeImage';

    return {
        save: function (dataUrl, fileName, contentType) {
            try {
                sessionStorage.setItem(KEY, JSON.stringify({ dataUrl, fileName, contentType }));
            } catch { /* quota exceeded (large image) — ignore */ }
        },
        load: function () {
            try {
                const raw = sessionStorage.getItem(KEY);
                return raw ? JSON.parse(raw) : null;
            } catch {
                return null;
            }
        },
        clear: function () {
            try { sessionStorage.removeItem(KEY); } catch {}
        }
    };
})();