import type { SmsSegmentCalculation } from "./types";

const GSM7_CHARS = new Set(
  "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà^{}\\[~]|€\f",
);

export function calculateSmsSegments(text: string): SmsSegmentCalculation {
  if (!text) {
    return {
      encoding: "GSM-7",
      isUnicode: false,
      characterCount: 0,
      segments: 0,
      perSegmentLimit: 160,
      remainingInCurrentSegment: 160,
    };
  }

  let isUnicode = false;
  for (let i = 0; i < text.length; i += 1) {
    if (!GSM7_CHARS.has(text[i]!)) {
      isUnicode = true;
      break;
    }
  }

  const charCount = text.length;
  const singleLimit = isUnicode ? 70 : 160;
  const multipartLimit = isUnicode ? 67 : 153;
  const encoding = isUnicode ? "UCS-2" : "GSM-7";

  if (charCount <= singleLimit) {
    return {
      encoding,
      isUnicode,
      characterCount: charCount,
      segments: 1,
      perSegmentLimit: singleLimit,
      remainingInCurrentSegment: singleLimit - charCount,
    };
  }

  const segments = Math.ceil(charCount / multipartLimit);
  return {
    encoding,
    isUnicode,
    characterCount: charCount,
    segments,
    perSegmentLimit: multipartLimit,
    remainingInCurrentSegment: segments * multipartLimit - charCount,
  };
}

export function renderNotificationTemplate(
  template: string,
  variables: Record<string, string>,
): string {
  if (!template) return "";
  return template.replace(/\{([a-zA-Z0-9_]+)\}/g, (full, key: string) => {
    const direct = variables[key];
    if (direct !== undefined) return direct;
    const lower = key.toLowerCase();
    for (const [k, v] of Object.entries(variables)) {
      if (k.toLowerCase() === lower) return v;
    }
    return full;
  });
}

export function minutesToTimeInput(minutes: number): string {
  const clamped = Math.max(0, Math.min(24 * 60 - 1, Math.floor(minutes)));
  const hrs = String(Math.floor(clamped / 60)).padStart(2, "0");
  const mins = String(clamped % 60).padStart(2, "0");
  return `${hrs}:${mins}`;
}

export function timeInputToMinutes(value: string): number {
  const parts = value.split(":");
  const hrs = Number(parts[0] ?? 0);
  const mins = Number(parts[1] ?? 0);
  if (Number.isNaN(hrs) || Number.isNaN(mins)) return 0;
  return Math.max(0, Math.min(24 * 60 - 1, hrs * 60 + mins));
}
