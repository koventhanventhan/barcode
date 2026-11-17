import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const dir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(dir, "../..");
const e2eDir = path.join(root, "e2e");
const docsScreens = path.join(root, "docs", "screenshots");
const publicScreens = path.resolve(dir, "../public/images/screens");

if (fs.existsSync(e2eDir)) {
  execFileSync("npm", ["test"], { cwd: e2eDir, stdio: "inherit", shell: true });
}

fs.mkdirSync(publicScreens, { recursive: true });
for (const name of ["dashboard.png", "scanner.png", "parent-mobile.png"]) {
  const src = path.join(docsScreens, name);
  if (fs.existsSync(src)) {
    fs.copyFileSync(src, path.join(publicScreens, name));
  }
}

execFileSync("node", [path.join(dir, "optimize-images.mjs")], {
  cwd: path.resolve(dir, ".."),
  stdio: "inherit",
});
