import { defineConfig, devices } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/browser",
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: "list",
  use: {
    baseURL: "http://127.0.0.1:5076",
    channel: process.env.PLAYWRIGHT_CHANNEL || undefined,
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    command:
      "dotnet run --project src/DocSense.Web --no-launch-profile --urls http://127.0.0.1:5076",
    url: "http://127.0.0.1:5076/health",
    reuseExistingServer: !process.env.CI,
    timeout: 60000,
  },
});
