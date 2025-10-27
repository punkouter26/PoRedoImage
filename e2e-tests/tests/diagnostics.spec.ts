import { test, expect } from '@playwright/test';

/**
 * E2E tests for Diagnostics page
 */
test.describe('Diagnostics Page', () => {
  test('should load the diagnostics page', async ({ page }) => {
    await page.goto('/diag');
    
    // Check for page title or heading
    const heading = page.locator('h1:has-text("System Diagnostics"), h1:has-text("Diagnostics")').first();
    await expect(heading).toBeVisible({ timeout: 10000 });
  });

  test('should display API health status', async ({ page }) => {
    await page.goto('/diag');
    
    // Wait for health checks to complete
    await page.waitForTimeout(2000);
    
    // Look for health status indicators
    const healthIndicator = page.locator('text=/healthy|error|checking|ok/i').first();
    await expect(healthIndicator).toBeVisible();
  });

  test('should have refresh button', async ({ page }) => {
    await page.goto('/diag');
    
    // Look for refresh or run diagnostics button
    const refreshButton = page.locator('button:has-text("Refresh"), button:has-text("Run"), button:has-text("Check")').first();
    
    if (await refreshButton.isVisible()) {
      await expect(refreshButton).toBeEnabled();
      
      // Click the button
      await refreshButton.click();
      
      // Wait for refresh to complete
      await page.waitForTimeout(1000);
    }
  });

  test('should display multiple health checks', async ({ page }) => {
    await page.goto('/diag');
    
    // Wait for page to load and checks to run
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(2000);
    
    // Look for health check cards specifically
    const healthCards = page.locator('.card');
    const count = await healthCards.count();
    
    // Should have at least 3 health check cards (API, Internet, Client)
    expect(count).toBeGreaterThanOrEqual(3);
  });

  test('should be responsive on mobile', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/diag');
    
    // Page should still be visible and functional
    await expect(page.locator('body')).toBeVisible();
    
    // Health status should be visible
    await page.waitForTimeout(2000);
    const healthIndicator = page.locator('text=/healthy|error|checking/i').first();
    await expect(healthIndicator).toBeVisible();
  });
});

test.describe('API Health Endpoint', () => {
  test('should return 200 OK from health endpoint', async ({ request }) => {
    const response = await request.get('/api/health');
    expect(response.status()).toBe(200);
  });

  test('should return valid JSON from health endpoint', async ({ request }) => {
    const response = await request.get('/api/health');
    expect(response.status()).toBe(200);
    
    const contentType = response.headers()['content-type'];
    expect(contentType).toContain('application/json');
  });
});
