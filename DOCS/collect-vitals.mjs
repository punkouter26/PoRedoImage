#!/usr/bin/env node
/**
 * One idempotent command that guarantees DOCS/diagnostic_history.json exists, has fresh
 * samples appended to it, and is inlined into the dashboard.
 *
 *   node DOCS/collect-vitals.mjs                  # 6 page loads, append, rebuild docs
 *   node DOCS/collect-vitals.mjs --runs 20
 *   node DOCS/collect-vitals.mjs --replace        # start the history over instead of appending
 *   node DOCS/collect-vitals.mjs --no-browser     # export whatever the app already stored
 *
 * Safe to run repeatedly. Each run:
 *   1. starts Azurite if its container exists but is stopped   (skipped if Docker is absent)
 *   2. builds, and starts the app ONLY if nothing already answers on --base
 *   3. drives a real headless browser through N page loads so the collector fires
 *   4. appends the new samples to diagnostic_history.json, de-duplicated
 *   5. re-inlines the file into DIAGNOSTIC_METRICS.html
 *   6. stops the app again — but only if this script was the one that started it
 *
 * History accumulates across runs: the export merges on (timestamp, route), so samples that
 * have aged out of the server's query window survive in the file. --keep bounds the growth.
 */
import { spawn, spawnSync, execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { dirname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const DOCS = dirname(fileURLToPath(import.meta.url));
const ROOT = dirname(DOCS);
const HISTORY = join(DOCS, 'diagnostic_history.json');
const IS_WIN = process.platform === 'win32';

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] && !process.argv[i + 1].startsWith('--')
    ? process.argv[i + 1] : fallback;
}
const flag = (name) => process.argv.includes(`--${name}`);

const base = arg('base', 'http://localhost:4000').replace(/\/$/, '');
const runs = Number(arg('runs', '6'));
const days = arg('days', '30');
const keep = arg('keep', '1000');
const routes = ['/', '/image-regeneration', '/meme-generation', '/style-director', '/rap-roast', '/bulk-generate'];

/**
 * Resolves a package from DOCS/_src regardless of how npm hoisted it, instead of
 * hardcoding a node_modules path that changes between npm versions and platforms.
 */
function resolveFrom(pkg) {
  try {
    const require = createRequire(join(DOCS, '_src', 'package.json'));
    return require.resolve(pkg);
  } catch { return null; }
}

const step = (n, msg) => console.log(`\n[${n}/6] ${msg}`);
const ok = (msg) => console.log(`      ✓ ${msg}`);
const warn = (msg) => console.log(`      ! ${msg}`);

async function reachable(url, ms = 2000) {
  try {
    const c = AbortSignal.timeout(ms);
    const r = await fetch(url, { signal: c });
    return r.ok;
  } catch { return false; }
}

async function waitFor(url, seconds) {
  for (let i = 0; i < seconds; i++) {
    if (await reachable(url)) return true;
    await new Promise((r) => setTimeout(r, 1000));
  }
  return false;
}

/* ── 1. storage ─────────────────────────────────────────────────────────── */
step(1, 'Azure Storage (Azurite)');
let storageUp = await reachable('http://127.0.0.1:10002/devstoreaccount1', 1500).catch(() => false);
// Azurite answers 400 to an unauthenticated probe, which fetch reports as !ok but not as a
// network error — so treat "responded at all" as up.
try { await fetch('http://127.0.0.1:10002/devstoreaccount1', { signal: AbortSignal.timeout(1500) }); storageUp = true; }
catch { storageUp = false; }

if (storageUp) {
  ok('Azurite is listening on :10002');
} else {
  const ps = spawnSync('docker', ['ps', '-a', '--filter', 'name=azurite', '--format', '{{.Names}}'],
    { encoding: 'utf8', shell: IS_WIN });
  const container = (ps.stdout || '').trim().split('\n').filter(Boolean)[0];
  if (container) {
    process.stdout.write(`      starting container "${container}"… `);
    spawnSync('docker', ['start', container], { stdio: 'ignore', shell: IS_WIN });
    console.log((await waitFor('http://127.0.0.1:10002/devstoreaccount1', 20)) ? 'up' : 'no response');
  } else {
    warn('Azurite not running and no container found — samples will not persist.');
    warn('Start it with: docker compose up -d azurite');
  }
}

/* ── 2. the app ─────────────────────────────────────────────────────────── */
step(2, 'Application');
let ownServer = null;

if (await reachable(`${base}/alive`)) {
  ok(`already running at ${base} — leaving it alone`);
} else {
  process.stdout.write('      building… ');
  const build = spawnSync('dotnet', ['build', join(ROOT, 'PoRedoImage.slnx'), '--nologo', '-v', 'q'],
    { encoding: 'utf8', shell: IS_WIN });
  if (build.status !== 0) {
    console.log('FAILED');
    const out = `${build.stdout || ''}${build.stderr || ''}`;
    // The most common failure here is not a compile error at all: another instance of the app
    // is holding the output DLLs. Name that explicitly rather than dumping 30 MSBuild lines.
    if (/MSB302[17]|being used by another process/.test(out)) {
      console.error(
        '\n      The build could not overwrite its output — another copy of the app is running\n' +
        '      on a different port and holding the DLLs. Stop it and re-run:\n' +
        (IS_WIN
          ? '        Get-Process PoRedoImage.Web | Stop-Process -Force\n'
          : '        pkill -f PoRedoImage.Web\n'));
    } else {
      console.error(out);
    }
    process.exit(1);
  }
  console.log('ok');

  // Launch the built executable rather than `dotnet run`: that gives a real PID to stop
  // later, instead of a wrapper process that orphans the app when killed.
  const exe = join(ROOT, 'src', 'PoRedoImage.Web', 'bin', 'Debug', 'net10.0',
    IS_WIN ? 'PoRedoImage.Web.exe' : 'PoRedoImage.Web');
  if (!existsSync(exe)) {
    console.error(`      built output not found at ${exe}`);
    process.exit(1);
  }

  process.stdout.write('      starting… ');
  ownServer = spawn(exe, [], {
    cwd: join(ROOT, 'src', 'PoRedoImage.Web'),
    stdio: 'ignore',
    detached: false,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      ASPNETCORE_URLS: base,
      // Never spend a live token just to measure page-load timing.
      Mocks__UseMockAi: 'true',
      Storage__ConnectionString: 'UseDevelopmentStorage=true',
    },
  });

  if (!(await waitFor(`${base}/alive`, 45))) {
    console.log('FAILED');
    try { ownServer.kill(); } catch { /* ignore */ }
    console.error(`      app did not become healthy at ${base}/alive`);
    process.exit(1);
  }
  console.log(`up at ${base}`);
}

