const INK = "#0F172A";
const PAPER = "#F8FAFC";

export const BRAND_STEPS = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900] as const;
export type BrandStep = (typeof BRAND_STEPS)[number];

const STEP_LIGHTNESS: Record<BrandStep, number> = {
  50: 0.97,
  100: 0.93,
  200: 0.86,
  300: 0.74,
  400: 0.62,
  500: 0.5,
  600: 0.42,
  700: 0.34,
  800: 0.26,
  900: 0.18,
};

export type ContrastPair = { background: string; foreground: string };

export type BrandScale = {
  steps: Record<BrandStep, string>;
  light: ContrastPair;
  dark: ContrastPair;
};

export const brandPresets = [
  { id: "ink", primary: "#1F4E46", accent: "#C45C26" },
  { id: "forest", primary: "#1B5E45", accent: "#C4A15A" },
  { id: "clay", primary: "#8C3E2F", accent: "#C4A276" },
  { id: "lagoon", primary: "#0F5F6B", accent: "#D97706" },
  { id: "marigold", primary: "#8A5A00", accent: "#1F4E46" },
  { id: "wine", primary: "#7F1D2D", accent: "#C4A484" },
] as const;

const HEX = /^#[0-9A-Fa-f]{6}$/;

export function normalizeHex(value: string): string {
  const trimmed = value.trim();
  if (!HEX.test(trimmed)) {
    throw new Error("Color must be #RRGGBB.");
  }
  return trimmed.toUpperCase();
}

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const normalized = normalizeHex(hex);
  return {
    r: Number.parseInt(normalized.slice(1, 3), 16),
    g: Number.parseInt(normalized.slice(3, 5), 16),
    b: Number.parseInt(normalized.slice(5, 7), 16),
  };
}

function channel(value: number): number {
  const s = value / 255;
  return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
}

export function relativeLuminance(hex: string): number {
  const { r, g, b } = hexToRgb(hex);
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

export function contrastRatio(a: string, b: string): number {
  const left = relativeLuminance(a);
  const right = relativeLuminance(b);
  const lighter = Math.max(left, right);
  const darker = Math.min(left, right);
  return (lighter + 0.05) / (darker + 0.05);
}

export function readableForeground(background: string): string {
  return contrastRatio(background, INK) >= contrastRatio(background, PAPER) ? INK : PAPER;
}

export function contrastSafePair(background: string): ContrastPair {
  let current = normalizeHex(background);
  for (let attempt = 0; attempt < 16; attempt += 1) {
    const foreground = readableForeground(current);
    if (contrastRatio(current, foreground) >= 4.5) {
      return { background: current, foreground };
    }
    const hsl = hexToHsl(current);
    const preferPaper = contrastRatio(current, PAPER) >= contrastRatio(current, INK);
    const nextL = preferPaper ? Math.max(0, hsl.l - 0.06) : Math.min(1, hsl.l + 0.06);
    const next = hslToHex(hsl.h, hsl.s, nextL);
    if (next === current) {
      break;
    }
    current = next;
  }
  return { background: current, foreground: readableForeground(current) };
}

export function generateBrandScale(primary: string): BrandScale {
  const normalized = normalizeHex(primary);
  const hsl = hexToHsl(normalized);
  const saturation = hsl.s < 0.12 ? hsl.s : clamp(hsl.s, 0.28, 0.72);
  const steps = {} as Record<BrandStep, string>;
  for (const step of BRAND_STEPS) {
    steps[step] = hslToHex(hsl.h, saturation, STEP_LIGHTNESS[step]);
  }
  const light = contrastSafePair(normalized);
  const darkSource = relativeLuminance(light.background) < 0.18 ? steps[300] : light.background;
  return { steps, light, dark: contrastSafePair(darkSource) };
}

export function applyBrandToDocument(input: {
  primaryColor: string;
  accentColor: string;
  fontPreference: string;
  mode: "light" | "dark";
}): void {
  const scale = generateBrandScale(input.primaryColor);
  const pair = input.mode === "dark" ? scale.dark : scale.light;
  const accent = contrastSafePair(input.accentColor);
  const root = document.documentElement;
  for (const step of BRAND_STEPS) {
    root.style.setProperty(`--brand-${step}`, scale.steps[step]);
  }
  root.style.setProperty("--primary", pair.background);
  root.style.setProperty("--primary-foreground", pair.foreground);
  root.style.setProperty("--accent", accent.background);
  root.style.setProperty("--accent-foreground", accent.foreground);
  root.style.setProperty("--ring", scale.steps[600]);
  const surface =
    input.mode === "dark"
      ? {
          "--background": "#101614",
          "--foreground": "#E6EEEA",
          "--card": "#18211D",
          "--muted": "#A3B0AA",
          "--border": "#2C3A34",
          "--secondary": "#24302B",
          "--secondary-foreground": "#E6EEEA",
        }
      : {
          "--background": "#FAF9F6",
          "--foreground": "#14213D",
          "--card": "#FFFFFF",
          "--muted": "#475569",
          "--border": "#E7E5E0",
          "--secondary": "#F1EFEA",
          "--secondary-foreground": "#14213D",
        };
  for (const [key, value] of Object.entries(surface)) {
    root.style.setProperty(key, value);
  }
  root.classList.toggle("dark", input.mode === "dark");
  root.dataset.font = input.fontPreference === "System" ? "System" : "Inter";
  root.style.colorScheme = input.mode;
}

export function resolveThemeMode(
  mode: string,
  prefersDark: boolean,
): "light" | "dark" {
  if (mode === "dark") return "dark";
  if (mode === "system") return prefersDark ? "dark" : "light";
  return "light";
}

function hexToHsl(hex: string): { h: number; s: number; l: number } {
  const { r, g, b } = hexToRgb(hex);
  const rn = r / 255;
  const gn = g / 255;
  const bn = b / 255;
  const max = Math.max(rn, gn, bn);
  const min = Math.min(rn, gn, bn);
  const l = (max + min) / 2;
  if (max === min) return { h: 0, s: 0, l };
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h = 0;
  if (max === rn) h = (gn - bn) / d + (gn < bn ? 6 : 0);
  else if (max === gn) h = (bn - rn) / d + 2;
  else h = (rn - gn) / d + 4;
  return { h: h / 6, s, l };
}

function hslToHex(h: number, s: number, l: number): string {
  if (s === 0) {
    const gray = toHex(Math.round(l * 255));
    return `#${gray}${gray}${gray}`;
  }
  const q = l < 0.5 ? l * (1 + s) : l + s - l * s;
  const p = 2 * l - q;
  const r = hueToRgb(p, q, h + 1 / 3);
  const g = hueToRgb(p, q, h);
  const b = hueToRgb(p, q, h - 1 / 3);
  return `#${toHex(Math.round(r * 255))}${toHex(Math.round(g * 255))}${toHex(Math.round(b * 255))}`;
}

function hueToRgb(p: number, q: number, t: number): number {
  let channelT = t;
  if (channelT < 0) channelT += 1;
  if (channelT > 1) channelT -= 1;
  if (channelT < 1 / 6) return p + (q - p) * 6 * channelT;
  if (channelT < 1 / 2) return q;
  if (channelT < 2 / 3) return p + (q - p) * (2 / 3 - channelT) * 6;
  return p;
}

function toHex(value: number): string {
  return value.toString(16).padStart(2, "0");
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
