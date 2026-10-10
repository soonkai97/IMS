import { defineConfig, devices } from '@playwright/test';
import { WEB_URL } from './src/config';

// One worker by default: scenarios use unique data, but a few switch global settings (negative stock) and every sign-in
// shares the API's login rate limit (20 per minute per address). Raise IMS_E2E_WORKERS only against a relaxed API.
export default defineConfig({
  testDir: './tests',
  globalSetup: './src/global-setup.ts',
  fullyParallel: false,
  workers: Number(process.env['IMS_E2E_WORKERS'] ?? 1),
  retries: process.env['CI'] ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: WEB_URL,
    locale: 'en-US',
    timezoneId: 'Asia/Kuala_Lumpur',
    // The service worker would cache assets/config.json and bypass the route that points the app at IMS_API_URL.
    serviceWorkers: 'block',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } }
    }
  ]
});
