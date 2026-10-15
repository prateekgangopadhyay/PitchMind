import { defineConfig, devices } from '@playwright/test';

// The live PostgreSQL/API journey is orchestrated by its dedicated Python
// runner. Keep it out of the ordinary stubbed browser suite unless that
// runner explicitly opts in and has provisioned the isolated fixture/API.
const includeLiveApiJourney = process.env.PITCHMIND_LIVE_API_E2E === '1';

export default defineConfig({
  testDir: './e2e',
  testIgnore: includeLiveApiJourney ? undefined : ['**/ep07-s05-live-postgres-journey.spec.ts'],
  fullyParallel: false,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  workers: 1,
  reporter: 'list',
  use: {
    ...devices['Desktop Chrome'],
    baseURL: 'http://127.0.0.1:4173',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  webServer: {
    command: 'npm run dev -- --host 127.0.0.1 --port 4173 --strictPort',
    url: 'http://127.0.0.1:4173/',
    cwd: './',
    reuseExistingServer: !process.env.CI,
    timeout: 30_000
  }
});
