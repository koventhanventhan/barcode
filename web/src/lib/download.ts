import { ApiError } from "./api";
import { getAccessToken, getTenantSlug } from "./session";

function headersFor(init: RequestInit) {
  const headers = new Headers(init.headers);
  const token = getAccessToken();
  if (token) headers.set("Authorization", `Bearer ${token}`);
  const tenant = getTenantSlug();
  if (tenant) headers.set("X-Tenant", tenant);
  return headers;
}

export async function downloadApi(path: string, filename: string, init: RequestInit = {}) {
  const headers = headersFor(init);
  if (init.body && !(init.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  const base = import.meta.env.VITE_API_URL ?? "";
  const response = await fetch(`${base}${path}`, { ...init, headers, credentials: "include" });
  if (!response.ok) {
    let message = "The file could not be downloaded.";
    let code: string | undefined;
    try {
      const body = (await response.json()) as { message?: string; code?: string };
      message = body.message || message;
      code = body.code;
    } catch {
      message = response.statusText || message;
    }
    throw new ApiError(response.status, message, code);
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export function downloadQrSvg(svg: SVGSVGElement | null, filename = "staff-registration-qr.svg") {
  if (!svg) return;
  const serialized = new XMLSerializer().serializeToString(svg);
  const blob = new Blob([serialized], { type: "image/svg+xml;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
