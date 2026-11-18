/* eslint-disable react-refresh/only-export-components */
import { createContext, useContext, type ReactNode } from "react";
import { useQuery } from "@tanstack/react-query";
import { t } from "@/config/strings";
import { api } from "@/lib/api";
import type { FeatureStatus } from "@/lib/types";
import { useAuth } from "@/features/auth/AuthProvider";

type FeatureContextValue = {
  features: FeatureStatus[];
  isLoading: boolean;
};

const FeatureContext = createContext<FeatureContextValue>({ features: [], isLoading: false });

export function FeatureProvider({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  const query = useQuery({
    queryKey: ["features", user?.tenantId],
    enabled: Boolean(user?.tenantId),
    queryFn: () => api<FeatureStatus[]>("/api/settings/features"),
  });

  return (
    <FeatureContext.Provider value={{ features: query.data ?? [], isLoading: query.isLoading }}>
      {children}
    </FeatureContext.Provider>
  );
}

export function StaticFeatureProvider({
  features,
  children,
}: {
  features: FeatureStatus[];
  children: ReactNode;
}) {
  return <FeatureContext.Provider value={{ features, isLoading: false }}>{children}</FeatureContext.Provider>;
}

export function useFeature(feature: string) {
  const { features, isLoading } = useContext(FeatureContext);
  const row = features.find((item) => item.featureKey === feature);
  return {
    isLoading,
    isEnabled: row?.isEffectiveEnabled ?? false,
    isInPlan: row?.isInPlan ?? false,
    isOverridden: row?.isOverridden ?? false,
  };
}

export function FeatureGate({
  feature,
  children,
  fallback,
}: {
  feature: string;
  children: ReactNode;
  fallback?: ReactNode;
}) {
  const state = useFeature(feature);
  if (state.isLoading) return null;
  if (!state.isInPlan) return fallback ?? <p className="text-sm text-muted">{t("gates.upgrade")}</p>;
  if (!state.isEnabled) return fallback ?? null;
  return children;
}
