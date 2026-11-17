import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium, defineConfig } from "@playwright/test";

const dir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(dir, "..");
const hasBundledChromium = fs.existsSync(chromium.executablePath());
const systemChrome = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const useSystemChrome = !process.env.CI && !hasBundledChromium && fs.existsSync(systemChrome);

export default defineConfig({
  testDir: "./tests",
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  timeout: 60_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]],
  use: {
    baseURL: "http://127.0.0.1:5179",
    acceptDownloads: true,
    trace: "on-first-retry",
    ...(useSystemChrome ? { channel: "chrome" } : {}),
  },
  webServer: [
    {
      command: "node start-api.mjs",
      cwd: dir,
      url: "http://127.0.0.1:5089/health",
      reuseExistingServer: false,
      timeout: 120_000,
    },
    {
      command: "npm run dev -- --host 127.0.0.1 --port 5179",
      cwd: path.join(root, "web"),
      env: {
        ...process.env,
        VITE_PROXY_TARGET: "http://127.0.0.1:5089",
      },
      url: "http://127.0.0.1:5179",
      reuseExistingServer: false,
      timeout: 60_000,
    },
  ],
});
