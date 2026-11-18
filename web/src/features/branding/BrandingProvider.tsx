/* eslint-disable react-refresh/only-export-components */
import { useQuery } from "@tanstack/react-query";
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useLocation } from "react-router-dom";
import { ProductName } from "@/config/brand";
import { useAuth } from "@/features/auth/AuthProvider";
import { api, fetchPublicBranding } from "@/lib/api";
import { applyBrandToDocument, resolveThemeMode } from "@/lib/color";
import { getTenantSlug, getThemeOverride, setTenantSlug, setThemeOverride, subscribeTenantSlug } from "@/lib/session";
import { extractInstituteCodeFromHost } from "@/lib/subdomain";
import type { Branding, PublicBranding } from "@/lib/types";

type BrandingContextValue = {
  branding: Branding | undefined;
  publicBranding: PublicBranding;
  isLoading: boolean;
  institute: string;
  hostSubdomain: string;
  setInstitute: (slug: string) => void;
  previewInstitute: (slug: string) => void;
  themeOverride: "light" | "dark" | null;
  setTheme: (mode: "light" | "dark") => void;
  resolvedMode: "light" | "dark";
};

const BrandingContext = createContext<BrandingContextValue | null>(null);

const fallbackPublic: PublicBranding = {
  displayName: ProductName,
  tagline: null,
  logoUrl: null,
  faviconUrl: null,
  primaryColor: "#0F766E",
  accentColor: "#0F766E",
};

const fallback: Branding = {
  primaryColor: "#0F766E",
  accentColor: "#0F766E",
  logoUrl: null,
  faviconUrl: null,
  loginBackgroundUrl: null,
  instituteDisplayName: ProductName,
  tagline: null,
  fontPreference: "Inter",
  themeMode: "light",
  removeProductBranding: false,
  canRemoveProductBranding: false,
  subdomain: null,
  isPlatformDefault: true,
};

function isHexColor(value: unknown): value is string {
  return typeof value === "string" && /^#[0-9A-Fa-f]{6}$/.test(value.trim());
}

function normalizePublic(raw: PublicBranding | undefined): PublicBranding {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return fallbackPublic;
  return {
    displayName: typeof raw.displayName === "string" && raw.displayName.trim() ? raw.displayName : ProductName,
    tagline: typeof raw.tagline === "string" ? raw.tagline : null,
    logoUrl: typeof raw.logoUrl === "string" ? raw.logoUrl : null,
    faviconUrl: typeof raw.faviconUrl === "string" ? raw.faviconUrl : null,
    primaryColor: isHexColor(raw.primaryColor) ? raw.primaryColor : fallbackPublic.primaryColor,
    accentColor: isHexColor(raw.accentColor) ? raw.accentColor : fallbackPublic.accentColor,
  };
}

function toBranding(pub: PublicBranding, code: string): Branding {
  return {
    ...fallback,
    primaryColor: pub.primaryColor,
    accentColor: pub.accentColor,
    logoUrl: pub.logoUrl,
    faviconUrl: pub.faviconUrl,
    instituteDisplayName: pub.displayName,
    tagline: pub.tagline,
    subdomain: code || null,
    isPlatformDefault: !code,
  };
}

function isMarketingPath(pathname: string): boolean {
  return (
    pathname === "/" ||
    pathname === "/features" ||
    pathname.startsWith("/features/") ||
    pathname === "/pricing" ||
    pathname === "/demo" ||
    pathname === "/about" ||
    pathname === "/contact" ||
    pathname === "/signup"
  );
}

function isAuthPath(pathname: string): boolean {
  return (
    pathname === "/login" ||
    pathname === "/forgot-password" ||
    pathname === "/reset-password" ||
    pathname === "/accept-invite"
  );
}

