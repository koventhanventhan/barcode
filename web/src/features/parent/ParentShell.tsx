/* eslint-disable react-refresh/only-export-components */
import { useQuery } from "@tanstack/react-query";
import { BookOpen, CalendarDays, ClipboardCheck, CreditCard, House, Inbox, LogOut, Moon, Sun } from "lucide-react";
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { useFeature } from "@/components/feature-gate";
import { PageSkeleton } from "@/components/skeletons";
import { ProductName } from "@/config/brand";
import { useAuth } from "@/features/auth/AuthProvider";
import { useBranding } from "@/features/branding/BrandingProvider";
import { ApiError, api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { readSelectedChild, type ParentHome } from "./model";

const STORAGE_KEY = "eduflow.parentStudent";

type ParentContextValue = {
  home: ParentHome;
  studentId: string;
  setStudentId: (id: string) => void;
};

const ParentContext = createContext<ParentContextValue | null>(null);

export function useParentPortal() {
  const value = useContext(ParentContext);
  if (!value) throw new Error("Parent portal is not ready.");
  return value;
}

export function ParentShell() {
  const { t } = useTranslation();
  const auth = useAuth();
  const branding = useBranding();
  const navigate = useNavigate();
  const feature = useFeature("ParentPortal");
  const [offline, setOffline] = useState(!navigator.onLine);
  const dark = branding.resolvedMode === "dark";
  const home = useQuery({
    queryKey: ["parent-home"],
    enabled: feature.isEnabled && auth.user?.role === "Parent",
    retry: false,
    queryFn: () => api<ParentHome>("/api/parent/children"),
  });
  const [studentId, setStudentIdState] = useState(() => localStorage.getItem(STORAGE_KEY) ?? "");

  useEffect(() => {
    const on = () => setOffline(false);
    const off = () => setOffline(true);
    window.addEventListener("online", on);
    window.addEventListener("offline", off);
    return () => {
      window.removeEventListener("online", on);
      window.removeEventListener("offline", off);
    };
  }, []);

  const ids = home.data?.children.map((child) => child.id) ?? [];
  const selected = readSelectedChild(ids, studentId);

  useEffect(() => {
    if (!home.data) return;
    if (selected !== studentId) {
      setStudentIdState(selected);
    }
    if (selected) localStorage.setItem(STORAGE_KEY, selected);
  }, [home.data, selected, studentId]);

  const blocked =
    (!feature.isLoading && !feature.isEnabled) ||
    (home.error instanceof ApiError && (home.error.code === "FEATURE_DISABLED" || home.error.code === "FEATURE_NOT_IN_PLAN"));

  const value = useMemo<ParentContextValue | null>(() => {
    if (!home.data) return null;
    return {
      home: home.data,
      studentId: selected,
      setStudentId: (id: string) => {
        setStudentIdState(id);
        localStorage.setItem(STORAGE_KEY, id);
      },
    };
  }, [home.data, selected]);

  const institute = branding.branding?.instituteDisplayName || ProductName;

  if (auth.user && auth.user.role !== "Parent") return <Outlet />;

  if (feature.isLoading || (feature.isEnabled && home.isLoading)) return <PageSkeleton />;

  if (blocked || !value) {
    return (
      <main className="mx-auto flex min-h-screen max-w-lg items-center px-4">
        <section className="w-full rounded-lg border border-border bg-card p-5">
          <h1 className="text-xl font-semibold">{t("parent.offTitle")}</h1>
          <p className="mt-2 text-sm text-muted">{t("parent.offBody")}</p>
          <button
            type="button"
            className="mt-4 text-sm text-primary"
            onClick={() => void auth.logout().then(() => navigate("/login"))}
          >
            {t("shell.logout")}
          </button>
        </section>
      </main>
    );
  }

  const links = [
    { to: "/parent", label: t("parent.navHome"), icon: House, end: true },
    { to: "/parent/attendance", label: t("parent.navAttendance"), icon: CalendarDays, end: false },
    { to: "/parent/classes", label: t("parent.navClasses"), icon: BookOpen, end: false },
    { to: "/parent/results", label: t("parent.navResults"), icon: ClipboardCheck, end: false },
    { to: "/parent/fees", label: t("parent.navFees"), icon: CreditCard, end: false },
    { to: "/parent/messages", label: t("parent.navMessages"), icon: Inbox, end: false },
  ];

  return (
    <ParentContext.Provider value={value}>
      <div className="min-h-screen bg-background pb-24">
        <a
          href="#parent-main"
          className="sr-only focus:not-sr-only focus:absolute focus:left-3 focus:top-3 focus:z-30 focus:rounded-md focus:bg-card focus:px-3 focus:py-2"
        >
          {t("parent.skip")}
        </a>
        <header className="border-b border-border bg-card">
          <div className="mx-auto flex max-w-3xl items-center justify-between gap-3 px-4 py-3">
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold">{institute}</p>
              <p className="truncate text-[12px] text-muted">{auth.user?.fullName}</p>
            </div>
            <div className="flex items-center gap-1">
              <button
                type="button"
                className="inline-flex h-11 w-11 items-center justify-center rounded-md border border-border"
                aria-label={dark ? t("shell.themeLight") : t("shell.themeDark")}
                onClick={() => branding.setTheme(dark ? "light" : "dark")}
              >
                {dark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
              </button>
              <button
                type="button"
                className="inline-flex h-11 w-11 items-center justify-center rounded-md border border-border"
                aria-label={t("shell.logout")}
                onClick={() => void auth.logout().then(() => navigate("/login"))}
              >
                <LogOut className="h-4 w-4" />
              </button>
            </div>
          </div>
          {value.home.children.length > 1 ? (
            <div className="mx-auto max-w-3xl px-4 pb-3">
              <label className="text-[12px] text-muted" htmlFor="parent-child">
                {t("parent.child")}
              </label>
              <select
                id="parent-child"
                className="mt-1 h-11 w-full rounded-md border border-border bg-background px-3 text-sm"
                value={value.studentId}
                onChange={(event) => value.setStudentId(event.target.value)}
              >
                {value.home.children.map((child) => (
                  <option key={child.id} value={child.id}>
                    {child.fullName}
                    {child.batchNames ? ` · ${child.batchNames}` : ""}
                  </option>
                ))}
              </select>
            </div>
          ) : null}
          {offline ? (
            <p className="bg-secondary px-4 py-2 text-center text-[13px]" role="status">
              {t("parent.offline")}
            </p>
          ) : null}
        </header>
        <main id="parent-main" className="mx-auto max-w-3xl px-4 py-4">
          <Outlet />
        </main>
        <nav
          aria-label={t("parent.navLabel")}
          className="fixed inset-x-0 bottom-0 z-20 border-t border-border bg-card"
        >
          <ul className="mx-auto flex max-w-3xl">
            {links.map((link) => (
              <li key={link.to} className="min-w-0 flex-1">
                <NavLink
                  to={link.to}
                  end={link.end}
                  className={({ isActive }) =>
                      cn(
                      "flex h-16 flex-col items-center justify-center gap-0.5 px-0.5 text-center text-[11px] leading-tight",
                      isActive ? "font-medium text-primary" : "text-foreground",
                    )
                  }
                >
                  <link.icon className="h-4 w-4" aria-hidden="true" />
                  {link.label}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      </div>
    </ParentContext.Provider>
  );
}

export function ParentFrame({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section>
      <h1 className="text-xl font-semibold">{title}</h1>
      <div className="mt-4 grid gap-3">{children}</div>
    </section>
  );
}
