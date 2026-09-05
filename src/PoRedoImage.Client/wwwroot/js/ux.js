// poUx — small, dependency-free UX helpers shared by the feature pages.
//
//   * Image intake  : clipboard paste (Ctrl+V) and drop-anywhere, both funnelled through the
//                     same validation the C# ImageLoadHelper applies to <InputFile> uploads.
//   * Share         : Web Share API (level 2, files) with a clipboard-image fallback.
//   * Zip           : STORE-method (no deflate) ZIP writer. PNG/JPEG are already compressed,
//                     so storing costs nothing and keeps this file dependency-free.
//   * Prompt store  : localStorage-backed recent/pinned prompt list.
window.poUx = (function () {
    'use strict';

    // Mirrors ImageLoadHelper.MaxFileSize / its accepted content types. Kept in sync by hand —
    // there are only two constants and duplicating them avoids a round trip on every paste.
    const MAX_BYTES = 20 * 1024 * 1024;
    const ALLOWED = ['image/jpeg', 'image/png'];

    // ── Image intake ────────────────────────────────────────────────────────
    let intake = null;   // { ref, onPaste, onDragOver, onDragLeave, onDrop }
    let dragDepth = 0;   // dragenter/dragleave fire per child element; count to avoid flicker

    function extensionFor(type) {
        return type === 'image/png' ? 'png' : 'jpg';
    }

    function readImage(file) {
        return new Promise(function (resolve) {
            if (!file) { resolve(null); return; }
            if (ALLOWED.indexOf(file.type) === -1) {
                resolve({ error: 'Only JPG and PNG images are supported.' });
                return;
            }
            if (file.size > MAX_BYTES) {
                const mb = Math.round((file.size / 1024 / 1024) * 100) / 100;
                resolve({ error: 'File size exceeds the maximum allowed (20 MB). Current: ' + mb + ' MB' });
                return;
            }
            const reader = new FileReader();
            reader.onload = function () {
                const url = String(reader.result);
                const comma = url.indexOf(',');
                if (comma < 0) { resolve({ error: 'Could not read the image.' }); return; }
                resolve({
                    base64: url.slice(comma + 1),
                    contentType: file.type,
                    // Pasted screenshots arrive as "image.png" or with no name at all.
                    fileName: file.name || ('pasted-image.' + extensionFor(file.type)),
                    error: null
                });
            };
            reader.onerror = function () { resolve({ error: 'Could not read the image.' }); };
            reader.readAsDataURL(file);
        });
    }

    async function push(file, source) {
        const payload = await readImage(file);
        if (!payload || !intake) return;
        payload.source = source;
        try { await intake.ref.invokeMethodAsync('AcceptIntake', payload); } catch { /* component gone */ }
    }

    function firstImageFrom(list) {
        if (!list) return null;
        for (let i = 0; i < list.length; i++) {
            const item = list[i];
            // DataTransferItemList entries expose .kind; FileList entries do not.
            if (item.kind !== undefined) {
                if (item.kind === 'file') {
                    const f = item.getAsFile();
                    if (f && f.type.indexOf('image/') === 0) return f;
                }
            } else if (item.type && item.type.indexOf('image/') === 0) {
                return item;
            }
        }
        return null;
    }

    function setDragOverlay(on) {
        document.body.classList.toggle('po-drop-active', on);
    }

    return {
        // Registers window-level paste + drop handlers that call back into `ref`
        // (`[JSInvokable] OnImageIntake`). Idempotent — a second call replaces the first,
        // so only the most recently rendered upload panel owns the listeners.
        registerIntake: function (ref) {
            this.unregisterIntake();

            const onPaste = function (e) {
                // Never hijack a paste aimed at a text field — prompt textareas live on the
                // same pages as the upload panel.
                const t = e.target;
                if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
                const file = firstImageFrom(e.clipboardData && e.clipboardData.items);
                if (!file) return;
                e.preventDefault();
                push(file, 'paste');
            };

            const onDragOver = function (e) {
                if (!e.dataTransfer) return;
                const types = e.dataTransfer.types;
                if (!types || Array.prototype.indexOf.call(types, 'Files') === -1) return;
                e.preventDefault();
                if (dragDepth === 0) setDragOverlay(true);
                dragDepth = 1;
            };

            const onDragLeave = function (e) {
                // relatedTarget === null means the pointer left the window entirely.
                if (e.relatedTarget === null) { dragDepth = 0; setDragOverlay(false); }
            };

            const onDrop = function (e) {
                dragDepth = 0;
                setDragOverlay(false);
                if (!e.dataTransfer) return;
                // A drop landing on the panel's own <InputFile> overlay is handled natively by
                // Blazor's OnChange; intercepting it here would load the image twice.
                if (e.target && e.target.closest && e.target.closest('.drop-zone')) return;
                const file = firstImageFrom(e.dataTransfer.files) || firstImageFrom(e.dataTransfer.items);
                if (!file) return;
                e.preventDefault();
                push(file, 'drop');
            };

            window.addEventListener('paste', onPaste);
            window.addEventListener('dragover', onDragOver);
            window.addEventListener('dragleave', onDragLeave);
            window.addEventListener('drop', onDrop);
            intake = { ref: ref, onPaste: onPaste, onDragOver: onDragOver, onDragLeave: onDragLeave, onDrop: onDrop };
        },

        unregisterIntake: function () {
            if (!intake) return;
            window.removeEventListener('paste', intake.onPaste);
            window.removeEventListener('dragover', intake.onDragOver);
            window.removeEventListener('dragleave', intake.onDragLeave);
            window.removeEventListener('drop', intake.onDrop);
            setDragOverlay(false);
            dragDepth = 0;
            intake = null;
        },

        // ── Share ───────────────────────────────────────────────────────────
        // Returns one of: 'shared' | 'copied' | 'cancelled' | 'unsupported' | 'failed'.
        // ── Zip ─────────────────────────────────────────────────────────────
        // files: [{ name, url }]. Packs them uncompressed and triggers a download.
        // Returns the number of entries actually written.
        downloadZip: async function (files, zipName) {
            const entries = [];
            for (const f of (files || [])) {
                try {
                    const res = await fetch(f.url);
                    if (!res.ok) continue;
                    const buf = new Uint8Array(await res.arrayBuffer());
                    entries.push({ name: f.name, data: buf });
                } catch { /* skip this entry, keep the rest of the archive */ }
            }
            if (entries.length === 0) return 0;

            const blob = buildZip(entries);
            const blobUrl = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = blobUrl;
            a.download = zipName || 'images.zip';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(blobUrl);
            return entries.length;
        },

        // ── Prompt history (localStorage) ────────────────────────────────────
        loadPromptHistory: function () {
            try { return localStorage.getItem('poRedoImage_promptHistory'); } catch { return null; }
        },
        savePromptHistory: function (json) {
            try { localStorage.setItem('poRedoImage_promptHistory', json); } catch { /* quota — non-fatal */ }
        },

        // ── Direct Clipboard Copy ────────────────────────────────────────────
        copyImageToClipboard: async function (url) {
            try {
                let blob;
                if (url.startsWith('data:')) {
                    const res = await fetch(url);
                    blob = await res.blob();
                } else {
                    const res = await fetch(url);
                    if (!res.ok) return 'failed';
                    blob = await res.blob();
                }
                if (navigator.clipboard && window.ClipboardItem) {
                    const png = blob.type === 'image/png' ? blob : await toPng(blob);
                    if (png) {
                        await navigator.clipboard.write([new ClipboardItem({ 'image/png': png })]);
                        return 'copied';
                    }
                }
                return 'unsupported';
            } catch {
                return 'failed';
            }
        },
    };

    // ── Internals ───────────────────────────────────────────────────────────

    function toPng(blob) {
        return new Promise(function (resolve) {
            const img = new Image();
            const objUrl = URL.createObjectURL(blob);
            img.onload = function () {
                const canvas = document.createElement('canvas');
                canvas.width = img.naturalWidth;
                canvas.height = img.naturalHeight;
                canvas.getContext('2d').drawImage(img, 0, 0);
                URL.revokeObjectURL(objUrl);
                canvas.toBlob(resolve, 'image/png');
            };
            img.onerror = function () { URL.revokeObjectURL(objUrl); resolve(null); };
            img.src = objUrl;
        });
    }

    const CRC_TABLE = (function () {
        const table = new Uint32Array(256);
        for (let i = 0; i < 256; i++) {
            let c = i;
            for (let k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
            table[i] = c >>> 0;
        }
        return table;
    })();

    function crc32(bytes) {
        let c = 0xFFFFFFFF;
        for (let i = 0; i < bytes.length; i++) c = CRC_TABLE[(c ^ bytes[i]) & 0xFF] ^ (c >>> 8);
        return (c ^ 0xFFFFFFFF) >>> 0;
    }

    // Builds a STORE-method (method 0) ZIP. No deflate: the payloads are PNG/JPEG, which
    // deflate cannot meaningfully shrink, so this trades ~0 bytes for ~0 dependencies.
    function buildZip(entries) {
        const enc = new TextEncoder();
        const now = new Date();
        const dosTime = (now.getHours() << 11) | (now.getMinutes() << 5) | (now.getSeconds() >> 1);
        const dosDate = ((now.getFullYear() - 1980) << 9) | ((now.getMonth() + 1) << 5) | now.getDate();

        const locals = [];
        const centrals = [];
        let offset = 0;

        for (const e of entries) {
            const nameBytes = enc.encode(e.name);
            const crc = crc32(e.data);

            const local = new Uint8Array(30 + nameBytes.length);
            const lv = new DataView(local.buffer);
            lv.setUint32(0, 0x04034b50, true);   // local file header signature
            lv.setUint16(4, 20, true);           // version needed
            lv.setUint16(6, 0, true);            // flags
            lv.setUint16(8, 0, true);            // method = store
            lv.setUint16(10, dosTime, true);
            lv.setUint16(12, dosDate, true);
            lv.setUint32(14, crc, true);
            lv.setUint32(18, e.data.length, true);
            lv.setUint32(22, e.data.length, true);
            lv.setUint16(26, nameBytes.length, true);
            lv.setUint16(28, 0, true);           // extra field length
            local.set(nameBytes, 30);

            const central = new Uint8Array(46 + nameBytes.length);
            const cv = new DataView(central.buffer);
            cv.setUint32(0, 0x02014b50, true);   // central directory signature
            cv.setUint16(4, 20, true);           // version made by
            cv.setUint16(6, 20, true);           // version needed
            cv.setUint16(8, 0, true);
            cv.setUint16(10, 0, true);
            cv.setUint16(12, dosTime, true);
            cv.setUint16(14, dosDate, true);
            cv.setUint32(16, crc, true);
            cv.setUint32(20, e.data.length, true);
            cv.setUint32(24, e.data.length, true);
            cv.setUint16(28, nameBytes.length, true);
            cv.setUint16(30, 0, true);           // extra
            cv.setUint16(32, 0, true);           // comment
            cv.setUint16(34, 0, true);           // disk number start
            cv.setUint16(36, 0, true);           // internal attrs
            cv.setUint32(38, 0, true);           // external attrs
            cv.setUint32(42, offset, true);      // relative offset of local header
            central.set(nameBytes, 46);

            locals.push(local, e.data);
            centrals.push(central);
            offset += local.length + e.data.length;
        }

        const centralSize = centrals.reduce(function (n, c) { return n + c.length; }, 0);
        const end = new Uint8Array(22);
        const ev = new DataView(end.buffer);
        ev.setUint32(0, 0x06054b50, true);       // end of central directory
        ev.setUint16(4, 0, true);
        ev.setUint16(6, 0, true);
        ev.setUint16(8, entries.length, true);
        ev.setUint16(10, entries.length, true);
        ev.setUint32(12, centralSize, true);
        ev.setUint32(16, offset, true);
        ev.setUint16(20, 0, true);

        return new Blob(locals.concat(centrals, [end]), { type: 'application/zip' });
    }
})();

// ── Textarea autosize ───────────────────────────────────────────────────────
// Any <textarea data-autosize> grows to fit its content, so it never shows its own
// scrollbar nested inside a panel that already scrolls (the prompt drawer stacked ten of
// them). Deliberately standalone rather than a poUx method invoked from C#: the drawer is
// rendered by Blazor with no IJSRuntime of its own, so this listens to the DOM instead of
// needing an interop call at exactly the right point in the render cycle.
//
// Browsers with CSS `field-sizing: content` already do this natively; the sizing below is
// idempotent and simply agrees with them.
(function () {
    'use strict';

    function fit(el) {
        if (!el || el.tagName !== 'TEXTAREA') return;
        // Reset first: without it the height only ever ratchets upward as text is deleted.
        el.style.height = 'auto';
        el.style.height = el.scrollHeight + 'px';
    }

    function fitAll(root) {
        (root || document).querySelectorAll('textarea[data-autosize]').forEach(fit);
    }

    // Typing. Capture phase so it still fires for elements added after this listener.
    document.addEventListener('input', function (e) {
        if (e.target && e.target.matches && e.target.matches('textarea[data-autosize]')) fit(e.target);
    }, true);

    // Blazor renders the drawer long after load, and sets `value` without firing `input`.
    if (typeof MutationObserver === 'function') {
        new MutationObserver(function (records) {
            for (const r of records) {
                for (const n of r.addedNodes) {
                    if (n.nodeType !== 1) continue;
                    if (n.matches && n.matches('textarea[data-autosize]')) fit(n);
                    else fitAll(n);
                }
            }
        }).observe(document.documentElement, { childList: true, subtree: true });
    }

    // A drawer can be re-shown without new nodes, and fonts land after first paint.
    window.addEventListener('load', function () { fitAll(); });
    document.addEventListener('transitionend', function (e) {
        if (e.target && e.target.classList && e.target.classList.contains('prompt-drawer')) fitAll(e.target);
    });
})();
