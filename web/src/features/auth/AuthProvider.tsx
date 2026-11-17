/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { ApiError, api, refreshAccessToken } from "@/lib/api";
import {
  clearActorBackup,
  getSession,
  readActorBackup,
  saveActorBackup,
  setSession,
  setTenantSlug,
  subscribeSession,
  getTenantSlug,
  type Session,
} from "@/lib/session";
import type { AuthResponse, AuthUser, MeResponse, RoleName } from "@/lib/types";

type ImpersonationResponse = {
  accessToken: string;
  expiresAt: string;
  tenantId: string;
  tenantSubdomain: string;
  isReadOnly: boolean;
};

export type AuthContextValue = {
  user: AuthUser | null;
  isReady: boolean;
  isReadOnly: boolean;
  isImpersonating: boolean;
  login: (email: string, password: string, institute: string, phone?: string) => Promise<RoleName>;
  adoptSession: (response: AuthResponse, institute: string) => Promise<RoleName>;
  logout: () => Promise<void>;
  hasPermission: (permission: string) => boolean;
  impersonate: (tenantId: string, subdomain: string) => Promise<void>;
  exitImpersonation: () => void;
};

export const AuthContext = createContext<AuthContextValue | null>(null);

export function homeFor(role: RoleName | string): string {
  if (role === "SuperAdmin") return "/admin/tenants";
  if (role === "Parent") return "/parent";
  return "/app";
}

async function withMe(response: AuthResponse, flags?: { isReadOnly: boolean; isImpersonating: boolean }): Promise<Session> {
  setSession({
    accessToken: response.accessToken,
    user: response.user,
    isReadOnly: flags?.isReadOnly ?? false,
    isImpersonating: flags?.isImpersonating ?? false,
  });
  const me = await api<MeResponse>("/api/auth/me");
  const next: Session = {
    accessToken: response.accessToken,
    user: {
      ...response.user,
      role: me.role,
      permissions: me.permissions,
      tenantId: me.tenantId,
      email: me.email || response.user.email,
    },
    isReadOnly: me.isReadOnly,
    isImpersonating: me.isImpersonating,
  };
  setSession(next);
  return next;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setLocal] = useState<Session | null>(getSession());
  const [isReady, setReady] = useState(false);

  useEffect(() => subscribeSession(() => setLocal(getSession())), []);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const refreshed = await refreshAccessToken();
        if (cancelled || !refreshed) return;
        await withMe(refreshed);
      } catch {
        setSession(null);
      } finally {
        if (!cancelled) setReady(true);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const value = useMemo<AuthContextValue>(() => {
    return {
      user: session?.user ?? null,
      isReady,
      isReadOnly: session?.isReadOnly ?? false,
      isImpersonating: session?.isImpersonating ?? false,
      async login(email, password, institute, phone) {
        setTenantSlug(institute);
        try {
          const response = await api<AuthResponse>(
            "/api/auth/login",
            {
              method: "POST",
              body: JSON.stringify(phone ? { phone, password } : { email, password }),
            },
            false,
          );
          const next = await withMe(response);
          return next.user.role;
        } catch (error) {
          if (!(error instanceof ApiError && error.feature === "ParentPortal")) {
            /* keep the institute code so the login page stays branded */
          }
          throw error;
        }
      },
      async adoptSession(response, institute) {
        setTenantSlug(institute);
        const next = await withMe(response, { isReadOnly: true, isImpersonating: false });
        return next.user.role;
      },
      async logout() {
        try {
          await api("/api/auth/logout", { method: "POST", body: "{}" }, false);
        } catch {
          /* local session still ends */
        }
        clearActorBackup();
        setSession(null);
      },
      hasPermission(permission: string) {
        const user = getSession()?.user;
        if (!user) return false;
        if (user.role === "SuperAdmin" || user.role === "InstituteAdmin") return true;
        return user.permissions.includes(permission);
      },
      async impersonate(tenantId: string, subdomain: string) {
        const current = getSession();
        if (!current) return;
        saveActorBackup({
          accessToken: current.accessToken,
          user: current.user,
          tenantSlug: getTenantSlug(),
        });
        const issued = await api<ImpersonationResponse>(
          `/api/super-admin/tenants/${tenantId}/impersonate`,
          { method: "POST" },
        );
        setTenantSlug(issued.tenantSubdomain || subdomain);
        setSession({
          accessToken: issued.accessToken,
          user: { ...current.user, role: "InstituteAdmin", tenantId: issued.tenantId },
          isReadOnly: true,
          isImpersonating: true,
        });
        const me = await api<MeResponse>("/api/auth/me");
        setSession({
          accessToken: issued.accessToken,
          user: {
            ...current.user,
            role: me.role,
            permissions: me.permissions,
            tenantId: me.tenantId,
            email: me.email || current.user.email,
          },
          isReadOnly: me.isReadOnly,
          isImpersonating: me.isImpersonating,
        });
      },
      exitImpersonation() {
        const actor = readActorBackup();
        clearActorBackup();
        setTenantSlug("");
        if (!actor) {
          setSession(null);
          return;
        }
        setSession({
          accessToken: actor.accessToken,
          user: actor.user,
          isReadOnly: false,
          isImpersonating: false,
        });
      },
    };
  }, [session, isReady]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) throw new Error("useAuth must be used within AuthProvider.");
  return context;
}
