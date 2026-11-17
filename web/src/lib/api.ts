import { getAccessToken, getSelectedBranch, getTenantSlug, setSession } from "./session";
import type { ApiErrorBody, AuthResponse } from "./types";

export class ApiError extends Error {
  status: number;
  code?: string;
  feature?: string;

  constructor(status: number, message: string, code?: string, feature?: string) {
    super(message);
    this.status = status;
    this.code = code;
    this.feature = feature;
  }
}

let refreshPromise: Promise<AuthResponse | null> | null = null;

function url(path: string): string {
  const base = import.meta.env.VITE_API_URL ?? "";
  return `${base}${path}`;
}

export async function api<T>(path: string, init: RequestInit = {}, retry = true): Promise<T> {
  const headers = new Headers(init.headers);
  const token = getAccessToken();
  if (token) headers.set("Authorization", `Bearer ${token}`);
  const tenant = getTenantSlug();
  if (tenant) headers.set("X-Tenant", tenant);
  const branch = getSelectedBranch();
  if (branch) headers.set("X-Branch", branch);
  if (init.body && !(init.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const response = await fetch(url(path), { ...init, headers, credentials: "include" });
  if (response.status === 401 && retry && !path.startsWith("/api/auth/")) {
    const refreshed = await refreshAccessToken();
    if (refreshed) return api<T>(path, init, false);
  }
  if (!response.ok) throw await toError(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export async function refreshAccessToken(): Promise<AuthResponse | null> {
  if (!refreshPromise) {
    refreshPromise = (async () => {
      const response = await fetch(url("/api/auth/refresh"), {
        method: "POST",
        credentials: "include",
        headers: { "Content-Type": "application/json" },
        body: "{}",
      });
      if (!response.ok) {
        setSession(null);
        return null;
      }
      return (await response.json()) as AuthResponse;
    })().finally(() => {
      refreshPromise = null;
    });
  }
  return refreshPromise;
}

async function toError(response: Response): Promise<ApiError> {
  let body: ApiErrorBody = {};
  try {
    body = (await response.json()) as ApiErrorBody;
  } catch {
    body = {};
  }
  const message = body.message || body.detail || body.title || "The request could not be completed.";
  return new ApiError(response.status, message, body.code, body.feature);
}
