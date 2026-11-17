/* eslint-disable react-refresh/only-export-components */
import { useQuery } from "@tanstack/react-query";
import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { ProductName } from "@/config/brand";
import { api } from "@/lib/api";
import { applyBrandToDocument, resolveThemeMode } from "@/lib/color";
import { getTenantSlug, getThemeOverride, setTenantSlug, setThemeOverride, subscribeTenantSlug } from "@/lib/session";
import type { Branding } from "@/lib/types";

type BrandingContextValue = {
  branding: Branding | undefined;
  isLoading: boolean;
  institute: string;
  setInstitute: (slug: string) => void;
  themeOverride: "light" | "dark" | null;
  setTheme: (mode: "light" | "dark") => void;
  resolvedMode: "light" | "dark";
};

const BrandingContext = createContext<BrandingContextValue | null>(null);

const fallback: Branding = {
  primaryColor: "#1F4E46",
  accentColor: "#C45C26",
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

export function BrandingProvider({ children }: { children: ReactNode }) {
  const [institute, setInstituteState] = useState(getTenantSlug);
  useEffect(() => subscribeTenantSlug(() => setInstituteState(getTenantSlug())), []);
  const [themeOverride, setThemeState] = useState<"light" | "dark" | null>(getThemeOverride);
  const query = useQuery({
    queryKey: ["public-branding", institute],
    queryFn: () => api<Branding>("/api/public/branding"),
  });
  const branding = query.data ?? fallback;
  const prefersDark = window.matchMedia("(prefers-color-scheme: dark)").matches;
  const resolvedMode = themeOverride ?? resolveThemeMode(branding.themeMode, prefersDark);

  useEffect(() => {
    applyBrandToDocument({
      primaryColor: branding.primaryColor,
      accentColor: branding.accentColor,
      fontPreference: branding.fontPreference,
      mode: resolvedMode,
    });
    document.title = branding.instituteDisplayName || ProductName;
    const link = document.querySelector<HTMLLinkElement>("link[rel='icon']");
    if (link && branding.faviconUrl) link.href = branding.faviconUrl;
  }, [branding, resolvedMode]);

  const value = useMemo<BrandingContextValue>(
    () => ({
      branding: query.data,
      isLoading: query.isLoading,
      institute,
      setInstitute: (slug: string) => {
        const normalized = slug.trim().toLowerCase();
        setTenantSlug(normalized);
        setInstituteState(normalized);
      },
      themeOverride,
      setTheme: (mode) => {
        setThemeOverride(mode);
        setThemeState(mode);
      },
      resolvedMode,
    }),
    [query.data, query.isLoading, institute, themeOverride, resolvedMode],
  );

  return <BrandingContext.Provider value={value}>{children}</BrandingContext.Provider>;
}

export function useBranding(): BrandingContextValue {
  const context = useContext(BrandingContext);
  if (!context) throw new Error("useBranding must be used within BrandingProvider.");
  return context;
}
