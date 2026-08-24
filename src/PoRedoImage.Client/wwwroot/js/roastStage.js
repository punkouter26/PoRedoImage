// roastStage.js — the Rap Roast performance surface.
//
//   * Karaoke  : highlights the bar currently being performed and scrolls it into view.
//                Timings are ESTIMATES computed in C# (RoastScript) — Lyria returns audio with no
//                alignment data — so a manual sync nudge is part of the contract, not a workaround.
//   * Export   : composes photo + active bar + branding on a canvas, then either records it to a
//                WebM with the track's own audio, or snapshots a single frame to PNG.
//
// Follows the js/ux.js house style: one global object, no modules, no build step, and every
// browser-capability difference handled by feature detection rather than by user agent.
window.poRoast = (function () {

    // ── Karaoke ─────────────────────────────────────────────────────────────

    let session = null;

    // createMediaElementSource() throws InvalidStateError if called twice for the same element, and
    // there is no API to ask whether one already exists — so the graph is remembered per element.
    // Once an element is routed through a context it stays routed, which is why the source is also
    // connected straight to ctx.destination: without that the user would never hear the track again
    // after a single export.
    const audioGraphs = new WeakMap();

    function prefersReducedMotion() {
        return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    }

    /** Resolves once the element knows its duration, or false if it never will. */
    function whenDurationKnown(audio) {
        return new Promise(function (resolve) {
            if (isUsableDuration(audio.duration)) { resolve(true); return; }

            let settled = false;
            const done = function (ok) {
                if (settled) return;
                settled = true;
                audio.removeEventListener('loadedmetadata', onMeta);
                audio.removeEventListener('durationchange', onMeta);
                audio.removeEventListener('error', onFail);
                clearTimeout(timer);
                resolve(ok);
            };
            const onMeta = function () { if (isUsableDuration(audio.duration)) done(true); };
            const onFail = function () { done(false); };

            audio.addEventListener('loadedmetadata', onMeta);
            audio.addEventListener('durationchange', onMeta);
            audio.addEventListener('error', onFail);
            // A data: URL that never produces metadata would otherwise leave the karaoke view
            // waiting forever on a promise nothing resolves.
            const timer = setTimeout(function () { done(false); }, 8000);
        });
    }

    // A track shorter than this is not a performance — it is the silent placeholder the mock music
    // service returns. Highlighting twelve bars across 26 milliseconds is worse than not trying.
    const MIN_TRACK_SECONDS = 3;

    function isUsableDuration(d) {
        return typeof d === 'number' && isFinite(d) && d >= MIN_TRACK_SECONDS;
    }

    function activeIndexAt(seconds, lines) {
        // Linear scan: a roast is a dozen lines, so a binary search would be more code than work.
        let active = -1;
        for (let i = 0; i < lines.length; i++) {
            const l = lines[i];
            if (l.isSection) continue;
            if (seconds >= l.start && seconds < l.end) return i;
            if (seconds >= l.end) active = i;
        }
        return active;
    }

    function paint() {
        if (!session) return;
        const { audio, container, lines, duration } = session;
        const t = audio.currentTime - session.offset;
        const idx = t < 0 ? -1 : activeIndexAt(t, lines);

        if (idx !== session.lastIndex) {
            session.lastIndex = idx;
            const nodes = container.querySelectorAll('[data-roast-line]');
            nodes.forEach(function (node) {
                const i = Number(node.getAttribute('data-roast-line'));
                node.classList.toggle('is-active', i === idx);
                node.classList.toggle('is-past', idx >= 0 && i < idx);
            });
            const activeNode = idx >= 0 ? container.querySelector('[data-roast-line="' + idx + '"]') : null;
            if (activeNode && container.scrollHeight > container.clientHeight) {
                const target = activeNode.offsetTop - (container.clientHeight / 2) + (activeNode.offsetHeight / 2);
                container.scrollTo({ top: Math.max(0, target), behavior: prefersReducedMotion() ? 'auto' : 'smooth' });
            }
        }

        const pct = duration > 0 ? Math.min(100, Math.max(0, (audio.currentTime / duration) * 100)) : 0;
        container.style.setProperty('--roast-progress', pct.toFixed(2) + '%');
    }

    function tick() {
        if (!session) return;
        paint();
        session.raf = requestAnimationFrame(tick);
    }

    function startLoop() {
        if (!session || session.raf) return;
        session.raf = requestAnimationFrame(tick);
    }

    function stopLoop() {
        if (!session || !session.raf) return;
        cancelAnimationFrame(session.raf);
        session.raf = 0;
        paint(); // one final settle so a pause leaves the correct bar lit
    }

    // ── Canvas composition (shared by the PNG card and the WebM recording) ───

    const CARD = 1080;
    const PAD = 64;
    const PHOTO_H = 520;

    function loadImage(src) {
        return new Promise(function (resolve) {
            const img = new Image();
            img.onload = function () { resolve(img); };
            img.onerror = function () { resolve(null); };
            img.src = src;
        });
    }

    /** Greedy word wrap. Returns at most `maxLines` strings that each fit `maxWidth`. */
    function wrap(ctx, text, maxWidth, maxLines) {
        const words = String(text).split(/\s+/).filter(Boolean);
        const out = [];
        let line = '';
        for (const word of words) {
            const candidate = line ? line + ' ' + word : word;
            if (ctx.measureText(candidate).width <= maxWidth || !line) {
                line = candidate;
            } else {
                out.push(line);
                line = word;
                if (out.length === maxLines) break;
            }
        }
        if (out.length < maxLines && line) out.push(line);
        if (out.length === maxLines && words.length) {
            // Signal the trim rather than silently dropping half a punchline.
            const last = out[maxLines - 1];
            if (ctx.measureText(last).width > maxWidth) out[maxLines - 1] = last.slice(0, -1) + '…';
        }
        return out;
    }

    function drawFrame(ctx, img, lines, idx, progress, meta) {
        ctx.fillStyle = '#0D0D0F';
        ctx.fillRect(0, 0, CARD, CARD);

        // ── Photo, contained inside a steel-framed cell ──
        const cellW = CARD - PAD * 2;
        ctx.fillStyle = '#1B1B1E';
        ctx.fillRect(PAD, PAD, cellW, PHOTO_H);
        if (img && img.naturalWidth) {
            const scale = Math.min(cellW / img.naturalWidth, PHOTO_H / img.naturalHeight);
            const w = img.naturalWidth * scale;
            const h = img.naturalHeight * scale;
            ctx.drawImage(img, PAD + (cellW - w) / 2, PAD + (PHOTO_H - h) / 2, w, h);
        }
        ctx.strokeStyle = '#B6BBC2';
        ctx.lineWidth = 2;
        ctx.strokeRect(PAD + 1, PAD + 1, cellW - 2, PHOTO_H - 2);

        // ── The bars: the one being performed, framed by its neighbours ──
        const sung = lines.filter(function (l) { return !l.isSection; });
        const activeLine = idx >= 0 && lines[idx] ? lines[idx] : (sung[0] || null);
        const pos = activeLine ? sung.indexOf(activeLine) : -1;

        let y = PAD + PHOTO_H + 96;

        ctx.textAlign = 'left';
        ctx.fillStyle = '#868C95';
        ctx.font = '500 30px "Archivo Narrow", Archivo, sans-serif';
        if (pos > 0) ctx.fillText(wrap(ctx, sung[pos - 1].text, cellW, 1)[0] || '', PAD, y);

        y += 78;
        ctx.fillStyle = '#FFB400';
        ctx.font = '700 54px "Archivo Narrow", Archivo, sans-serif';
        const activeWrapped = activeLine ? wrap(ctx, activeLine.text, cellW, 2) : [];
        for (const l of activeWrapped) { ctx.fillText(l, PAD, y); y += 62; }
        if (activeWrapped.length < 2) y += 62;

        y += 20;
        ctx.fillStyle = '#868C95';
        ctx.font = '500 30px "Archivo Narrow", Archivo, sans-serif';
        if (pos >= 0 && pos + 1 < sung.length) ctx.fillText(wrap(ctx, sung[pos + 1].text, cellW, 1)[0] || '', PAD, y);

        // ── Footer: what this is, and what it was cut from ──
        ctx.font = '600 24px "Archivo Narrow", Archivo, sans-serif';
        ctx.fillStyle = '#F2F2F2';
        ctx.fillText('PoRedoImage · Rap Roast', PAD, CARD - PAD - 26);
        ctx.textAlign = 'right';
        ctx.fillStyle = '#868C95';
        ctx.fillText(meta || '', CARD - PAD, CARD - PAD - 26);

        // ── Progress hairline ──
        ctx.fillStyle = '#1B1B1E';
        ctx.fillRect(PAD, CARD - PAD, cellW, 4);
        ctx.fillStyle = '#FFB400';
        ctx.fillRect(PAD, CARD - PAD, cellW * Math.min(1, Math.max(0, progress)), 4);
        ctx.textAlign = 'left';
    }

    async function buildCanvas() {
        // Canvas text falls back to a system face if the webfont has not landed yet, which would
        // silently produce a card in the wrong typeface.
        if (document.fonts && document.fonts.ready) { try { await document.fonts.ready; } catch { /* proceed */ } }
        const canvas = document.createElement('canvas');
        canvas.width = CARD;
        canvas.height = CARD;
        return canvas;
    }

    function downloadBlob(blob, fileName) {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        // Revoking synchronously can cancel the download in some browsers; one turn is enough.
        setTimeout(function () { URL.revokeObjectURL(url); }, 4000);
    }

    function pickMimeType() {
        const candidates = [
            'video/webm;codecs=vp9,opus',
            'video/webm;codecs=vp8,opus',
            'video/webm',
            'video/mp4',
        ];
        for (const type of candidates) {
            if (window.MediaRecorder && MediaRecorder.isTypeSupported(type)) return type;
        }
        return '';
    }

    return {

        /**
         * Binds the karaoke driver to an <audio> element and its lyric list.
         * `lines` are RoastLine records; fractions become seconds once the duration is known.
         * Returns false when the track carries no usable duration — the caller then shows the
         * plain lyric block instead of a karaoke view that could never advance.
         */
        attach: async function (audio, container, lines, offsetSec) {
            this.detach();
            if (!audio || !container || !lines || !lines.length) return false;
            if (!(await whenDurationKnown(audio))) return false;

            const duration = audio.duration;
            session = {
                audio: audio,
                container: container,
                duration: duration,
                offset: offsetSec || 0,
                lastIndex: -2, // -2, not -1: forces the first paint even when nothing is active yet
                raf: 0,
                lines: lines.map(function (l) {
                    return {
                        isSection: l.isSection,
                        text: l.text,
                        start: l.startFraction * duration,
                        end: l.endFraction * duration,
                    };
                }),
                handlers: {},
            };

            session.handlers.play = startLoop;
            session.handlers.pause = stopLoop;
            session.handlers.ended = stopLoop;
            session.handlers.seeked = paint;
            audio.addEventListener('play', session.handlers.play);
            audio.addEventListener('pause', session.handlers.pause);
            audio.addEventListener('ended', session.handlers.ended);
            audio.addEventListener('seeked', session.handlers.seeked);

            paint();
            if (!audio.paused) startLoop();
            return true;
        },

        /** Shifts every line by `sec` — the user's correction to an estimated alignment. */
        setOffset: function (sec) {
            if (!session) return;
            session.offset = sec || 0;
            session.lastIndex = -2;
            paint();
        },

        /** Jumps the track to where line `index` is estimated to begin. */
        seekToLine: function (audio, index) {
            if (!session || !audio) return;
            const line = session.lines[index];
            if (!line || line.isSection) return;
            audio.currentTime = Math.max(0, Math.min(session.duration - 0.05, line.start + session.offset));
            paint();
        },

        detach: function () {
            if (!session) return;
            stopLoop();
            const { audio, handlers } = session;
            audio.removeEventListener('play', handlers.play);
            audio.removeEventListener('pause', handlers.pause);
            audio.removeEventListener('ended', handlers.ended);
            audio.removeEventListener('seeked', handlers.seeked);
            session = null;
        },

        /** True when this browser can record canvas + audio to a file. */
        canRecord: function () {
            return !!(window.MediaRecorder
                && HTMLCanvasElement.prototype.captureStream
                && (window.AudioContext || window.webkitAudioContext)
                && pickMimeType());
        },

        /**
         * Renders one frame — the photo with whichever bar is currently lit — and saves it as PNG.
         * Always available: it needs nothing beyond a 2D canvas, which is why it is the fallback
         * wherever recording is not supported.
         */
        exportCard: async function (imageUrl, lines, activeIndex, meta, fileName) {
            try {
                // -1 means "whatever bar is lit right now". The karaoke session owns that number —
                // asking .NET for it would need a second interop hop to learn something this side
                // already knows.
                if (activeIndex < 0 && session) activeIndex = session.lastIndex;
                const canvas = await buildCanvas();
                const ctx = canvas.getContext('2d');
                const img = await loadImage(imageUrl);
                drawFrame(ctx, img, lines || [], activeIndex, 0, meta);
                const blob = await new Promise(function (r) { canvas.toBlob(r, 'image/png'); });
                if (!blob) return 'failed';
                downloadBlob(blob, fileName || 'rap-roast-card.png');
                return 'saved';
            } catch {
                return 'failed';
            }
        },

        /**
         * Records the animated card with the track's own audio, in real time, and saves the file.
         * Real time is not a shortcut — MediaRecorder captures a live stream, so a 30-second clip
         * takes 30 seconds. Progress is reported to .NET so the page can say so honestly.
         */
        exportVideo: async function (audio, dotNetRef, imageUrl, lines, meta, fileName) {
            if (!this.canRecord()) return 'unsupported';
            if (!audio || !isUsableDuration(audio.duration)) return 'failed';

            let recorder = null;
            let raf = 0;
            try {
                const canvas = await buildCanvas();
                const ctx = canvas.getContext('2d');
                const img = await loadImage(imageUrl);
                const duration = audio.duration;

                const timed = (lines || []).map(function (l) {
                    return { isSection: l.isSection, text: l.text, start: l.startFraction * duration, end: l.endFraction * duration };
                });

                // Route the element's audio into a recordable stream, keeping the speaker path live.
                let graph = audioGraphs.get(audio);
                if (!graph) {
                    const Ctor = window.AudioContext || window.webkitAudioContext;
                    const ctxA = new Ctor();
                    const source = ctxA.createMediaElementSource(audio);
                    const dest = ctxA.createMediaStreamDestination();
                    source.connect(ctxA.destination);
                    source.connect(dest);
                    graph = { ctx: ctxA, dest: dest };
                    audioGraphs.set(audio, graph);
                }
                if (graph.ctx.state === 'suspended') await graph.ctx.resume();

                const stream = new MediaStream([
                    ...canvas.captureStream(30).getVideoTracks(),
                    ...graph.dest.stream.getAudioTracks(),
                ]);

                const chunks = [];
                const mimeType = pickMimeType();
                recorder = new MediaRecorder(stream, { mimeType: mimeType });
                recorder.ondataavailable = function (e) { if (e.data && e.data.size) chunks.push(e.data); };

                const finished = new Promise(function (resolve) { recorder.onstop = resolve; });

                const draw = function () {
                    const t = audio.currentTime;
                    let idx = -1;
                    for (let i = 0; i < timed.length; i++) {
                        const l = timed[i];
                        if (l.isSection) continue;
                        if (t >= l.start && t < l.end) { idx = i; break; }
                        if (t >= l.end) idx = i;
                    }
                    drawFrame(ctx, img, timed, idx, t / duration, meta);
                    if (dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnExportProgress', Math.round((t / duration) * 100))
                            .catch(function () { /* component disposed mid-record — keep recording */ });
                    }
                    raf = requestAnimationFrame(draw);
                };

                audio.currentTime = 0;
                await audio.play();
                recorder.start();
                draw();

                await new Promise(function (resolve) {
                    audio.addEventListener('ended', resolve, { once: true });
                });

                cancelAnimationFrame(raf);
                raf = 0;
                recorder.stop();
                await finished;

                if (!chunks.length) return 'failed';
                downloadBlob(new Blob(chunks, { type: mimeType }), fileName || 'rap-roast.webm');
                return 'saved';
            } catch {
                return 'failed';
            } finally {
                if (raf) cancelAnimationFrame(raf);
                if (recorder && recorder.state === 'recording') { try { recorder.stop(); } catch { /* already stopping */ } }
            }
        },
    };
})();
