import { mkdtempSync, mkdirSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig, devices } from "@playwright/test";

const here = path.dirname(fileURLToPath(import.meta.url));
const bffProject = path.resolve(here, "..", "Mma.Cli.Bff");
const e2eRoot = process.env.MMA_E2E_ROOT ?? mkdtempSync(path.join(os.tmpdir(), "mma-e2e-"));
const allowedRoot = path.join(e2eRoot, "workspaces");
const workspaceDb = path.join(e2eRoot, "workspace.db");
const browserChannel = process.env.PLAYWRIGHT_CHANNEL;

mkdirSync(allowedRoot, { recursive: true });
process.env.MMA_E2E_ROOT = e2eRoot;
process.env.MMA_E2E_ALLOWED_ROOT = allowedRoot;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  timeout: 180_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { open: "never" }]],
  use: {
    baseURL: "http://127.0.0.1:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    viewport: { width: 1440, height: 900 }
  },
  projects: [{
    name: "chromium",
    use: { ...devices["Desktop Chrome"], ...(browserChannel ? { channel: browserChannel } : {}) }
  }],
  webServer: [
    {
      command: `dotnet run --project "${bffProject}" -c Release --no-launch-profile`,
      url: "http://127.0.0.1:5180/api/v1/health",
      reuseExistingServer: !process.env.CI,
      timeout: 240_000,
      env: {
        ASPNETCORE_URLS: "http://127.0.0.1:5180",
        ASPNETCORE_ENVIRONMENT: "Development",
        Logging__EventLog__LogLevel__Default: "None",
        MMA_BFF_DEV_AUTH: "1",
        MMA_BFF_ALLOWED_ROOTS: allowedRoot,
        Bff__WorkspaceDbPath: workspaceDb
      }
    },
    {
      command: "npm run dev",
      url: "http://127.0.0.1:5173",
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: {
        BROWSER: "none"
      }
    }
  ]
});
