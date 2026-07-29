// Browser performance collector for the diagnostics dashboard.
//
// Measures four things the .NET side cannot see, and nothing else:
//   loadMs              navigation start -> load          (includes the WASM boot)
//   domContentLoadedMs  navigation start -> DOMContentLoaded
//   cls                 Cumulative Layout Shift, excluding shifts within 500ms of user input
//   jsHeapMb / wasmHeapMb  memory, where the browser exposes it
//
// The CLS observer is installed as early as this script is parsed (it sits before
// blazor.webassembly.js in index.html) because layout-shift entries are NOT buffered
// retroactively past the observer's registration for the pre-observer window in every
// engine — installing late silently under-reports the boot-time shift, which is exactly
// the shift a WASM app most needs to measure.
window.poRedoImageVitals = (function () {
    'use strict';

    var clsValue = 0;
    var clsSupported = false;

    try {
        if (typeof PerformanceObserver === 'function' &&
            PerformanceObserver.supportedEntryTypes &&
            PerformanceObserver.supportedEntryTypes.indexOf('layout-shift') !== -1) {

            var observer = new PerformanceObserver(function (list) {
                var entries = list.getEntries();
                for (var i = 0; i < entries.length; i++) {
                    // Shifts that follow user input within 500ms are expected (an accordion
                    // opening is not a layout bug) and are excluded from the CLS definition.
                    if (!entries[i].hadRecentInput) clsValue += entries[i].value;
                }
            });
            observer.observe({ type: 'layout-shift', buffered: true });
            clsSupported = true;
        }
    } catch (e) {
        // Any failure here leaves clsSupported false; collect() then reports 0 and the
        // caller can tell the difference via `clsSupported`.
    }

    function navigationTiming() {
        try {
            var nav = performance.getEntriesByType('navigation')[0];
            if (nav) {
                return {
                    // loadEventEnd is 0 while the load event is still in flight; the caller
                    // waits for `load` before collecting, so this is populated by then.
                    loadMs: Math.max(0, Math.round(nav.loadEventEnd || nav.duration || 0)),
                    domContentLoadedMs: Math.max(0, Math.round(nav.domContentLoadedEventEnd || 0))
                };
            }
        } catch (e) { /* fall through */ }
        return { loadMs: 0, domContentLoadedMs: 0 };
    }

    function jsHeapMb() {
        // performance.memory is non-standard and Chromium-only. Returning null rather than 0
        // elsewhere keeps "not measurable here" distinct from "measured as zero".
        try {
            if (performance.memory && typeof performance.memory.usedJSHeapSize === 'number') {
                return +(performance.memory.usedJSHeapSize / 1048576).toFixed(2);
            }
        } catch (e) { /* ignore */ }
        return null;
    }

    function wasmHeapMb() {
        // The .NET WASM linear memory — the real "WASM overhead", which is NOT part of the JS
        // heap figure above. getDotnetRuntime is the documented accessor for the running
        // instance; it is absent until the runtime has started, hence the guard.
        try {
            if (typeof globalThis.getDotnetRuntime !== 'function') return null;
            var runtime = globalThis.getDotnetRuntime(0);
            var buffer = runtime && runtime.Module && runtime.Module.HEAPU8 && runtime.Module.HEAPU8.buffer;
            if (buffer && typeof buffer.byteLength === 'number') {
                return +(buffer.byteLength / 1048576).toFixed(2);
            }
        } catch (e) { /* ignore */ }
        return null;
    }

    function afterLoad() {
        // Resolve once the load event has fired, so loadEventEnd is populated.
        return new Promise(function (resolve) {
            if (document.readyState === 'complete') resolve();
            else window.addEventListener('load', function () { resolve(); }, { once: true });
        });
    }

    return {
        /**
         * Waits for the load event, lets layout settle for `settleMs`, then returns one sample.
         * Never rejects — a failure returns nulls rather than breaking the caller.
         */
        collect: async function (settleMs) {
            try {
                // Captured FIRST, before any waiting. The caller is .NET code running after the
                // component's first render, so this is navigation start -> Blazor interactive.
                //
                // This is the number that actually matters for a WebAssembly app, and it is NOT
                // loadEventEnd: blazor.web.js downloads and starts the runtime asynchronously,
                // so the document's load event fires long before the app can do anything. On this
                // app loadEventEnd lands around 30ms while the runtime is still booting.
                var interactiveMs = Math.max(0, Math.round(performance.now()));

                await afterLoad();
                await new Promise(function (r) { setTimeout(r, settleMs > 0 ? settleMs : 0); });

                var timing = navigationTiming();
                return {
                    route: location.pathname || '/',
                    interactiveMs: interactiveMs,
                    loadMs: timing.loadMs,
                    domContentLoadedMs: timing.domContentLoadedMs,
                    cls: +clsValue.toFixed(4),
                    clsSupported: clsSupported,
                    jsHeapMb: jsHeapMb(),
                    wasmHeapMb: wasmHeapMb()
                };
            } catch (e) {
                return null;
            }
        }
    };
})();
