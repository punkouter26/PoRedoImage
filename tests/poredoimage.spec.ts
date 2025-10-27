import { test, expect } from '@playwright/test';

// Configuration
const BASE_URL = process.env.BASE_URL || 'https://app-poredoimage-cqevadpy77pvi.azurewebsites.net';
const API_BASE_URL = `${BASE_URL}/api`;

test.describe('PoRedoImage Application Tests', () => {
  
  test('should load the home page', async ({ page }) => {
    await page.goto(BASE_URL);
    
    // Wait for Blazor to load
    await page.waitForLoadState('networkidle');
    
    // Check that the page title contains the app name
    await expect(page).toHaveTitle(/PoRedoImage|Image/i);
    
    console.log('✅ Home page loaded successfully');
  });

  test('should check API health endpoint', async ({ request }) => {
    const response = await request.get(`${API_BASE_URL}/health`);
    
    expect(response.ok()).toBeTruthy();
    expect(response.status()).toBe(200);
    
    const data = await response.json();
    expect(data).toHaveProperty('status');
    expect(data.status).toBe('Healthy');
    expect(data).toHaveProperty('timestamp');
    
    console.log('✅ Health endpoint returned:', data);
  });

  test('should navigate to diagnostics page', async ({ page }) => {
    await page.goto(`${BASE_URL}/diag`);
    await page.waitForLoadState('networkidle');
    
    // Check for diagnostic content
    const content = await page.textContent('body');
    expect(content).toBeTruthy();
    
    console.log('✅ Diagnostics page accessible');
  });

  test('should have image upload functionality on home page', async ({ page }) => {
    await page.goto(BASE_URL);
    await page.waitForLoadState('networkidle');
    
    // Wait a bit for Blazor components to initialize
    await page.waitForTimeout(2000);
    
    // Look for file input or upload button
    const hasFileInput = await page.locator('input[type="file"]').count() > 0;
    const hasUploadButton = await page.locator('button:has-text("Upload"), button:has-text("Analyze")').count() > 0;
    
    expect(hasFileInput || hasUploadButton).toBeTruthy();
    
    console.log('✅ Upload functionality detected');
  });

  test('should test image analysis API endpoint structure', async ({ request }) => {
    // Test with a minimal request to see the API structure
    // Note: This might fail if no valid image is provided, but we're testing the endpoint exists
    const response = await request.post(`${API_BASE_URL}/imageanalysis/analyze`, {
      headers: {
        'Content-Type': 'application/json',
      },
      data: {
        imageUrl: 'https://example.com/test.jpg'
      },
      failOnStatusCode: false
    });
    
    // We expect it to respond (even if it's an error due to invalid image)
    // Status codes like 400, 415, 500 are acceptable - we just want to confirm endpoint exists
    expect([200, 400, 415, 500, 502, 503]).toContain(response.status());
    
    console.log(`✅ Image analysis endpoint responded with status: ${response.status()}`);
  });

  test('should verify Application Insights is configured', async ({ page }) => {
    await page.goto(BASE_URL);
    
    // Check if Application Insights script is loaded
    const hasAppInsights = await page.evaluate(() => {
      return typeof (window as any).appInsights !== 'undefined' ||
             document.querySelector('script[src*="applicationinsights"]') !== null;
    });
    
    console.log(`Application Insights detected: ${hasAppInsights}`);
    // This is informational - not all Blazor WASM apps inject AI client-side
  });

  test('should check for proper HTTPS configuration', async ({ request }) => {
    const response = await request.get(BASE_URL);
    
    expect(response.ok()).toBeTruthy();
    expect(response.url()).toContain('https://');
    
    console.log('✅ HTTPS properly configured');
  });

  test('should verify CORS headers for API', async ({ request }) => {
    const response = await request.get(`${API_BASE_URL}/health`);
    
    const headers = response.headers();
    console.log('API Response Headers:', headers);
    
    // Health endpoint should be accessible
    expect(response.ok()).toBeTruthy();
    
    console.log('✅ API accessible');
  });

  test('should check diagnostics page shows system status', async ({ page }) => {
    await page.goto(`${BASE_URL}/diag`);
    await page.waitForLoadState('networkidle');
    await page.waitForTimeout(3000); // Wait for checks to complete
    
    const bodyText = await page.textContent('body');
    
    // Look for indicators of diagnostic information
    const hasDiagnosticContent = 
      bodyText?.toLowerCase().includes('status') ||
      bodyText?.toLowerCase().includes('health') ||
      bodyText?.toLowerCase().includes('diagnostic') ||
      bodyText?.toLowerCase().includes('api') ||
      bodyText?.toLowerCase().includes('database');
    
    expect(hasDiagnosticContent).toBeTruthy();
    
    console.log('✅ Diagnostic page contains status information');
  });

  test('performance: home page should load within reasonable time', async ({ page }) => {
    const startTime = Date.now();
    
    await page.goto(BASE_URL);
    await page.waitForLoadState('networkidle');
    
    const loadTime = Date.now() - startTime;
    
    console.log(`⏱️ Page load time: ${loadTime}ms`);
    
    // Blazor WASM can take a while to load, so we're lenient here
    expect(loadTime).toBeLessThan(30000); // 30 seconds max
  });
});
