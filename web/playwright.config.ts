import { defineConfig, devices } from '@playwright/test';

const embedded = process.env['PM_E2E_MODE'] === 'embedded';
const root = process.env['PM_E2E_ROOT'];
if (!root) throw new Error('Run Playwright through npm run e2e or npm run e2e:embedded.');
const uiPort = process.env['PM_E2E_UI_PORT'];
if (!uiPort) throw new Error('PM_E2E_UI_PORT must be set by the E2E runner.');
const baseURL = `http://127.0.0.1:${uiPort}`;

export default defineConfig({
  testDir: './e2e',
  testMatch: embedded ? '**/embedded.smoke.spec.ts' : '**/workflows.spec.ts',
  fullyParallel: false,
  workers: 1,
  retries: process.env['CI'] ? 2 : 0,
  reporter: [['line'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: `node scripts/e2e-host.mjs ${embedded ? 'embedded' : 'dev'}`,
    url: `${baseURL}/api/v1/project`,
    reuseExistingServer: false,
    timeout: 120_000,
    env: { ...process.env, PM_E2E_ROOT: root },
  },
  projects: embedded
    ? [{ name: 'embedded-chromium', use: { ...devices['Desktop Chrome'] } }]
    : [
        { name: 'desktop-chromium', use: { ...devices['Desktop Chrome'] } },
        {
          name: 'mobile-chromium',
          use: {
            ...devices['Desktop Chrome'],
            viewport: { width: 390, height: 844 },
            isMobile: true,
          },
        },
      ],
});
