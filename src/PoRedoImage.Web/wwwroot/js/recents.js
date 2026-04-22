// recentsManager — persists recent images in localStorage for the RecentImages component.
window.recentsManager = (function () {
    const KEY = 'poRedoImage_recents';
    const MAX = 10;

    function load() {
        try {
            return JSON.parse(localStorage.getItem(KEY) || '[]');
        } catch {
            return [];
        }
    }

    function persist(items) {
        try {
            localStorage.setItem(KEY, JSON.stringify(items));
        } catch { /* storage full — ignore */ }
    }

    return {
        save: function (dataUrl, fileName) {
            const items = load().filter(i => i.fileName !== fileName);
            items.unshift({ dataUrl, fileName, savedAt: new Date().toISOString() });
            persist(items.slice(0, MAX));
        },
        get: function () {
            return load();
        },
        remove: function (fileName) {
            persist(load().filter(i => i.fileName !== fileName));
        },
        clear: function () {
            localStorage.removeItem(KEY);
        }
    };
})();
