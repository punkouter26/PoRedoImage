#!/usr/bin/env node
/**
 * DOCS build — assembles self-contained audit reports.
 *
 *   node DOCS/build.mjs            # compile diagrams (if mmdc available) + build pages
 *   node DOCS/build.mjs --no-mmd   # build pages only, reusing the committed SVGs
 *
 * Sources live in DOCS/_src; the built pages land at DOCS/*.html with every asset
 * inlined — no CDN, no relative asset fetch, opens straight off the filesystem.
 *
 * Markers understood in a page source:
 *   <!--@STYLE-->        shared design-system CSS
 *   <!--@SHELL-->        shared shell JS (theme toggle + chart theming)
 *   <!--@CHARTJS-->      Chart.js 4.5.1 UMD build, inlined
 *   <!--@SVG:name-->     DOCS/_src/diagrams/out/name.{light,dark}.svg, both inlined
 *   <!--@RAIL:file-->    cross-report navigation rail, current page marked
 */
import { readFileSync, writeFileSync, readdirSync, mkdirSync, existsSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const DOCS = dirname(fileURLToPath(import.meta.url));
const SRC = join(DOCS, '_src');
const DGM_IN = join(SRC, 'diagrams');
const DGM_OUT = join(DGM_IN, 'out');

const REPORTS = [
  ['01', 'AI_SERVICES_REPORT.html', 'AI Services'],
  ['02', 'ARCHITECTURE_REPORT.html', 'Architecture'],
  ['03', 'SLICE_ISOLATION_REPORT.html', 'Slice Isolation'],
  ['04', 'AUTH_LIFECYCLE.html', 'Auth Lifecycle'],
  ['05', 'DIAGNOSTIC_METRICS.html', 'Diagnostics'],
  ['06', 'TESTING_TIER_HIERARCHY.html', 'Testing Tiers'],
  ['07', 'ROLES_PERMISSIONS_MATRIX.html', 'Roles & Permissions'],
  ['08', 'USER_WORKFLOW.html', 'User Workflow'],
];

/* ── mermaid → svg, one light pass and one dark pass ─────────────────────── */
const THEMES = {
  light: {
    theme: 'base',
    themeVariables: {
      background: 'transparent',
      primaryColor: '#f1f3f6', primaryTextColor: '#0b0e12', primaryBorderColor: '#0e7c8c',
      secondaryColor: '#e4e8ed', secondaryTextColor: '#0b0e12', secondaryBorderColor: '#d5dae1',
      tertiaryColor: '#fafbfc', tertiaryTextColor: '#4a535e', tertiaryBorderColor: '#d5dae1',
      lineColor: '#79838f', textColor: '#0b0e12', mainBkg: '#f1f3f6', nodeBorder: '#0e7c8c',
      clusterBkg: 'rgba(14,124,140,0.05)', clusterBorder: '#c2c9d2', titleColor: '#0b0e12',
      edgeLabelBackground: '#fafbfc', nodeTextColor: '#0b0e12',
      actorBkg: '#f1f3f6', actorBorder: '#0e7c8c', actorTextColor: '#0b0e12', actorLineColor: '#c2c9d2',
      signalColor: '#4a535e', signalTextColor: '#0b0e12',
      labelBoxBkgColor: '#e4e8ed', labelBoxBorderColor: '#0e7c8c', labelTextColor: '#0b0e12',
      loopTextColor: '#4a535e', noteBkgColor: '#e4e8ed', noteTextColor: '#0b0e12', noteBorderColor: '#c2c9d2',
      activationBkgColor: '#cfe6ea', activationBorderColor: '#0e7c8c',
      sequenceNumberColor: '#fafbfc',
      transitionColor: '#79838f', stateBkg: '#f1f3f6', stateLabelColor: '#0b0e12',
      altBackground: '#e9ecef', compositeBackground: '#fafbfc', compositeTitleBackground: '#e4e8ed',
      fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif', fontSize: '14px',
    },
  },
  dark: {
    theme: 'base',
    themeVariables: {
      darkMode: true,
      background: 'transparent',
      primaryColor: '#1c222a', primaryTextColor: '#f2f5f8', primaryBorderColor: '#2aa7b8',
      secondaryColor: '#232b34', secondaryTextColor: '#f2f5f8', secondaryBorderColor: '#252c35',
      tertiaryColor: '#151a20', tertiaryTextColor: '#a8b4c2', tertiaryBorderColor: '#252c35',
      lineColor: '#77828f', textColor: '#f2f5f8', mainBkg: '#1c222a', nodeBorder: '#2aa7b8',
      clusterBkg: 'rgba(42,167,184,0.07)', clusterBorder: '#333c47', titleColor: '#f2f5f8',
      edgeLabelBackground: '#151a20', nodeTextColor: '#f2f5f8',
      actorBkg: '#1c222a', actorBorder: '#2aa7b8', actorTextColor: '#f2f5f8', actorLineColor: '#333c47',
      signalColor: '#a8b4c2', signalTextColor: '#f2f5f8',
      labelBoxBkgColor: '#232b34', labelBoxBorderColor: '#2aa7b8', labelTextColor: '#f2f5f8',
      loopTextColor: '#a8b4c2', noteBkgColor: '#232b34', noteTextColor: '#f2f5f8', noteBorderColor: '#333c47',
      activationBkgColor: '#17414a', activationBorderColor: '#2aa7b8',
      sequenceNumberColor: '#0c0f13',
      transitionColor: '#77828f', stateBkg: '#1c222a', stateLabelColor: '#f2f5f8',
      altBackground: '#0c0f13', compositeBackground: '#151a20', compositeTitleBackground: '#232b34',
      fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif', fontSize: '14px',
    },
  },
};

function compileDiagrams() {
  mkdirSync(DGM_OUT, { recursive: true });
  const cfgPath = {};
  for (const mode of ['light', 'dark']) {
    cfgPath[mode] = join(DGM_OUT, `.mmdc.${mode}.json`);
    writeFileSync(cfgPath[mode], JSON.stringify(THEMES[mode], null, 2));
  }
  const puppeteerCfg = join(DGM_OUT, '.puppeteer.json');
  writeFileSync(puppeteerCfg, JSON.stringify({ args: ['--no-sandbox', '--disable-setuid-sandbox'] }));

  // Prefer the locally installed CLI (fast, version-pinned); fall back to the
  // documented one-liner `npx -p @mermaid-js/mermaid-cli mmdc …` when absent.
  const localBin = join(SRC, 'node_modules', '.bin', process.platform === 'win32' ? 'mmdc.cmd' : 'mmdc');
  const hasLocal = existsSync(localBin);
  const runner = (args) => hasLocal
    ? execFileSync(localBin, args, { stdio: ['ignore', 'ignore', 'inherit'], shell: process.platform === 'win32' })
    : execFileSync('npx', ['-y', '-p', '@mermaid-js/mermaid-cli', 'mmdc', ...args],
        { stdio: ['ignore', 'ignore', 'inherit'], shell: process.platform === 'win32' });

  const files = readdirSync(DGM_IN).filter((f) => f.endsWith('.mmd'));
  for (const f of files) {
    const name = f.replace(/\.mmd$/, '');
    for (const mode of ['light', 'dark']) {
      const out = join(DGM_OUT, `${name}.${mode}.svg`);
      process.stdout.write(`  mmdc ${name} (${mode})… `);
      runner(['-i', join(DGM_IN, f), '-o', out, '-c', cfgPath[mode], '-p', puppeteerCfg, '-b', 'transparent']);
      console.log('ok');
    }
  }
  return files.length;
}

/* ── inline an SVG, scoped so ids from two copies never collide ──────────── */
function loadSvg(name, mode) {
  const p = join(DGM_OUT, `${name}.${mode}.svg`);
  if (!existsSync(p)) throw new Error(`missing compiled diagram: ${p} (run without --no-mmd)`);
  let svg = readFileSync(p, 'utf8')
    .replace(/^<\?xml[^>]*\?>\s*/, '')
    .replace(/<!DOCTYPE[^>]*>\s*/, '');
  // Both copies of a diagram land in the same document, so every id must be unique
  // per copy. mermaid roots everything at the literal id "my-svg" — it is the root
  // element id, the prefix of every generated id, AND the prefix of every selector
  // in the emitted <style> block. Renaming that one token in a single global pass
  // keeps attributes, url(#…) refs and CSS selectors consistent; renaming only the
  // attributes (as an earlier pass did) orphans the entire stylesheet and the
  // diagram renders unstyled — black fills, clipped labels, no subgraph titles.
  const scope = `d-${name}-${mode}`;
  svg = svg.replaceAll('my-svg', scope)
           // Link paths carry unprefixed L_<from>_<to> ids; namespace those too so the
           // two copies do not duplicate ids in the document.
           .replace(/\bid="(L_[^"]*)"/g, (_, v) => `id="${scope}-${v}"`);
  svg = svg.replace('<svg', `<svg class="d-${mode}"`);
  return svg;
}

