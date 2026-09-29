// fx.js — the board's sensory layer: flap clacks, arrival chime + ticker-tape, the developing-photo
// reveal, the gallery hero morph, and phone haptics. Sound lives in audio.js; this file decides WHEN.
//
// Same house style as ux.js: one global, no modules, feature-detected. Every visual effect is a
// no-op under prefers-reduced-motion, and haptics share the audio kill switch, so a user who has
// turned feedback off gets none of it.
window.poFx = (function () {

    function audio() { return window.PoRedoImageAudio; }

    function reduced() {
        return !!(window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    }

    function buzz(pattern) {
        try {
            if (navigator.vibrate && audio() && audio().isOn()) navigator.vibrate(pattern);
        } catch { /* vibrate throws in some sandboxed iframes */ }
    }

    // ── Clacks ──────────────────────────────────────────────────────────────
    // FlapText staggers each changed cell's flapSettle by 26ms, so animationstart already arrives as
    // a cascade — listening for it gives every flap on the board a sound with no Razor changes.
    // Bulk tiles use a scoped keyframe, which Blazor renames to bulkFlip-b-<hash>.
    let lastClack = 0;
    document.addEventListener('animationstart', function (e) {
        const name = e.animationName || '';
        if (name === 'flapSettle') {
            const now = performance.now();
            if (now - lastClack < 18) return; // a 20-cell title would otherwise stack into a buzz
            lastClack = now;
            audio() && audio().clack(0.5);
        } else if (name.indexOf('bulkFlip') === 0) {
            audio() && audio().clack(1);
            buzz(12);
        }
    }, true);

    // ── Ticker-tape ─────────────────────────────────────────────────────────
    // A burst of tiny flap cells, each tumbling on its hinge (scaleY by cos) under gravity.
    const GLYPHS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789';

    function burst(originEl) {
        if (reduced()) return;
        const dpr = Math.min(window.devicePixelRatio || 1, 2);
        const canvas = document.createElement('canvas');
        canvas.className = 'fx-burst';
        canvas.width = innerWidth * dpr;
        canvas.height = innerHeight * dpr;
        document.body.appendChild(canvas);
        const ctx = canvas.getContext('2d');
        ctx.scale(dpr, dpr);

        const r = originEl && originEl.getBoundingClientRect();
        const ox = r ? r.left + r.width / 2 : innerWidth / 2;
        const oy = r ? r.top + r.height / 3 : innerHeight / 3;
        const bits = [];
        for (let i = 0; i < 70; i++) {
            const a = -Math.PI / 2 + (Math.random() - 0.5) * 2.2;
            const v = 380 + Math.random() * 520;
            bits.push({
                x: ox, y: oy,
                vx: Math.cos(a) * v, vy: Math.sin(a) * v,
                rot: Math.random() * 6.28, spin: (Math.random() - 0.5) * 10,
                flip: Math.random() * 6.28, flipRate: 8 + Math.random() * 10,
                glyph: GLYPHS[(Math.random() * GLYPHS.length) | 0],
                amber: Math.random() < 0.3,
            });
        }

        const LIFE = 1.7;
        let t0 = 0, last = 0;
        function frame(ts) {
            if (!t0) { t0 = last = ts; }
            const dt = Math.min(0.033, (ts - last) / 1000);
            last = ts;
            const age = (ts - t0) / 1000;
            ctx.clearRect(0, 0, innerWidth, innerHeight);
            ctx.globalAlpha = age > LIFE - 0.4 ? Math.max(0, (LIFE - age) / 0.4) : 1;
            ctx.font = '700 11px "Archivo Narrow", Arial, sans-serif';
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            for (const b of bits) {
                b.vy += 1100 * dt;
                b.vx *= 0.985;
                b.x += b.vx * dt;
                b.y += b.vy * dt;
                b.rot += b.spin * dt;
                b.flip += b.flipRate * dt;
                ctx.save();
                ctx.translate(b.x, b.y);
                ctx.rotate(b.rot);
                ctx.scale(1, Math.cos(b.flip));
                ctx.fillStyle = b.amber ? '#FFB400' : '#1B1B1E';
                ctx.fillRect(-7, -10, 14, 20);
                ctx.fillStyle = 'rgba(0,0,0,0.6)';
                ctx.fillRect(-7, -0.5, 14, 1);
                ctx.fillStyle = b.amber ? '#0D0D0F' : '#F2F2F2';
                ctx.fillText(b.glyph, 0, 1);
                ctx.restore();
            }
            if (age < LIFE) requestAnimationFrame(frame); else canvas.remove();
        }
        requestAnimationFrame(frame);
    }

    // ── Developing-photo reveal (WebGL2) ────────────────────────────────────
    // A fresh result arrives as a halftone print whose dots swell brightest-first, swept by an amber
    // scanline, then resolves to the real pixels. WebGL2 rather than WebGPU: WebGPU is already held
    // by the in-browser models in local-ai/, and WebGL2 runs on every device that reaches this page.
    const VS = '#version 300 es\n' +
        'in vec2 p; out vec2 uv;\n' +
        'void main(){ uv = vec2(p.x * .5 + .5, .5 - p.y * .5); gl_Position = vec4(p, 0., 1.); }';
    const FS = '#version 300 es\n' +
        'precision mediump float;\n' +
        'in vec2 uv; out vec4 o;\n' +
        'uniform sampler2D img; uniform float t; uniform vec2 res;\n' +
        'void main(){\n' +
        '  vec4 c = texture(img, uv);\n' +
        '  float lum = dot(c.rgb, vec3(.299, .587, .114));\n' +
        '  vec2 gv = fract(uv * res / 10.) - .5;\n' +
        '  float r = clamp(t * 1.7 - (1. - lum) * .7, 0., 1.) * .72;\n' +
        '  float dotMask = smoothstep(r, r - .09, length(gv));\n' +
        '  vec3 print = mix(vec3(.05), c.rgb * vec3(1., .9, .72), dotMask);\n' +
        '  float k = smoothstep(.55, 1., t);\n' +
        '  float band = exp(-pow((uv.y - (t * 1.25 - .12)) * 38., 2.)) * (1. - k);\n' +
        '  o = vec4(mix(print, c.rgb, k) + vec3(1., .706, 0.) * band * .7, 1.);\n' +
        '}';

    function develop(img) {
        if (reduced() || !img.naturalWidth) return;
        const host = img.parentElement;
        const scale = Math.min(1, 1024 / Math.max(img.naturalWidth, img.naturalHeight));
        const canvas = document.createElement('canvas');
        canvas.className = 'fx-develop';
        canvas.width = Math.round(img.naturalWidth * scale);
        canvas.height = Math.round(img.naturalHeight * scale);
        const gl = canvas.getContext('webgl2', { premultipliedAlpha: false });
        if (!gl) return;

        const prog = gl.createProgram();
        try {
            [[gl.VERTEX_SHADER, VS], [gl.FRAGMENT_SHADER, FS]].forEach(function (s) {
                const sh = gl.createShader(s[0]);
                gl.shaderSource(sh, s[1]);
                gl.compileShader(sh);
                gl.attachShader(prog, sh);
            });
            gl.linkProgram(prog);
            if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) return;
            gl.useProgram(prog);

            gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
            gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, 1, 1]), gl.STATIC_DRAW);
            const loc = gl.getAttribLocation(prog, 'p');
            gl.enableVertexAttribArray(loc);
            gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);

            gl.bindTexture(gl.TEXTURE_2D, gl.createTexture());
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
            gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
            // Throws SecurityError for a cross-origin image without CORS — the catch lets the
            // plain fade-in stand, which is the correct fallback for a decoration.
            gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
        } catch {
            return;
        }

        const uT = gl.getUniformLocation(prog, 't');
        gl.uniform2f(gl.getUniformLocation(prog, 'res'), canvas.width, canvas.height);
        gl.viewport(0, 0, canvas.width, canvas.height);
        host.appendChild(canvas);

        const DURATION = 1500;
        let t0 = 0;
        function frame(ts) {
            if (!t0) t0 = ts;
            const t = Math.min(1, (ts - t0) / DURATION);
            gl.uniform1f(uT, t);
            gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
            if (t < 1 && canvas.isConnected) { requestAnimationFrame(frame); return; }
            canvas.remove();
            const lose = gl.getExtension('WEBGL_lose_context');
            if (lose) lose.loseContext(); // browsers cap live contexts at ~16
        }
        requestAnimationFrame(frame);
    }

    // `load` does not bubble, but it does capture. The right-hand side of the compare panel is the
    // result on every page that has one; `.error` marks the broken-image placeholder swap.
    document.addEventListener('load', function (e) {
        const img = e.target;
        if (img.tagName === 'IMG'
            && !img.classList.contains('error')
            && img.matches('.split-compare__side:last-child .split-compare__img')) {
            develop(img);
        }
    }, true);

    // ── Gallery hero morph (View Transitions) ───────────────────────────────
    function waitFor(selector, ms) {
        return new Promise(function (resolve) {
            const found = document.querySelector(selector);
            if (found) { resolve(found); return; }
            const obs = new MutationObserver(function () {
                const el = document.querySelector(selector);
                if (el) { obs.disconnect(); clearTimeout(timer); resolve(el); }
            });
            obs.observe(document.body, { childList: true, subtree: true });
            const timer = setTimeout(function () { obs.disconnect(); resolve(null); }, ms);
        });
    }

    /**
     * Call BEFORE opening the lightbox for `src`. Resolves once the browser has snapshotted the
     * thumbnail, which is the moment the caller may open the dialog; the transition then morphs the
     * thumbnail into `.gallery-lightbox__img` when it lands.
     */
    function heroFrom(src) {
        if (!document.startViewTransition || reduced()) return Promise.resolve();
        const thumb = Array.prototype.find.call(document.querySelectorAll('img'), function (i) {
            return i.getAttribute('src') === src && i.offsetParent !== null;
        });
        if (!thumb) return Promise.resolve();

        thumb.style.viewTransitionName = 'po-hero';
        return new Promise(function (resolve) {
            // Belt and braces: a skipped transition never calls the update callback, and a dialog
            // that waits on it forever is far worse than one that opens without the morph.
            const bail = setTimeout(resolve, 300);
            const vt = document.startViewTransition(function () {
                thumb.style.viewTransitionName = '';
                clearTimeout(bail);
                resolve();
                return waitFor('.gallery-lightbox__img', 1000).then(function (big) {
                    if (big) big.style.viewTransitionName = 'po-hero';
                });
            });
            vt.ready.catch(function () { /* skipped — the dialog still opens */ });
            vt.finished.finally(function () {
                thumb.style.viewTransitionName = '';
                const big = document.querySelector('.gallery-lightbox__img');
                if (big) big.style.viewTransitionName = '';
            });
        });
    }

    return {
        /** A run finished: chime, "Now arriving: <label>", ticker-tape, a double tap. */
        done: function (label) {
            audio() && audio().success(label);
            burst(document.querySelector('.split-compare, .bulk-gallery, .roast-player, .video-result__player'));
            buzz([18, 40, 18]);
        },
        /** A run failed: the thud and one long buzz. */
        fail: function () {
            audio() && audio().failure();
            buzz(80);
        },
        heroFrom: heroFrom,
    };
})();
