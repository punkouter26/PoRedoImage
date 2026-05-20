import { test, expect } from '@playwright/test';

// Accumulate JS errors across all tests so they appear in the final report
const jsErrors: string[] = [];

test.beforeEach(async ({ page }) => {
  page.on('pageerror', (err) => jsErrors.push(`[pageerror] ${err.message}`));
  page.on('console', (msg) => {
    if (msg.type() === 'error') jsErrors.push(`[console.error] ${msg.text()}`);
  });
});

test.afterAll(() => {
  if (jsErrors.length > 0) {
    console.log('\nJS Errors captured during E2E run:');
    jsErrors.forEach((e) => console.log('  •', e));
  } else {
    console.log('\nNo JS errors detected during E2E run. ✓');
  }
});

test('landing page loads and contains app title', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/PoRedoImage/i);
  await expect(page.getByRole('heading', { name: 'AI Image Studio' })).toBeVisible();
});

test('/health returns healthy JSON', async ({ request }) => {
  const res = await request.get('/health');
  expect(res.ok()).toBeTruthy();
  const json = await res.json();
  expect(json).toHaveProperty('status');
});

test('/api/diag returns masked configuration', async ({ page }) => {
  // Authenticate via dev-login first so the session cookie is available to page.request
  await page.goto('/dev-login?email=e2e-test@example.com&returnUrl=/');
  const res = await page.request.get('/api/diag');
  expect(res.ok()).toBeTruthy();
  const json = await res.json();
  expect(json).toHaveProperty('Configuration');
  expect(json).toHaveProperty('Health');
});

test('dev login creates session cookie', async ({ page }) => {
  await page.goto('/dev-login?email=e2e-test@example.com&returnUrl=/');
  await expect(page).toHaveTitle(/PoRedoImage/i);
});

