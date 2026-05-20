import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  timeout: 30_000,
  retries: process.env['CI'] ? 2 : 0,
  workers: process.env['CI'] ? 1 : undefined,
  reporter: [['html', { open: 'never' }], ['list']],
  use: {
    baseURL: 'http://localhost:5000',
    // Headed in dev, headless in CI or when HEADLESS env var is set
    headless: !!process.env['CI'] || !!process.env['HEADLESS'],
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  // Start the .NET server before running tests; kill it after.
  webServer: {
    command: 'dotnet run --project ../../src/PoRedoImage.Web --launch-profile http',
    url: 'http://localhost:5000',
    reuseExistingServer: !process.env['CI'],
    timeout: 60_000,
  },
});
