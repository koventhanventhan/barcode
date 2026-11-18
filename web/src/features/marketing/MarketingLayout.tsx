import { ChevronDown, Menu, X } from "lucide-react";
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from "react";
import { t } from "@/config/strings";
import { Link, NavLink, useLocation } from "react-router-dom";
import { ProductName } from "@/config/brand";
import { cn } from "@/lib/utils";
import { FEATURE_PAGES } from "./catalog";

export function MarketingLayout({ children }: { children: ReactNode }) {
  const location = useLocation();
  const [megaOpen, setMegaOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const megaId = useId();
  const megaRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    window.scrollTo(0, 0);
    setMegaOpen(false);
    setDrawerOpen(false);
  }, [location.pathname]);

  useEffect(() => {
    function handleOutside(event: MouseEvent) {
      if (megaRef.current && !megaRef.current.contains(event.target as Node)) {
        setMegaOpen(false);
      }
    }
    function handleEsc(event: globalThis.KeyboardEvent) {
      if (event.key === "Escape") {
        setMegaOpen(false);
        setDrawerOpen(false);
      }
    }
    document.addEventListener("mousedown", handleOutside);
    document.addEventListener("keydown", handleEsc);
    return () => {
      document.removeEventListener("mousedown", handleOutside);
      document.removeEventListener("keydown", handleEsc);
    };
  }, []);

  function onTriggerKeyDown(event: KeyboardEvent<HTMLButtonElement>) {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setMegaOpen(true);
    } else if (event.key === "Escape" && megaOpen) {
      event.preventDefault();
      setMegaOpen(false);
      triggerRef.current?.focus();
    }
  }

  const featureActive = location.pathname === "/features" || location.pathname.startsWith("/features/");

  return (
    <div className="flex min-h-screen flex-col bg-background text-foreground">
      <a
        href="#marketing-main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-3 focus:z-50 focus:rounded-md focus:bg-card focus:px-3 focus:py-2 focus:text-sm focus:font-medium"
      >
        {t("shell.skip")}
      </a>

      <header className="sticky top-0 z-40 border-b border-border bg-card">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between gap-4 px-4 sm:px-6">
          <Link to="/" className="flex items-center gap-2.5 font-heading text-base font-bold tracking-tight text-foreground">
            <span className="inline-flex h-7 w-7 items-center justify-center rounded-md bg-primary text-xs font-extrabold text-primary-foreground">
              E
            </span>
            <span>{ProductName}</span>
          </Link>

          <nav aria-label={t("marketing.nav")} className="hidden items-center gap-1 md:flex">
            <div ref={megaRef} className="relative">
              <button
                ref={triggerRef}
                type="button"
                aria-expanded={megaOpen}
                aria-controls={megaId}
                aria-haspopup="true"
                onClick={() => setMegaOpen((prev) => !prev)}
                onKeyDown={onTriggerKeyDown}
                className={cn(
                  "inline-flex items-center gap-1 rounded-md px-3 py-2 text-sm font-medium transition-colors",
                  featureActive ? "text-primary" : "text-foreground hover:text-primary",
                )}
              >
                {t("marketing.navFeatures")}
                <ChevronDown className={cn("h-4 w-4 stroke-[1.5] transition-transform", megaOpen && "rotate-180")} aria-hidden="true" />
              </button>

              {megaOpen ? (
                <div
                  id={megaId}
                  role="region"
                  aria-label={t("marketing.navFeatures")}
                  className="absolute left-0 top-full mt-2 w-[36rem] rounded-lg border border-border bg-card p-4 shadow-sm"
                >
                  <div className="mb-3 flex items-center justify-between border-b border-border pb-2">
                    <span className="text-xs font-medium uppercase tracking-wider text-muted">
                      {t("marketing.modulesEyebrow")}
                    </span>
                    <Link
                      to="/features"
                      className="text-xs font-medium text-primary underline-offset-4 hover:underline"
                    >
                      {t("marketing.allFeatures")} &rarr;
                    </Link>
                  </div>
                  <ul className="grid grid-cols-2 gap-2">
                    {FEATURE_PAGES.map((item) => (
                      <li key={item.slug}>
                        <Link
                          to={`/features/${item.slug}`}
                          className="block rounded-md border border-transparent p-2.5 transition-colors hover:border-border hover:bg-background"
                        >
                          <p className="font-heading text-sm font-semibold text-foreground">
                            <span className="mr-1.5 text-xs text-primary tabular">{item.number}</span>
                            {t(`marketing.pages.${item.slug}.navTitle`)}
                          </p>
                          <p className="mt-0.5 text-xs leading-5 text-muted">
                            {t(`marketing.pages.${item.slug}.navSummary`)}
                          </p>
                        </Link>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </div>

            <HeaderLink to="/pricing">{t("marketing.navPricing")}</HeaderLink>
            <HeaderLink to="/demo">{t("marketing.navDemo")}</HeaderLink>
            <HeaderLink to="/about">{t("marketing.navAbout")}</HeaderLink>
          </nav>

          <div className="hidden items-center gap-2.5 md:flex">
            <Link
              to="/login"
              className="rounded-md px-3 py-2 text-sm font-medium text-foreground underline-offset-4 hover:text-primary hover:underline"
            >
              {t("marketing.signIn")}
            </Link>
            <Link
              to="/signup"
              className="inline-flex h-9 items-center rounded-md bg-primary px-3.5 text-sm font-medium text-primary-foreground transition-opacity hover:opacity-95"
            >
              {t("marketing.startFreeTrial")}
            </Link>
          </div>

          <button
            type="button"
            className="inline-flex h-10 w-10 items-center justify-center rounded-md border border-border bg-background text-foreground md:hidden"
            aria-label={t("shell.openMenu")}
            aria-expanded={drawerOpen}
            onClick={() => setDrawerOpen(true)}
          >
            <Menu className="h-5 w-5 stroke-[1.5]" aria-hidden="true" />
          </button>
        </div>
      </header>

      {drawerOpen ? (
        <div className="fixed inset-0 z-50 md:hidden" role="dialog" aria-modal="true" aria-label={t("marketing.nav")}>
          <button
            type="button"
            aria-label={t("shell.closeMenu")}
            className="fixed inset-0 bg-foreground/30"
            onClick={() => setDrawerOpen(false)}
          />
          <nav className="fixed inset-y-0 right-0 flex w-80 max-w-[85vw] flex-col border-l border-border bg-card p-5 overflow-y-auto">
            <div className="flex items-center justify-between border-b border-border pb-3">
              <span className="font-heading text-base font-bold">{ProductName}</span>
              <button
                type="button"
                aria-label={t("shell.closeMenu")}
                className="inline-flex h-9 w-9 items-center justify-center rounded-md border border-border"
                onClick={() => setDrawerOpen(false)}
              >
                <X className="h-4 w-4 stroke-[1.5]" aria-hidden="true" />
              </button>
            </div>

            <div className="mt-4 grid gap-1 text-sm">
              <NavLink to="/" end className={drawerLinkClass}>
                {t("marketing.navHome")}
              </NavLink>
              <NavLink to="/features" className={drawerLinkClass}>
                {t("marketing.navFeatures")}
              </NavLink>
              <ul className="ml-3 grid gap-1 border-l border-border pl-3 py-1">
                {FEATURE_PAGES.map((item) => (
                  <li key={item.slug}>
                    <NavLink to={`/features/${item.slug}`} className={drawerSubLinkClass}>
                      {item.number} · {t(`marketing.pages.${item.slug}.navTitle`)}
                    </NavLink>
                  </li>
                ))}
              </ul>
              <NavLink to="/pricing" className={drawerLinkClass}>
                {t("marketing.navPricing")}
              </NavLink>
              <NavLink to="/demo" className={drawerLinkClass}>
                {t("marketing.navDemo")}
              </NavLink>
              <NavLink to="/about" className={drawerLinkClass}>
                {t("marketing.navAbout")}
              </NavLink>
              <NavLink to="/contact" className={drawerLinkClass}>
                {t("marketing.contactTitle")}
              </NavLink>
            </div>

            <div className="mt-6 grid gap-3 border-t border-border pt-4">
              <Link
                to="/login"
                className="inline-flex h-10 items-center justify-center rounded-md border border-border text-sm font-medium"
              >
                {t("marketing.signIn")}
              </Link>
              <Link
                to="/signup"
                className="inline-flex h-10 items-center justify-center rounded-md bg-primary text-sm font-medium text-primary-foreground"
              >
                {t("marketing.startFreeTrial")}
              </Link>
            </div>
          </nav>
        </div>
      ) : null}

      <main id="marketing-main" key={location.pathname} className="editorial-page-enter flex-1">
        {children}
      </main>

      <footer className="border-t border-border bg-card">
        <div className="mx-auto grid max-w-6xl gap-8 px-4 py-12 sm:grid-cols-2 sm:px-6 lg:grid-cols-4">
          <div>
            <p className="font-heading text-base font-bold">{ProductName}</p>
            <p className="mt-2 max-w-xs text-sm leading-6 text-muted">{t("marketing.footerTagline")}</p>
          </div>
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-muted">{t("marketing.navFeatures")}</p>
            <ul className="mt-3 grid gap-2 text-sm">
              <li>
                <Link to="/features" className="text-foreground underline-offset-4 hover:text-primary hover:underline">
                  {t("marketing.allFeatures")}
                </Link>
              </li>
              {FEATURE_PAGES.map((item) => (
                <li key={item.slug}>
                  <Link
                    to={`/features/${item.slug}`}
                    className="text-muted underline-offset-4 hover:text-primary hover:underline"
                  >
                    {t(`marketing.pages.${item.slug}.navTitle`)}
                  </Link>
                </li>
              ))}
            </ul>
          </div>
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-muted">{t("marketing.footerCompany")}</p>
            <ul className="mt-3 grid gap-2 text-sm">
              <li><Link to="/pricing" className="text-muted underline-offset-4 hover:text-primary hover:underline">{t("marketing.navPricing")}</Link></li>
              <li><Link to="/demo" className="text-muted underline-offset-4 hover:text-primary hover:underline">{t("marketing.navDemo")}</Link></li>
              <li><Link to="/about" className="text-muted underline-offset-4 hover:text-primary hover:underline">{t("marketing.navAbout")}</Link></li>
              <li><Link to="/contact" className="text-muted underline-offset-4 hover:text-primary hover:underline">{t("marketing.contactTitle")}</Link></li>
              <li><Link to="/login" className="text-muted underline-offset-4 hover:text-primary hover:underline">{t("marketing.signIn")}</Link></li>
              <li><Link to="/signup" className="text-muted underline-offset-4 hover:text-primary hover:underline">{t("marketing.startFreeTrial")}</Link></li>
            </ul>
          </div>
          <div>
            <p className="text-xs font-semibold uppercase tracking-wider text-muted">{t("marketing.contactTitle")}</p>
            <p className="mt-3 text-sm text-foreground">Colombo · Jaffna</p>
            <p className="mt-1 text-sm">
              <a href="mailto:superadmin@eduflow.lk" className="text-primary underline-offset-4 hover:underline">
                superadmin@eduflow.lk
              </a>
            </p>
          </div>
        </div>
        <div className="border-t border-border">
          <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-3 px-4 py-4 text-xs text-muted sm:px-6">
            <span>© 2026 {ProductName}. {t("marketing.footerRights")}</span>
            <span>{t("marketing.footerSmallPrint")}</span>
          </div>
        </div>
      </footer>
    </div>
  );
}

function HeaderLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        cn(
          "rounded-md px-3 py-2 text-sm font-medium underline-offset-4 transition-colors hover:text-primary hover:underline",
          isActive ? "text-primary underline" : "text-foreground",
        )
      }
    >
      {children}
    </NavLink>
  );
}

function drawerLinkClass({ isActive }: { isActive: boolean }) {
  return cn(
    "rounded-md px-2.5 py-2 font-medium",
    isActive ? "bg-brand-50 text-primary" : "text-foreground hover:bg-background",
  );
}

function drawerSubLinkClass({ isActive }: { isActive: boolean }) {
  return cn(
    "block rounded-md px-2 py-1.5 text-[13px]",
    isActive ? "font-semibold text-primary" : "text-muted hover:text-foreground",
  );
}