/* Always stop what we started, including on Ctrl-C or an unhandled error. */
function stopOwnServer() {
  if (!ownServer) return;
  try { ownServer.kill('SIGKILL'); } catch { /* ignore */ }
  ownServer = null;
}
process.on('SIGINT', () => { stopOwnServer(); process.exit(130); });
process.on('uncaughtException', (e) => { stopOwnServer(); throw e; });

try {
  /* ── 3. collect ───────────────────────────────────────────────────────── */
  step(3, `Collecting ${runs} sample(s) in a real browser`);
  if (flag('no-browser')) {
    ok('skipped (--no-browser) — exporting whatever the app already stored');
  } else {
    // DOCS/_src/node_modules is gitignored, so on a fresh clone the toolchain is absent even
    // though this script is present. Install it rather than failing — that is the single most
    // likely reason for this step to break on another machine.
    let puppeteerEntry = resolveFrom('puppeteer');
    if (!puppeteerEntry) {
      process.stdout.write('      puppeteer not installed — running npm install (first run only)… ');
      const install = spawnSync('npm', ['install', '--no-audit', '--no-fund', '--silent'],
        { cwd: join(DOCS, '_src'), encoding: 'utf8', shell: IS_WIN });
      console.log(install.status === 0 ? 'ok' : 'FAILED');
      puppeteerEntry = resolveFrom('puppeteer');
    }

    if (!puppeteerEntry) {
      warn('Could not install puppeteer. Install it manually:  npm --prefix DOCS/_src install');
      warn('Continuing without collection — the export below will use whatever is already stored.');
    } else {
      const { default: puppeteer } = await import(pathToFileURL(puppeteerEntry).href);
      const browser = await puppeteer.launch({ args: ['--no-sandbox', '--disable-setuid-sandbox'] });
      try {
        const page = await browser.newPage();
        let accepted = 0;
        page.on('response', (r) => {
          if (r.url().includes('/api/diag/vitals') && r.request().method() === 'POST' && r.status() === 202) accepted++;
        });

        // Development GUEST sign-in; the vitals POST requires an authenticated principal.
        await page.goto(`${base}/auth/login/fake?email=vitals-collector@local`, { waitUntil: 'networkidle2' });

        for (let i = 0; i < runs; i++) {
          const route = routes[i % routes.length];
          process.stdout.write(`      ${String(i + 1).padStart(2, '0')}/${runs}  ${route.padEnd(20)}`);
          // A full document load each time: PerformanceNavigationTiming describes the document,
          // so a client-side route change would not produce a new sample.
          await page.goto(base + route, { waitUntil: 'networkidle2', timeout: 60000 });
          // The reporter waits for `load` plus a 2.5s settle window before posting.
          await new Promise((r) => setTimeout(r, 4500));
          console.log('sent');
        }
        ok(`${accepted}/${runs} accepted by the API`);
        if (accepted === 0) {
          warn('No samples were accepted. Check that js/vitals.js is referenced from Web/Components/App.razor.');
        }
      } finally {
        await browser.close();
      }
    }
  }

  /* ── 4. export ────────────────────────────────────────────────────────── */
  step(4, `Exporting to ${HISTORY}`);
  const exportArgs = [join(DOCS, 'export-vitals.mjs'), '--base', base, '--days', String(days), '--keep', String(keep)];
  if (flag('replace')) exportArgs.push('--replace');
  execFileSync(process.execPath, exportArgs, { stdio: 'inherit' });

  /* ── 5. rebuild docs ──────────────────────────────────────────────────── */
  step(5, 'Rebuilding the dashboard');
  execFileSync(process.execPath, [join(DOCS, 'build.mjs'), '--no-mmd'], { stdio: 'inherit' });

  /* ── 6. report ────────────────────────────────────────────────────────── */
  step(6, 'Summary');
  const doc = JSON.parse(readFileSync(HISTORY, 'utf8'));
  const tti = doc.samples.map((s) => s.interactiveMs).filter((v) => v > 0).sort((a, b) => a - b);
  const median = tti.length ? tti[tti.length >> 1] : 0;
  ok(`${doc.count} sample(s) in history, median time to interactive ${Math.round(median)} ms`);
  ok(`open DOCS/DIAGNOSTIC_METRICS.html`);
} finally {
  if (ownServer) {
    stopOwnServer();
    console.log('\n      (stopped the app this script started)');
  } else {
    console.log('\n      (left your already-running app alone)');
  }
}
