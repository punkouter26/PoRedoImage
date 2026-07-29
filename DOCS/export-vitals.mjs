#!/usr/bin/env node
/**
 * Exports collected client vitals to DOCS/diagnostic_history.json.
 *
 *   node DOCS/export-vitals.mjs                       # localhost:4000, dev GUEST sign-in
 *   node DOCS/export-vitals.mjs --days 30 --max 500
 *   node DOCS/export-vitals.mjs --base https://your-app.azurewebsites.net --cookie "<auth cookie>"
 *
 * Reads GET /api/diag/vitals, which is behind the DiagnosticsAccess policy: in Production the
 * caller must be on the Diagnostics:AdminEmails allow-list, so pass a real session cookie with
 * --cookie there. Against a local Development instance the script can sign itself in via the
 * GUEST route, which only exists outside Production.
 *
 * The build step inlines the resulting file, so after exporting run:
 *   node DOCS/build.mjs --no-mmd
 */
import { writeFileSync, readFileSync, existsSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const DOCS = dirname(fileURLToPath(import.meta.url));
const OUT = join(DOCS, 'diagnostic_history.json');

function arg(name, fallback) {
  const i = process.argv.indexOf(`--${name}`);
  return i !== -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const base = arg('base', 'http://localhost:4000').replace(/\/$/, '');
const days = Number(arg('days', '30'));
const max = Number(arg('max', '500'));
const merge = !process.argv.includes('--replace');
/* Drop anything older than an ISO instant. Useful for incremental exports, and after a
   schema change: rows written before a field existed come back with that field at its
   default, which would otherwise plot as a spurious zero. */
const since = arg('since', null) ? new Date(arg('since', null)) : null;
if (since && Number.isNaN(since.valueOf())) {
  console.error('--since must be an ISO instant, e.g. 2026-07-29T01:00:00Z');
  process.exit(1);
}
let cookie = arg('cookie', null);

/** Dev-only convenience: the GUEST route issues a cookie outside Production. */
async function devSignIn() {
  const res = await fetch(`${base}/auth/login/fake?email=vitals-export@local`, { redirect: 'manual' });
  const setCookie = res.headers.getSetCookie?.() ?? [];
  if (!setCookie.length) return null;
  return setCookie.map((c) => c.split(';')[0]).join('; ');
}

if (!cookie) {
  process.stdout.write('No --cookie given; trying the Development GUEST sign-in… ');
  cookie = await devSignIn();
  console.log(cookie ? 'ok' : 'unavailable');
  if (!cookie) {
    console.error(
      '\nCould not obtain a session. Against a non-Development instance pass a real cookie:\n' +
      '  node DOCS/export-vitals.mjs --base https://… --cookie ".AspNetCore.Cookies=…"\n');
    process.exit(1);
  }
}

const url = `${base}/api/diag/vitals?days=${days}&max=${max}`;
const res = await fetch(url, { headers: { cookie } });

if (res.status === 401 || res.status === 403) {
  console.error(
    `\n${res.status} from ${url}.\n` +
    'GET /api/diag/vitals is behind the DiagnosticsAccess policy. In Production the caller must be\n' +
    'on the Diagnostics:AdminEmails allow-list — and an empty list denies everyone, by design.');
  process.exit(1);
}
if (!res.ok) {
  console.error(`\n${res.status} ${res.statusText} from ${url}`);
  process.exit(1);
}

const payload = await res.json();
const all = payload.samples ?? [];

/* Rows written before InteractiveMs existed deserialize with it at 0. That value is
   impossible for a genuine sample — the app cannot have rendered at navigation start —
   so it identifies a pre-schema row exactly. Dropped, but never silently: a zero here
   would plot as a floor-scraping outlier and drag the whole series down. */
const schemaOk = all.filter((s) => Number(s.interactiveMs) > 0);
if (schemaOk.length !== all.length) {
  console.log(`  skipped ${all.length - schemaOk.length} row(s) written before interactiveMs existed`);
}

const incoming = since ? schemaOk.filter((s) => new Date(s.timestamp) >= since) : schemaOk;
if (since && schemaOk.length !== incoming.length) {
  console.log(`  --since dropped ${schemaOk.length - incoming.length} sample(s) older than ${since.toISOString()}`);
}

/* Merge on (timestamp, route) so repeated exports accumulate history rather than
   truncating it to whatever the server's retention window still holds. */
var samples = incoming;
if (merge && existsSync(OUT)) {
  const existing = JSON.parse(readFileSync(OUT, 'utf8')).samples ?? [];
  const seen = new Set(incoming.map((s) => `${s.timestamp}|${s.route}`));
  samples = [...incoming, ...existing.filter((s) => !seen.has(`${s.timestamp}|${s.route}`))];
}
samples.sort((a, b) => new Date(b.timestamp) - new Date(a.timestamp));

/* Bound the file. Merging across many runs would otherwise grow it without limit, and the
   dashboard only ever plots a window anyway. Newest are kept — the sort above is descending. */
const keep = Number(arg('keep', '1000'));
const dropped = Math.max(0, samples.length - keep);
if (dropped > 0) {
  samples = samples.slice(0, keep);
  console.log(`  --keep ${keep} pruned ${dropped} of the oldest sample(s)`);
}

const doc = {
  schemaVersion: 1,
  source: base,
  exportedAt: payload.generatedAt,
  windowDays: payload.days,
  count: samples.length,
  samples,
};

writeFileSync(OUT, JSON.stringify(doc, null, 2) + '\n');

const added = samples.length - (merge && existsSync(OUT) ? samples.length - incoming.length : 0);
console.log(
  `\nWrote ${OUT}\n` +
  `  ${incoming.length} sample(s) from the server, ${samples.length} total after merge\n` +
  `\nNow run:  node DOCS/build.mjs --no-mmd   (inlines it into DIAGNOSTIC_METRICS.html)`);
if (samples.length === 0) {
  console.log(
    '\nZero samples: the app has to be loaded in a real browser at least once while signed in.\n' +
    'The reporter waits for the load event plus a 2.5s settle window before posting.');
}
