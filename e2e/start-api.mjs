import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(dir, "..");
const tmpDir = path.join(dir, ".tmp");
const dbPath = path.join(tmpDir, "e2e.db");
const storagePath = path.join(tmpDir, "storage");

fs.rmSync(tmpDir, { recursive: true, force: true });
fs.mkdirSync(storagePath, { recursive: true });

const env = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: "Development",
  ASPNETCORE_URLS: "http://127.0.0.1:5089",
  ConnectionStrings__Default: `Data Source=${dbPath}`,
  Cors__Origins__0: "http://127.0.0.1:5179",
  App__PublicWebUrl: "http://127.0.0.1:5179",
  FileStorage__Root: storagePath,
  Hangfire__Storage: "InMemory",
  Hangfire__EnableRecurringJobs: "false",
  RateLimiting__AuthPermitLimit: "1000",
  RateLimiting__PublicPermitLimit: "1000",
  RateLimiting__ScanPermitLimit: "2000",
  Jwt__SigningKey: "Admino_E2E_Signing_Key_Must_Be_At_Least_32_Bytes_Long!",
  SUPERADMIN_EMAIL: "superadmin@admino.lk",
  SUPERADMIN_PASSWORD: "SuperAdmin@123!",
};

const api = spawn(
  "dotnet",
  ["run", "--project", path.join(root, "src/EduFlow.Api/EduFlow.Api.csproj"), "-c", "Release", "--no-build", "--no-launch-profile"],
  {
    cwd: root,
    env,
    stdio: "inherit",
  },
);

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => {
    api.kill(signal);
  });
}

api.on("exit", (code) => {
  process.exit(code ?? 0);
});
