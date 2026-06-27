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

    function tone({ freq = 880, durMs = 60, type = 'sine', attack = 4, release = 24 } = {}) {
        if (!enabled || prefersReduced()) return;
        const c = ensureContext();
        if (!c) return;
        if (c.state === 'suspended') c.resume();
        const osc = c.createOscillator();
        const gain = c.createGain();
        osc.type = type;
        osc.frequency.value = freq;
        const now = c.currentTime;
        gain.gain.setValueAtTime(0, now);
        gain.gain.linearRampToValueAtTime(1, now + attack / 1000);
        gain.gain.linearRampToValueAtTime(0, now + durMs / 1000);
        osc.connect(gain);
        gain.connect(master);
        osc.start(now);
        osc.stop(now + (durMs + release) / 1000);
    }

    function noise({ durMs = 120, lowpass = 1200 } = {}) {
        if (!enabled || prefersReduced()) return;
        const c = ensureContext();
        if (!c) return;
        if (c.state === 'suspended') c.resume();
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

    // Two-note success arpeggio: A5 → E6, 70ms each.
    function success() {
        tone({ freq: 880, durMs: 70, type: 'sine' });
        setTimeout(() => tone({ freq: 1318.51, durMs: 110, type: 'sine' }), 70);
    }

    // Low-passed noise burst for failures.
    function failure() {
        noise({ durMs: 140, lowpass: 800 });
    }

    // Single soft tick for in-progress events.
    function tick() {
        tone({ freq: 1320, durMs: 22, type: 'sine' });
    }

    function setEnabled(value) {
        enabled = !!value;
        try { localStorage.setItem('poredoimage.audio.enabled', enabled ? '1' : '0'); } catch { /* ignore */ }
    }

    readPref();
    return { success, failure, tick, setEnabled };
})();

window.PoRedoImageAudio = AudioCue;
