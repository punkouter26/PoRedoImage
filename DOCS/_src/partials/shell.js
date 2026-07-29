/* Shared report shell: theme toggle, chart theming, table-view relief.
   No network, no storage assumptions beyond localStorage (guarded). */
(function () {
  var root = document.documentElement;

  /* ── theme ────────────────────────────────────────────────────────────── */
  var stored = null;
  try { stored = localStorage.getItem('poredo-docs-theme'); } catch (e) { /* private mode */ }
  if (stored === 'light' || stored === 'dark') root.setAttribute('data-theme', stored);

  function effective() {
    var t = root.getAttribute('data-theme');
    if (t) return t;
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  var toggle = document.getElementById('theme-toggle');
  function paintToggle() {
    if (!toggle) return;
    var t = effective();
    toggle.textContent = t === 'dark' ? 'Light theme' : 'Dark theme';
    toggle.setAttribute('aria-label', 'Switch to ' + (t === 'dark' ? 'light' : 'dark') + ' theme');
  }
  paintToggle();

  if (toggle) {
    toggle.addEventListener('click', function () {
      var next = effective() === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-theme', next);
      try { localStorage.setItem('poredo-docs-theme', next); } catch (e) { /* ignore */ }
      paintToggle();
      repaintCharts();
    });
  }
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
    if (!root.getAttribute('data-theme')) { paintToggle(); repaintCharts(); }
  });

  /* ── design tokens, read live from CSS so charts never fork the palette ── */
  function token(name) {
    return getComputedStyle(root).getPropertyValue(name).trim();
  }
  window.tok = token;

  /* ── Chart.js defaults wired to the tokens ────────────────────────────── */
  var charts = [];
  window.registerChart = function (c) { charts.push(c); return c; };

  window.chartTheme = function () {
    return {
      ink:    token('--ink'),
      ink2:   token('--ink-2'),
      ink3:   token('--ink-3'),
      grid:   token('--grid'),
      axis:   token('--axis'),
      surf:   token('--surface'),
      series: ['--s1', '--s2', '--s3', '--s4', '--s5', '--s6'].map(token),
      status: { ok: token('--ok'), warn: token('--warn'), serious: token('--serious'), crit: token('--crit') },
      mono:   token('--mono') || 'monospace'
    };
  };

  function repaintCharts() {
    if (typeof Chart === 'undefined') return;
    applyChartDefaults();
    charts.forEach(function (c) {
      if (c && typeof c.rethemeFn === 'function') c.rethemeFn(window.chartTheme());
      if (c && typeof c.update === 'function') c.update('none');
    });
  }
  window.repaintCharts = repaintCharts;

  function applyChartDefaults() {
    if (typeof Chart === 'undefined') return;
    var t = window.chartTheme();
    Chart.defaults.font.family = 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif';
    Chart.defaults.font.size = 11;
    Chart.defaults.color = t.ink3;
    Chart.defaults.borderColor = t.grid;
    Chart.defaults.animation.duration =
      window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 420;
    /* Legends are rendered in HTML (identity is never colour-alone and stays
       readable when the canvas is unavailable) — Chart.js' own box stays off. */
    Chart.defaults.plugins.legend.display = false;
    Chart.defaults.plugins.tooltip.backgroundColor = t.ink;
    Chart.defaults.plugins.tooltip.titleColor = t.surf;
    Chart.defaults.plugins.tooltip.bodyColor = t.surf;
    Chart.defaults.plugins.tooltip.borderColor = t.axis;
    Chart.defaults.plugins.tooltip.borderWidth = 1;
    Chart.defaults.plugins.tooltip.padding = 9;
    Chart.defaults.plugins.tooltip.cornerRadius = 3;
    Chart.defaults.plugins.tooltip.displayColors = true;
    Chart.defaults.plugins.tooltip.boxWidth = 9;
    Chart.defaults.plugins.tooltip.boxHeight = 9;
    Chart.defaults.plugins.tooltip.usePointStyle = false;
    Chart.defaults.maintainAspectRatio = false;
    Chart.defaults.responsive = true;
  }
  window.applyChartDefaults = applyChartDefaults;

  document.addEventListener('DOMContentLoaded', function () {
    applyChartDefaults();
    if (typeof window.buildCharts === 'function') window.buildCharts();
  });
})();
