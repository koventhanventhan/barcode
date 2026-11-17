import { useQuery } from "@tanstack/react-query";
import {
  Building2,
  CreditCard,
  KeyRound,
  Languages,
  LayoutDashboard,
  LogOut,
  Menu,
  Moon,
  Palette,
  PanelLeft,
  Search,
  SlidersHorizontal,
  Sun,
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
  const platform = user?.role === "SuperAdmin" && !auth.isImpersonating;

  const items = useMemo<NavItem[]>(() => {
    if (!user) return [];
    return [
      { to: "/app", label: t("nav.dashboard"), icon: LayoutDashboard, show: institute || auth.isImpersonating },
      { to: "/app/settings/features", label: t("nav.features"), icon: SlidersHorizontal, show: (institute || auth.isImpersonating) && canSettings },
      { to: "/app/settings/branding", label: t("nav.branding"), icon: Palette, show: (institute || auth.isImpersonating) && canSettings },
      { to: "/account/password", label: t("nav.password"), icon: KeyRound, show: true },
      { to: "/admin/tenants", label: t("nav.tenants"), icon: Building2, show: platform },
      { to: "/admin/plans", label: t("nav.plans"), icon: CreditCard, show: platform },
      { to: "/admin/branding", label: t("nav.platformBranding"), icon: Palette, show: platform },
      { to: "/parent", label: t("nav.portal"), icon: Users, show: user.role === "Parent" },
    ].filter((item) => item.show);
  }, [auth.isImpersonating, canSettings, institute, platform, t, user]);

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
          {!collapsed ? <p className="px-2 pb-1 text-[11px] font-medium uppercase tracking-wide text-muted">{platform ? t("nav.platform") : t("nav.overview")}</p> : null}
          <ul className="grid gap-1">
            {items.map((item) => (
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
