import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { applyDocumentLanguage } from "@/i18n/language";
import {
  brandPresets,
  contrastRatio,
  contrastSafePair,
  generateBrandScale,
} from "./color";

const samples = ["#1F4E46", "#F8E7C9", "#111111", "#FFFFFF", "#8A5A00", "#0F5F6B", "#7F1D2D"];

describe("brand scale contrast", () => {
  it("keeps primary text at WCAG AA on light and dark surfaces", () => {
    for (const hex of samples) {
      const scale = generateBrandScale(hex);
      expect(contrastRatio(scale.light.background, scale.light.foreground)).toBeGreaterThanOrEqual(4.5);
      expect(contrastRatio(scale.dark.background, scale.dark.foreground)).toBeGreaterThanOrEqual(4.5);
      expect(Object.keys(scale.steps)).toHaveLength(10);
    }
  });

  it("keeps curated presets and accent pairs readable", () => {
    expect(brandPresets).toHaveLength(6);
    for (const preset of brandPresets) {
      const accent = contrastSafePair(preset.accent);
      expect(contrastRatio(accent.background, accent.foreground)).toBeGreaterThanOrEqual(4.5);
      expect(generateBrandScale(preset.primary).light.background).toMatch(/^#[0-9A-F]{6}$/);
    }
  });
});

describe("document language", () => {
  it("sets the html lang so Tamil and Sinhala fonts apply", () => {
    applyDocumentLanguage("ta");
    expect(document.documentElement.lang).toBe("ta");
    applyDocumentLanguage("si");
    expect(document.documentElement.lang).toBe("si");
    const css = readFileSync(path.resolve("src/styles/globals.css"), "utf8");
    const main = readFileSync(path.resolve("src/main.tsx"), "utf8");
    expect(css).toContain('html[lang="ta"]');
    expect(css).toContain('html[lang="si"]');
    expect(main).toContain("@fontsource/noto-sans-tamil");
    expect(main).toContain("@fontsource/noto-sans-sinhala");
  });
});
