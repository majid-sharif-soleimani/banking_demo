import { defineConfig, devices } from "@playwright/test";

// Runs against the whole bank started by the AppHost (see README.md in this folder). The agent host and the gateway
// keep the ports of their launchSettings.json under Aspire: 5180 and 5100.
export default defineConfig({
  testDir: "./tests",
  timeout: 60_000,
  retries: 0,
  reporter: [["list"]],
  use: {
    baseURL: process.env.AGENT_URL ?? "http://localhost:5180",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
