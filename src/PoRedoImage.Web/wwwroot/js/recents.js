// localStorage-based recent images manager for PoRedoImage.
// Saves up to 8 recently processed images (resized to 800 px max) with 80 px thumbnails.
// Items auto-expire after 30 days.

const RECENTS_KEY = 'pori_recents_v1';
const MAX_RECENTS = 8;
const MAX_AGE_MS = 30 * 24 * 60 * 60 * 1000;

async function _resizeDataUrl(dataUrl, maxPx, quality) {
    return new Promise((resolve) => {
        const img = new Image();
        img.onload = () => {
            const scale = Math.min(1, maxPx / Math.max(img.width, img.height));
            const canvas = document.createElement('canvas');
            canvas.width = Math.max(1, Math.round(img.width * scale));
            canvas.height = Math.max(1, Math.round(img.height * scale));
            canvas.getContext('2d').drawImage(img, 0, 0, canvas.width, canvas.height);
            resolve(canvas.toDataURL('image/jpeg', quality));
        };
        img.onerror = () => resolve(null);
        img.src = dataUrl;
    });
}

window.recentsManager = {
    async save(previewUrl, fileName) {
        try {
            const storedUrl = await _resizeDataUrl(previewUrl, 800, 0.85);
            const thumbUrl = await _resizeDataUrl(previewUrl, 80, 0.7);
            if (!storedUrl || !thumbUrl) return;

            let items = this._load();
            // Deduplicate by fileName
            items = items.filter(i => i.fileName !== fileName);
            items.unshift({ storedUrl, thumbUrl, fileName, savedAt: Date.now() });
            items = items.slice(0, MAX_RECENTS);

            try {
                localStorage.setItem(RECENTS_KEY, JSON.stringify(items));
            } catch (e) {
                // Storage full — keep only the 3 most recent
                items = items.slice(0, 3);
                try { localStorage.setItem(RECENTS_KEY, JSON.stringify(items)); } catch (_) { }
            }
        } catch (e) {
            console.warn('recentsManager.save failed:', e);
        }
    },

    get() {
        const cutoff = Date.now() - MAX_AGE_MS;
        return this._load().filter(i => i.savedAt > cutoff);
    },

    _load() {
        try { return JSON.parse(localStorage.getItem(RECENTS_KEY) || '[]'); }
        catch { return []; }
    }
};
