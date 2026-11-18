import type { ReactNode } from "react";
import { t } from "@/config/strings";
import { Navigate, useLocation } from "react-router-dom";
import { PageSkeleton } from "@/components/skeletons";
import { useAuth } from "./AuthProvider";

export function RequireAuth({ children }: { children: ReactNode }) {
  const { user, isReady } = useAuth();
  const location = useLocation();
  if (!isReady) return <PageSkeleton />;
  if (!user) return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  if (user.mustChangePassword && location.pathname !== "/account/password") {
    return <Navigate to="/account/password" replace />;
  }
  return children;
}

export function GuestOnly({ children }: { children: ReactNode }) {
  const { user, isReady } = useAuth();
  if (!isReady) return <PageSkeleton />;
  if (user && !user.mustChangePassword) return <Navigate to={homePath(user.role)} replace />;
  return children;
}

export function RequireRole({ roles, children }: { roles: readonly string[]; children: ReactNode }) {
  const { user } = useAuth();
  if (!user || !roles.includes(user.role)) return <AccessDenied />;
  return children;
}

export function AccessDenied() {
  return (
    <section className="mx-auto max-w-lg rounded-lg border border-border bg-card p-6">
      <h1 className="text-lg font-semibold">{t("auth.deniedTitle")}</h1>
      <p className="mt-2 text-sm text-muted">{t("auth.deniedBody")}</p>
    </section>
  );
}

function homePath(role: string): string {
  if (role === "SuperAdmin") return "/admin/tenants";
  if (role === "Parent") return "/parent";
  return "/app";
}
