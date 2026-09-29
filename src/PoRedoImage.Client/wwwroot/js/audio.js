// audio.js — procedurally synthesized micro-feedback cues.
// Zero asset cost: every sound is built from OscillatorNode + AudioBufferSourceNode.
// Honours prefers-reduced-motion / reduced-data + a localStorage kill switch.

const AudioCue = (() => {
    let ctx = null;
    let master = null;
    let enabled = true;

    function ensureContext() {
        if (ctx) return ctx;
        const Ctor = window.AudioContext || window.webkitAudioContext;
        if (!Ctor) return null;
        ctx = new Ctor();
        master = ctx.createGain();
        master.gain.value = 0.18; // global ceiling — keep cues polite
        master.connect(ctx.destination);
        return ctx;
    }

    function readPref() {
        try {
            const stored = localStorage.getItem('poredoimage.audio.enabled');
            if (stored === '0') enabled = false;
            if (stored === '1') enabled = true;
        } catch { /* localStorage may be blocked in some contexts */ }
    }

    function prefersReduced() {
        if (window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches) return true;
        if (window.matchMedia && window.matchMedia('(prefers-reduced-data: reduce)').matches) return true;
        return false;
    }

    function ready() {
        if (!isOn()) return false;
        const c = ensureContext();
        if (!c) return false;
        if (c.state === 'suspended') c.resume();
        return true;
    }

    function noise({ durMs = 120, lowpass = 1200 } = {}) {
        if (!ready()) return;
        const c = ctx;
        // Fill a tiny buffer with white noise + lowpass for a soft thud.
        const buf = c.createBuffer(1, c.sampleRate * (durMs / 1000), c.sampleRate);
        const data = buf.getChannelData(0);
        for (let i = 0; i < data.length; i++) data[i] = (Math.random() * 2 - 1) * (1 - i / data.length);
        const src = c.createBufferSource();
        src.buffer = buf;
        const filter = c.createBiquadFilter();
        filter.type = 'lowpass';
        filter.frequency.value = lowpass;
        const gain = c.createGain();
        gain.gain.value = 0.7;
        src.connect(filter).connect(gain).connect(master);
        src.start();
        src.stop(c.currentTime + durMs / 1000);
    }

    // Station PA chime — three struck bells, G5 → E5 → C5, each with a long exponential tail.
    function bell(freq, at, peak) {
        const c = ensureContext();
        const osc = c.createOscillator();
        const gain = c.createGain();
        osc.type = 'triangle';
        osc.frequency.value = freq;
        gain.gain.setValueAtTime(0, at);
        gain.gain.linearRampToValueAtTime(peak, at + 0.008);
        gain.gain.exponentialRampToValueAtTime(0.0001, at + 1.1);
        osc.connect(gain).connect(master);
        osc.start(at);
        osc.stop(at + 1.15);
    }

    function success(label) {
        if (!ready()) return;
        const now = ctx.currentTime;
        bell(783.99, now, 0.9);
        bell(659.25, now + 0.28, 0.8);
        bell(523.25, now + 0.56, 0.9);
        announce(label, 900);
    }

    // "Now arriving: Meme." Spoken through the platform voice, after the chime has rung out.
    function announce(label, delayMs) {
        if (!label || !window.speechSynthesis || !window.SpeechSynthesisUtterance) return;
        setTimeout(function () {
            const u = new SpeechSynthesisUtterance('Now arriving: ' + label + '.');
            u.rate = 0.95;
            u.volume = 0.8;
            speechSynthesis.cancel();
            speechSynthesis.speak(u);
        }, delayMs);
    }

    // Low-passed noise burst for failures.
    function failure() {
        noise({ durMs: 140, lowpass: 800 });
    }

    // One split-flap leaf hitting its stop: a 10ms band-passed click with a little pitch scatter,
    // so a cascade of them sounds mechanical rather than like one sample on repeat.
    // Skipped until the page has had a gesture — an AudioContext created earlier starts suspended
    // and Chrome logs a warning for every resume attempt.
    let clackBuf = null;
    function clack(level) {
        if (navigator.userActivation && !navigator.userActivation.hasBeenActive) return;
        if (!ready()) return;
        if (!clackBuf) {
            clackBuf = ctx.createBuffer(1, Math.ceil(ctx.sampleRate * 0.012), ctx.sampleRate);
            const d = clackBuf.getChannelData(0);
            for (let i = 0; i < d.length; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / d.length, 3);
        }
        const src = ctx.createBufferSource();
        src.buffer = clackBuf;
        src.playbackRate.value = 0.85 + Math.random() * 0.3;
        const band = ctx.createBiquadFilter();
        band.type = 'bandpass';
        band.frequency.value = 2400 + Math.random() * 1600;
        band.Q.value = 1.2;
        const gain = ctx.createGain();
        gain.gain.value = 0.55 * (level || 1);
        src.connect(band).connect(gain).connect(master);
        src.start();
    }

    /** True when cues are allowed to fire — the same gate every sound uses. */
    function isOn() {
        return enabled && !prefersReduced();
    }

    function setEnabled(value) {
        enabled = !!value;
        try { localStorage.setItem('poredoimage.audio.enabled', enabled ? '1' : '0'); } catch { /* ignore */ }
    }

    readPref();
    return { success, failure, clack, isOn, setEnabled };
})();

window.PoRedoImageAudio = AudioCue;
