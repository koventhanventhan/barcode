export function monogramFor(displayName: string): string {
  const cleaned = displayName.trim().replace(/[^a-zA-Z0-9]/g, "");
  if (cleaned.length >= 2) return cleaned.slice(0, 2).toUpperCase();
  if (cleaned.length === 1) return cleaned.toUpperCase();
  return "EF";
}

export function extractInstituteCodeFromHost(hostname: string): string {
  const normalized = hostname.trim().toLowerCase();
  if (!normalized || normalized === "localhost" || normalized === "::1" || /^\d+\.\d+\.\d+\.\d+$/.test(normalized)) {
    return "";
  }

  if (normalized.endsWith(".localhost")) {
    const sub = normalized.slice(0, -".localhost".length);
    return sub && sub !== "www" && !sub.includes(".") ? sub : "";
  }

  const parts = normalized.split(".");
  if (parts.length >= 3) {
    const candidate = parts[0];
    if (candidate && candidate !== "www" && candidate !== "api") {
      return candidate;
    }
  }

  return "";
}