export function BrandingProvider({ children }: { children: ReactNode }) {
  const location = useLocation();
  const auth = useAuth();
  const marketing = isMarketingPath(location.pathname);
  const onAuthPage = isAuthPath(location.pathname);
  const hostSubdomain = useMemo(() => extractInstituteCodeFromHost(window.location.hostname), []);
  const [institute, setInstituteState] = useState(() => hostSubdomain || getTenantSlug());
  const [previewCode, setPreviewCode] = useState(() => hostSubdomain || getTenantSlug());

  useEffect(() => {
    if (hostSubdomain && getTenantSlug() !== hostSubdomain) {
      setTenantSlug(hostSubdomain);
    }
  }, [hostSubdomain]);

  useEffect(() => subscribeTenantSlug(() => setInstituteState(hostSubdomain || getTenantSlug())), [hostSubdomain]);

  const [themeOverride, setThemeState] = useState<"light" | "dark" | null>(getThemeOverride);

  const publicQuery = useQuery({
    queryKey: ["public-branding", previewCode],
    enabled: !marketing && (!auth.user || onAuthPage),
    placeholderData: (previous) => previous,
    queryFn: () => fetchPublicBranding(previewCode),
  });

  const currentQuery = useQuery({
    queryKey: ["current-branding", auth.user?.id ?? null, auth.user?.tenantId ?? null, auth.isImpersonating],
    enabled: !marketing && !onAuthPage && Boolean(auth.user),
    queryFn: () => api<Branding>("/api/branding/current"),
  });

  const normalizedPublic = useMemo(() => normalizePublic(publicQuery.data), [publicQuery.data]);
  const activeFull = useMemo<Branding>(() => {
    if (marketing) return fallback;
    if (auth.user && !onAuthPage && currentQuery.data) return currentQuery.data;
    return toBranding(normalizedPublic, previewCode);
  }, [marketing, auth.user, onAuthPage, currentQuery.data, normalizedPublic, previewCode]);

  const activePublic = useMemo<PublicBranding>(() => {
    if (marketing) return fallbackPublic;
    if (auth.user && !onAuthPage && currentQuery.data) {
      return {
        displayName: currentQuery.data.instituteDisplayName || ProductName,
        tagline: currentQuery.data.tagline,
        logoUrl: currentQuery.data.logoUrl,
        faviconUrl: currentQuery.data.faviconUrl,
        primaryColor: isHexColor(currentQuery.data.primaryColor) ? currentQuery.data.primaryColor : fallbackPublic.primaryColor,
        accentColor: isHexColor(currentQuery.data.accentColor) ? currentQuery.data.accentColor : fallbackPublic.accentColor,
      };
    }
    return normalizedPublic;
  }, [marketing, auth.user, onAuthPage, currentQuery.data, normalizedPublic]);

  const prefersDark = Boolean(window.matchMedia?.("(prefers-color-scheme: dark)")?.matches);
  const resolvedMode = marketing ? "light" : (themeOverride ?? resolveThemeMode(activeFull.themeMode, prefersDark));

  useEffect(() => {
    applyBrandToDocument({
      primaryColor: activeFull.primaryColor,
      accentColor: activeFull.accentColor,
      fontPreference: activeFull.fontPreference,
      mode: resolvedMode,
    });
    if (!marketing) {
      document.title = activePublic.displayName || ProductName;
      const link = document.querySelector<HTMLLinkElement>("link[rel='icon']");
      if (link) {
        link.href = activePublic.faviconUrl || "/favicon.svg";
      }
    }
  }, [activeFull, activePublic, resolvedMode, marketing]);

  const value = useMemo<BrandingContextValue>(
    () => ({
      branding: marketing ? fallback : activeFull,
      publicBranding: activePublic,
      isLoading: publicQuery.isLoading || currentQuery.isLoading,
      institute,
      hostSubdomain,
      setInstitute: (slug: string) => {
        const normalized = slug.trim().toLowerCase();
        setTenantSlug(normalized);
        setInstituteState(normalized);
        setPreviewCode(normalized);
      },
      previewInstitute: (slug: string) => {
        setPreviewCode(slug.trim().toLowerCase());
      },
      themeOverride,
      setTheme: (mode) => {
        setThemeOverride(mode);
        setThemeState(mode);
      },
      resolvedMode,
    }),
    [
      marketing,
      activeFull,
      activePublic,
      publicQuery.isLoading,
      currentQuery.isLoading,
      institute,
      hostSubdomain,
      themeOverride,
      resolvedMode,
    ],
  );

  return <BrandingContext.Provider value={value}>{children}</BrandingContext.Provider>;
}

export function useBranding(): BrandingContextValue {
  const context = useContext(BrandingContext);
  if (!context) throw new Error("useBranding must be used within BrandingProvider.");
  return context;
}
