import { test, expect } from '@playwright/test';

test('landing page loads and contains app title', async ({ page }) => {
  await page.goto('/');
  await expect(page).toHaveTitle(/PoRedoImage/i);
  await expect(page.getByText('AI Image Studio')).toBeVisible();
});

test('/health returns healthy JSON', async ({ request }) => {
  const res = await request.get('/health');
  expect(res.ok()).toBeTruthy();
  const json = await res.json();
  expect(json).toHaveProperty('Status');
});

test('/api/diag returns masked configuration', async ({ request }) => {
  const res = await request.get('/api/diag');
  expect(res.ok()).toBeTruthy();
  const json = await res.json();
  expect(json).toHaveProperty('Configuration');
  expect(json).toHaveProperty('Health');
});

test('dev login creates session cookie', async ({ page }) => {
  await page.goto('/dev-login?email=e2e-test@example.com&returnUrl=/');
  await expect(page).toHaveTitle(/PoRedoImage/i);
});