/* ── build ───────────────────────────────────────────────────────────────── */
const noMmd = process.argv.includes('--no-mmd');
if (!noMmd) {
  console.log('Compiling mermaid diagrams (light + dark):');
  const n = compileDiagrams();
  console.log(`  ${n} source diagram(s) → ${n * 2} SVG(s)\n`);
}

const style = readFileSync(join(SRC, 'partials', 'theme.css'), 'utf8');
const shell = readFileSync(join(SRC, 'partials', 'shell.js'), 'utf8');
const chartjs = readFileSync(join(SRC, 'vendor', 'chart.umd.min.js'), 'utf8');

function rail(current) {
  const items = REPORTS.map(([n, file, label]) => {
    const cur = file === current ? ' aria-current="page"' : '';
    return `      <a href="./${file}"${cur}><i>${n}</i>${label}</a>`;
  }).join('\n');
  return `<nav class="rail" aria-label="Audit reports">\n    <div class="wrap rail__inner">\n${items}\n    </div>\n  </nav>`;
}

console.log('Building reports:');
let built = 0;
for (const [, file] of REPORTS) {
  const srcPath = join(SRC, 'pages', file);
  if (!existsSync(srcPath)) { console.log(`  ${file} — SKIP (no source)`); continue; }
  let html = readFileSync(srcPath, 'utf8');

  html = html.replace('<!--@STYLE-->', () => `<style>\n${style}\n</style>`);
  html = html.replace('<!--@CHARTJS-->', () => `<script>${chartjs}</script>`);
  html = html.replace('<!--@SHELL-->', () => `<script>\n${shell}\n</script>`);
  html = html.replace(/<!--@RAIL:(.+?)-->/g, (_, f) => rail(f.trim()));
  html = html.replace(/<!--@SVG:(.+?)-->/g, (_, name) => {
    const n = name.trim();
    return `${loadSvg(n, 'light')}\n${loadSvg(n, 'dark')}`;
  });

  const leftover = html.match(/<!--@[A-Z]+[^>]*-->/g);
  if (leftover) throw new Error(`${file}: unresolved marker(s) ${leftover.join(', ')}`);

  writeFileSync(join(DOCS, file), html);
  console.log(`  ${file} — ${(html.length / 1024).toFixed(0)} KB`);
  built++;
}

/* Task 9 asset: the standalone architecture vector. */
const archSrc = join(DGM_OUT, 'architecture.light.svg');
if (existsSync(archSrc)) {
  writeFileSync(join(DOCS, 'architecture.svg'), readFileSync(archSrc));
  console.log('  architecture.svg — standalone vector asset');
}

console.log(`\n${built}/${REPORTS.length} reports built. All assets inlined; no network at view time.`);
