import type { AuthUser } from "./types";

export type Session = {
  accessToken: string;
  user: AuthUser;
  isReadOnly: boolean;
  isImpersonating: boolean;
};

const TENANT_KEY = "eduflow.tenant";
const BRANCH_KEY = "eduflow.branch";
const branchListeners = new Set<() => void>();
const slugListeners = new Set<() => void>();
const THEME_KEY = "eduflow.theme";
const ACTOR_KEY = "eduflow.actor";

let session: Session | null = null;
const listeners = new Set<() => void>();

export function getSession(): Session | null {
  return session;
}

export function getAccessToken(): string | null {
  return session?.accessToken ?? null;
}

export function setSession(next: Session | null): void {
  session = next;
  if (!next) setSelectedBranch(null);
  listeners.forEach((listener) => listener());
}

export function getSelectedBranch(): string | null {
  try {
    const value = sessionStorage.getItem(BRANCH_KEY);
    return value && value !== "all" ? value : null;
  } catch {
    return null;
  }
}

export function setSelectedBranch(id: string | null): void {
  try {
    if (!id) sessionStorage.removeItem(BRANCH_KEY);
    else sessionStorage.setItem(BRANCH_KEY, id);
  } catch {
    /* private mode */
  }
  branchListeners.forEach((listener) => listener());
}

export function subscribeBranch(listener: () => void): () => void {
  branchListeners.add(listener);
  return () => branchListeners.delete(listener);
}

export function subscribeSession(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getTenantSlug(): string {
  try {
    return localStorage.getItem(TENANT_KEY)?.trim().toLowerCase() ?? "";
  } catch {
    return "";
  }
}

export function setTenantSlug(slug: string): void {
  try {
    const normalized = slug.trim().toLowerCase();
    if (!normalized) localStorage.removeItem(TENANT_KEY);
    else localStorage.setItem(TENANT_KEY, normalized);
  } catch {
    /* private mode */
  }
  slugListeners.forEach((listener) => listener());
}

export function subscribeTenantSlug(listener: () => void): () => void {
  slugListeners.add(listener);
  return () => slugListeners.delete(listener);
}

export function getThemeOverride(): "light" | "dark" | null {
  try {
    const value = localStorage.getItem(THEME_KEY);
    return value === "light" || value === "dark" ? value : null;
  } catch {
    return null;
  }
}

export function setThemeOverride(mode: "light" | "dark"): void {
  try {
    localStorage.setItem(THEME_KEY, mode);
  } catch {
    /* private mode */
  }
}

export type ActorBackup = {
  accessToken: string;
  user: AuthUser;
  tenantSlug: string;
};

export function saveActorBackup(backup: ActorBackup): void {
  sessionStorage.setItem(ACTOR_KEY, JSON.stringify(backup));
}

export function readActorBackup(): ActorBackup | null {
  const raw = sessionStorage.getItem(ACTOR_KEY);
  if (!raw) return null;
  try {
    return JSON.parse(raw) as ActorBackup;
  } catch {
    return null;
  }
}

export function clearActorBackup(): void {
  sessionStorage.removeItem(ACTOR_KEY);
}
