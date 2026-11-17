import { deflateSync } from "node:zlib";
import { mkdirSync, writeFileSync } from "node:fs";

function crc32(buf) {
  let crc = ~0;
  for (const byte of buf) {
    crc ^= byte;
    for (let i = 0; i < 8; i++) crc = (crc >>> 1) ^ (crc & 1 ? 0xedb88320 : 0);
  }
  return ~crc >>> 0;
}

function chunk(type, data) {
  const body = Buffer.concat([Buffer.from(type), data]);
  const out = Buffer.alloc(12 + data.length);
  out.writeUInt32BE(data.length, 0);
  body.copy(out, 4);
  out.writeUInt32BE(crc32(body), 8 + data.length);
  return out;
}

function rounded(x, y, size, margin, radius) {
  const left = margin;
  const right = size - margin;
  const top = margin;
  const bottom = size - margin;
  if (x < left || x >= right || y < top || y >= bottom) return false;
  const cx = x < left + radius ? left + radius : x >= right - radius ? right - radius : x;
  const cy = y < top + radius ? top + radius : y >= bottom - radius ? bottom - radius : y;
  return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2;
}

function letter(x, y, size) {
  const nx = (x - size / 2) / size;
  const ny = (y - size / 2) / size;
  const stem = nx > -0.16 && nx < -0.08 && ny > -0.22 && ny < 0.22;
  const top = ny > -0.22 && ny < -0.15 && nx > -0.16 && nx < 0.14;
  const mid = ny > -0.035 && ny < 0.035 && nx > -0.16 && nx < 0.1;
  const bot = ny > 0.15 && ny < 0.22 && nx > -0.16 && nx < 0.14;
  return stem || top || mid || bot;
}

function png(size) {
  const raw = Buffer.alloc((size * 4 + 1) * size);
  const margin = Math.round(size * 0.08);
  const radius = Math.round(size * 0.18);
  for (let y = 0; y < size; y++) {
    const row = y * (size * 4 + 1);
    raw[row] = 0;
    for (let x = 0; x < size; x++) {
      const i = row + 1 + x * 4;
      const inside = rounded(x, y, size, margin, radius);
      const ink = inside && letter(x, y, size);
      raw[i] = ink ? 248 : inside ? 31 : 244;
      raw[i + 1] = ink ? 250 : inside ? 78 : 246;
      raw[i + 2] = ink ? 252 : inside ? 70 : 245;
      raw[i + 3] = inside ? 255 : 0;
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw)),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

mkdirSync(new URL("../public/icons/", import.meta.url), { recursive: true });
writeFileSync(new URL("../public/icons/icon-192.png", import.meta.url), png(192));
writeFileSync(new URL("../public/icons/icon-512.png", import.meta.url), png(512));
