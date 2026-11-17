import { useQuery } from "@tanstack/react-query";
import {
  BookOpen,
  Building2,
  CalendarDays,
  CreditCard,
  ClipboardCheck,
  GraduationCap,
  IdCard,
  ScanLine,
  KeyRound,
  Languages,
  Layers,
  LayoutDashboard,
  LogOut,
  Menu,
  MessageSquare,
  Moon,
  Palette,
  PanelLeft,
  Search,
  SlidersHorizontal,
  Sun,
  UserRound,
  Users,
  X,
} from "lucide-react";
import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { ProductName } from "@/config/brand";
import { useAuth } from "@/features/auth/AuthProvider";
import { useBranding } from "@/features/branding/BrandingProvider";
import { api } from "@/lib/api";
import type { SubscriptionSummary } from "@/lib/types";
import { cn } from "@/lib/utils";

type NavItem = {
  to: string;
  label: string;
  icon: typeof LayoutDashboard;
  show: boolean;
  group: string;
};

export function AppShell() {
  const { t, i18n } = useTranslation();
  const auth = useAuth();
  const branding = useBranding();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [collapsed, setCollapsed] = useState(false);
  const [query, setQuery] = useState("");
  const user = auth.user;
  const institute = user && user.role !== "SuperAdmin" && user.role !== "Parent";
  const subscription = useQuery({
    queryKey: ["subscription", user?.tenantId],
    enabled: Boolean(institute),
    queryFn: () => api<SubscriptionSummary>("/api/subscription"),
  });
  const canSettings = auth.hasPermission("settings.manage");
  const canStudents = auth.hasPermission("students.view");
  const canAcademics = auth.hasPermission("academics.view");
  const canStaff = auth.hasPermission("users.view");
  const canAttendance = auth.hasPermission("attendance.mark");
  const canFees = auth.hasPermission("fees.view") || auth.hasPermission("fees.collect");
  const canExams = auth.hasPermission("exams.manage");
  const platform = user?.role === "SuperAdmin" && !auth.isImpersonating;
  const inInstitute = institute || auth.isImpersonating;

  const items = useMemo((): NavItem[] => {
    if (!user) return [];
    return [
      { to: "/app", label: t("nav.dashboard"), icon: LayoutDashboard, show: inInstitute, group: "overview" },
      { to: "/app/students", label: t("nav.students"), icon: IdCard, show: inInstitute && canStudents, group: "academic" },
      { to: "/app/academics/levels", label: t("nav.levels"), icon: GraduationCap, show: inInstitute && canAcademics, group: "academic" },
      { to: "/app/academics/batches", label: t("nav.batches"), icon: Layers, show: inInstitute && canAcademics, group: "academic" },
      { to: "/app/academics/subjects", label: t("nav.subjects"), icon: BookOpen, show: inInstitute && canAcademics, group: "academic" },
      { to: "/app/academics/classes", label: t("nav.classes"), icon: CalendarDays, show: inInstitute && canAcademics, group: "academic" },
      { to: "/app/staff", label: t("nav.staff"), icon: Users, show: inInstitute && canStaff, group: "academic" },
      { to: "/app/parents", label: t("nav.parents"), icon: UserRound, show: inInstitute && canStudents, group: "academic" },
      { to: "/app/attendance", label: t("nav.attendance"), icon: ScanLine, show: inInstitute && canAttendance, group: "academic" },
      { to: "/app/fees", label: t("nav.fees"), icon: CreditCard, show: inInstitute && canFees, group: "academic" },
      { to: "/app/exams", label: t("nav.exams"), icon: ClipboardCheck, show: inInstitute && canExams, group: "academic" },
      { to: "/app/settings/features", label: t("nav.features"), icon: SlidersHorizontal, show: inInstitute && canSettings, group: "settings" },
      { to: "/app/settings/messages", label: t("nav.messageLog"), icon: MessageSquare, show: inInstitute && canSettings, group: "settings" },
      { to: "/app/settings/branding", label: t("nav.branding"), icon: Palette, show: inInstitute && canSettings, group: "settings" },
      { to: "/account/password", label: t("nav.password"), icon: KeyRound, show: true, group: "settings" },
      { to: "/admin/tenants", label: t("nav.tenants"), icon: Building2, show: platform, group: "platform" },
      { to: "/admin/plans", label: t("nav.plans"), icon: CreditCard, show: platform, group: "platform" },
      { to: "/admin/branding", label: t("nav.platformBranding"), icon: Palette, show: platform, group: "platform" },
    ].filter((item) => item.show);
  }, [canAcademics, canAttendance, canExams, canFees, canSettings, canStaff, canStudents, inInstitute, platform, t, user]);

  const matches = items.filter((item) => item.label.toLowerCase().includes(query.trim().toLowerCase()));
  const name = branding.branding?.instituteDisplayName || ProductName;
  const hideProduct = branding.branding?.removeProductBranding;
  const readOnly = auth.isReadOnly || Boolean(subscription.data?.isReadOnly);

  return (
    <div className="min-h-screen md:grid md:grid-cols-[auto_1fr]">
      <a href="#content" className="sr-only focus:not-sr-only focus:absolute focus:left-3 focus:top-3 focus:z-50 focus:bg-card focus:px-3 focus:py-2">
        {t("shell.skip")}
      </a>
      {open ? (
        <button type="button" className="fixed inset-0 z-30 bg-foreground/30 md:hidden" aria-label={t("shell.closeMenu")} onClick={() => setOpen(false)} />
      ) : null}
      <aside
        className={cn(
          "fixed inset-y-0 left-0 z-40 flex w-60 flex-col border-r border-border bg-card md:static",
          collapsed && "md:w-16",
          open ? "flex" : "hidden md:flex",
        )}
      >
        <div className="flex h-14 items-center gap-2 border-b border-border px-3">
          {branding.branding?.logoUrl ? <img src={branding.branding.logoUrl} alt="" className="h-7 w-7 object-contain" /> : <span className="h-7 w-7 rounded-md bg-primary" />}
          {collapsed ? null : (
            <span className="min-w-0">
              <span className="block truncate text-sm font-semibold">{name}</span>
              {hideProduct || name === ProductName ? null : (
                <span className="block truncate text-[11px] text-muted">{ProductName}</span>
              )}
            </span>
          )}
        </div>
        <nav className="flex-1 overflow-y-auto px-2 py-3" aria-label="Primary">
          {(["overview", "academic", "settings", "platform"] as const).map((group) => {
            const groupItems = items.filter((item) => item.group === group);
            if (groupItems.length === 0) return null;
            return (
              <div key={group} className="mb-3">
                {!collapsed ? <p className="px-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted">{t(`nav.${group}`)}</p> : null}
                <ul className="grid gap-1">
                  {groupItems.map((item) => (
                    <li key={item.to}>
                      <NavLink
                        to={item.to}
                        end={item.to === "/app"}
                        onClick={() => setOpen(false)}
                        title={item.label}
                        className={({ isActive }) =>
                          cn(
                            "flex items-center gap-2 rounded-md px-2 py-2 text-sm",
                            isActive ? "bg-[color-mix(in_srgb,var(--primary)_14%,transparent)] font-medium" : "hover:bg-secondary",
                          )
                        }
                      >
                        <item.icon className="h-4 w-4 shrink-0" aria-hidden="true" />
                        {collapsed ? null : <span className="truncate">{item.label}</span>}
                      </NavLink>
                    </li>
                  ))}
                </ul>
              </div>
            );
          })}
        </nav>
      </aside>
      <div className="min-w-0">
        <header className="sticky top-0 z-20 flex h-14 items-center gap-2 border-b border-border bg-card px-3">
          <button type="button" className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-border md:hidden" onClick={() => setOpen(true)} aria-label={t("shell.openMenu")}>
            <Menu className="h-4 w-4" />
          </button>
          <button type="button" className="hidden h-8 w-8 items-center justify-center rounded-md border border-border md:inline-flex" onClick={() => setCollapsed((value) => !value)} aria-label={collapsed ? t("shell.expand") : t("shell.collapse")}>
            <PanelLeft className="h-4 w-4" />
          </button>
          <div className="relative min-w-0 flex-1">
            <Search className="pointer-events-none absolute left-2 top-2 h-4 w-4 text-muted" aria-hidden="true" />
            <input
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder={t("shell.search")}
              aria-label={t("shell.search")}
              className="h-8 w-full rounded-md border border-border bg-background pl-8 pr-3 text-sm"
            />
            {query.trim() ? (
              <div className="absolute left-0 right-0 top-10 z-30 rounded-md border border-border bg-card p-1">
                {matches.length === 0 ? (
                  <p className="px-2 py-2 text-[13px] text-muted">{t("shell.searchEmpty")}</p>
                ) : (
                  matches.map((item) => (
                    <button
                      key={item.to}
                      type="button"
                      className="block w-full rounded-md px-2 py-1.5 text-left text-sm hover:bg-secondary"
                      onClick={() => {
                        navigate(item.to);
                        setQuery("");
                      }}
                    >
                      {item.label}
                    </button>
                  ))
                )}
              </div>
            ) : null}
          </div>
          <label className="hidden items-center gap-1 text-xs text-muted sm:flex">
            <Languages className="h-4 w-4" aria-hidden="true" />
            <span className="sr-only">{t("shell.language")}</span>
            <select
              aria-label={t("shell.language")}
              className="h-8 rounded-md border border-border bg-card px-2"
              value={i18n.language}
              onChange={(event) => void i18n.changeLanguage(event.target.value)}
            >
              <option value="en">{t("lang.en")}</option>
              <option value="ta">{t("lang.ta")}</option>
              <option value="si">{t("lang.si")}</option>
            </select>
          </label>
          <button
            type="button"
            className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-border"
            aria-label={branding.resolvedMode === "dark" ? t("shell.themeLight") : t("shell.themeDark")}
            onClick={() => branding.setTheme(branding.resolvedMode === "dark" ? "light" : "dark")}
          >
            {branding.resolvedMode === "dark" ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
          </button>
          <button
            type="button"
            className="inline-flex h-8 items-center gap-1 rounded-md border border-border px-2 text-[13px]"
            onClick={() => void auth.logout().then(() => navigate("/login"))}
          >
            <LogOut className="h-4 w-4" aria-hidden="true" />
            <span className="hidden sm:inline">{t("shell.logout")}</span>
          </button>
        </header>
        {auth.isImpersonating ? (
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border bg-secondary px-4 py-2 text-[13px]">
            <span>{t("shell.impersonating", { name })}</span>
            <button type="button" className="font-medium text-primary" onClick={() => { auth.exitImpersonation(); navigate("/admin/tenants"); }}>
              {t("shell.exitImpersonation")}
            </button>
          </div>
        ) : readOnly ? (
          <div className="border-b border-border bg-secondary px-4 py-2 text-[13px]">{t("shell.readOnly")}</div>
        ) : null}
        <main id="content" className="px-3 py-5 sm:px-6">
          <Outlet />
        </main>
        {open ? (
          <button type="button" className="sr-only" onClick={() => setOpen(false)}>
            <X />
          </button>
        ) : null}
      </div>
    </div>
  );
}
