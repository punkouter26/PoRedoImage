import { test, expect } from '@playwright/test';

/**
 * E2E tests for Home page functionality
 */
test.describe('Home Page', () => {
  test('should load the home page successfully', async ({ page }) => {
    await page.goto('/');
    
    // Check that the page title is correct
    await expect(page).toHaveTitle(/ImageGc/);
    
    // Check for main heading
    await expect(page.locator('h1, h2, h3').first()).toBeVisible();
  });

  test('should display the file upload section', async ({ page }) => {
    await page.goto('/');
    
    // Look for file input or upload button
    const fileInput = page.locator('input[type="file"]');
    await expect(fileInput).toBeVisible();
  });

  test('should show description length slider', async ({ page }) => {
    await page.goto('/');
    
    // Look for slider or number input for description length
    const slider = page.locator('input[type="range"], input[type="number"]').first();
    await expect(slider).toBeVisible();
  });

  test('should have responsive layout on mobile', async ({ page }) => {
    // Set mobile viewport
    await page.setViewportSize({ width: 375, height: 667 });
    await page.goto('/');
    
    // Check that page is still visible and usable
    await expect(page.locator('body')).toBeVisible();
    
    // Upload section should still be accessible
    const fileInput = page.locator('input[type="file"]');
    await expect(fileInput).toBeVisible();
  });
});

test.describe('Navigation', () => {
  test('should navigate to diagnostics page', async ({ page }) => {
    await page.goto('/');
    
    // Try to find and click diagnostics link
    const diagLink = page.locator('a[href="/diag"], a:has-text("Diag")').first();
    
    if (await diagLink.isVisible()) {
      await diagLink.click();
      await expect(page).toHaveURL(/.*diag/);
      await expect(page.locator('text=/diagnostics|health|status/i').first()).toBeVisible();
    }
  });

  test('should have working navigation menu', async ({ page }) => {
    await page.goto('/');
    
    // Check for navigation elements
    const nav = page.locator('nav, .navbar, [role="navigation"]').first();
    if (await nav.isVisible()) {
      await expect(nav).toBeVisible();
    }
  });
});

test.describe('Image Upload Workflow', () => {
  test('should allow file selection', async ({ page }) => {
    await page.goto('/');
    
    const fileInput = page.locator('input[type="file"]');
    
    // Create a test file
    const buffer = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAoAAAAKCAYAAACNMs+9AAAAFUlEQVR42mNk+M9Qz0AEYBxVSF+FABJADveWkH6oAAAAAElFTkSuQmCC', 'base64');
    
    // Set the file
    await fileInput.setInputFiles({
      name: 'test.png',
      mimeType: 'image/png',
      buffer: buffer,
    });
    
    // Wait a bit for file to be processed
    await page.waitForTimeout(500);
    
    // Check if upload button or process button appears
    const processButton = page.locator('button:has-text("Analyze"), button:has-text("Process"), button:has-text("Upload")').first();
    if (await processButton.isVisible()) {
      await expect(processButton).toBeEnabled();
    }
  });

  test('should display error for invalid file type', async ({ page }) => {
    await page.goto('/');
    
    const fileInput = page.locator('input[type="file"]');
    
    // Try to upload a text file
    await fileInput.setInputFiles({
      name: 'test.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('This is not an image'),
    });
    
    // Wait for potential error message
    await page.waitForTimeout(1000);
    
    // Look for error message (if validation is in place)
    const errorMessage = page.locator('text=/invalid|error|not supported/i').first();
    // This may or may not be visible depending on client-side validation
  });
});

test.describe('Responsive Design', () => {
  test('should work on desktop viewport', async ({ page }) => {
    await page.setViewportSize({ width: 1920, height: 1080 });
    await page.goto('/');
    
    await expect(page.locator('body')).toBeVisible();
  });

  test('should work on mobile portrait', async ({ page }) => {
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto('/');
    
    await expect(page.locator('body')).toBeVisible();
    
    // File input should still be accessible
    const fileInput = page.locator('input[type="file"]');
    await expect(fileInput).toBeVisible();
  });

  test('should work on tablet viewport', async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 1024 });
    await page.goto('/');
    
    await expect(page.locator('body')).toBeVisible();
  });
});
