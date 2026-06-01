// GUEST LocalStorage persistence — Rule 6
// Persists GUEST identity across browser refreshes and E2E tests.
// In production, the GUEST login button is hidden/disabled via programmatic check.
window.poRedoImageGuest = {
    _key: 'PoRedoImage_GuestSession',

    saveGuestId(guestId) {
        try {
            localStorage.setItem(this._key, guestId);
        } catch (e) {
            console.warn('Guest session save failed:', e);
        }
    },

    loadGuestId() {
        try {
            return localStorage.getItem(this._key);
        } catch (e) {
            return null;
        }
    },

    clearGuestId() {
        try {
            localStorage.removeItem(this._key);
        } catch (e) {
            console.warn('Guest session clear failed:', e);
        }
    },

    // Returns true if running in Azure Production (not localhost)
    isProduction() {
        return window.location.hostname !== 'localhost' 
            && window.location.hostname !== '127.0.0.1'
            && !window.location.hostname.includes('.local');
    },

    // Hide GUEST login button in production — called on login page load
    hideGuestButtonInProduction() {
        if (this.isProduction()) {
            const guestBtn = document.querySelector('.btn-anon-signin');
            if (guestBtn) {
                guestBtn.style.display = 'none';
                guestBtn.setAttribute('disabled', 'disabled');
            }
        }
    },

    // Parse guestId from URL query params and persist to LocalStorage.
    // Called on every page load to capture the redirect from /dev-login.
    persistGuestFromUrl() {
        const params = new URLSearchParams(window.location.search);
        const guestId = params.get('guestId');
        if (guestId && guestId.startsWith('GUEST')) {
            this.saveGuestId(guestId);
            // Clean the URL without triggering a reload
            const url = new URL(window.location);
            url.searchParams.delete('guestId');
            window.history.replaceState({}, '', url);
        }
    },

    // Initialize on page load
    init() {
        this.persistGuestFromUrl();
    }
};

// Auto-run on DOM ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => window.poRedoImageGuest.init());
} else {
    window.poRedoImageGuest.init();
}
