import { defineConfig, devices } from '@playwright/test';

// Os horários dos cenários usam o fuso padrão das contas semeadas. O runner
// Linux usa UTC; sem isto, 09h do teste vira 06h no consultório.
process.env.TZ = 'America/Sao_Paulo';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [['list']],
  use: {
    timezoneId: 'America/Sao_Paulo',
    baseURL: process.env.PLAYWRIGHT_BASE_URL ?? 'http://localhost:4200',


    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  projects: [
    {
      name: 'desktop',
      use: { ...devices['Desktop Chrome'], viewport: { width: 1440, height: 1024 } },
    },
    {
      name: 'mobile',
      use: { ...devices['Pixel 7'], viewport: { width: 390, height: 844 } },
    },
  ],
});
