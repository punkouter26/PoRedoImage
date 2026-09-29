// PoRedoImage service worker. It exists for the Web Share Target only and deliberately caches
// nothing else: every screen depends on the authenticated API, so an offline shell would load
// and then fail on its first request — worse than the browser's own offline page.
//
// A photo shared into the installed app arrives as a multipart POST to /share-target. The server
// never sees it: this worker stashes the file in Cache Storage and redirects to /?shared=1, where
// poUx (wwwroot/js/ux.js) feeds it into the normal paste/drop intake.
'use strict';

self.addEventListener('install', function () { self.skipWaiting(); });
self.addEventListener('activate', function (e) { e.waitUntil(self.clients.claim()); });

self.addEventListener('fetch', function (event) {
    const url = new URL(event.request.url);
    if (event.request.method !== 'POST' || url.pathname !== '/share-target') return;

    event.respondWith((async function () {
        try {
            const file = (await event.request.formData()).get('image');
            if (file && typeof file !== 'string') {
                const cache = await caches.open('po-share');
                await cache.put('/shared-image', new Response(file, {
                    headers: { 'Content-Type': file.type, 'X-File-Name': encodeURIComponent(file.name || '') }
                }));
            }
        } catch { /* unreadable share — land on the Studio anyway so the user can upload */ }
        return Response.redirect('/?shared=1', 303);
    })());
});
