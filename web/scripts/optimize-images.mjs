import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const dir = path.dirname(fileURLToPath(import.meta.url));
const imagesDir = path.resolve(dir, "../public/images");

async function optimizeDirectory(targetDir) {
  if (!fs.existsSync(targetDir)) return;
  const entries = fs.readdirSync(targetDir, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(targetDir, entry.name);
    if (entry.isDirectory()) {
      await optimizeDirectory(full);
      continue;
    }
    if (!/\.(jpe?g|png)$/i.test(entry.name)) continue;

    const base = full.replace(/\.(jpe?g|png)$/i, "");
    const meta = await sharp(full).metadata();
    const width = Math.min(meta.width ?? 1600, 1600);

    // WebP < 200 KB
    let quality = 80;
    let webpBuf = await sharp(full).resize({ width, withoutEnlargement: true }).webp({ quality }).toBuffer();
    while (webpBuf.length > 200 * 1024 && quality > 45) {
      quality -= 8;
      webpBuf = await sharp(full).resize({ width, withoutEnlargement: true }).webp({ quality }).toBuffer();
    }
    fs.writeFileSync(`${base}.webp`, webpBuf);

    // AVIF < 200 KB
    let avifQuality = 62;
    let avifBuf = await sharp(full).resize({ width, withoutEnlargement: true }).avif({ quality: avifQuality }).toBuffer();
    while (avifBuf.length > 200 * 1024 && avifQuality > 35) {
      avifQuality -= 7;
      avifBuf = await sharp(full).resize({ width, withoutEnlargement: true }).avif({ quality: avifQuality }).toBuffer();
    }
    fs.writeFileSync(`${base}.avif`, avifBuf);

    console.log(
      `${path.relative(imagesDir, full)} -> .webp ${(webpBuf.length / 1024).toFixed(1)} KB, .avif ${(avifBuf.length / 1024).toFixed(1)} KB`,
    );
  }
}

await optimizeDirectory(imagesDir);
