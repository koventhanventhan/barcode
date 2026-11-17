import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Lock } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Dialog } from "@/components/ui/dialog";
import { Switch } from "@/components/ui/switch";
import { NOTIFICATION_FEATURES } from "@/domain/catalog";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import type { FeatureStatus } from "@/lib/types";

export function FeaturesPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { isReadOnly } = useAuth();
  const queryClient = useQueryClient();
  const [locked, setLocked] = useState<string | null>(null);
  const query = useQuery({
    queryKey: ["features"],
    queryFn: () => api<FeatureStatus[]>("/api/settings/features"),
  });
  const mutation = useMutation({
    mutationFn: (input: { featureKey: string; isEnabled: boolean }) =>
      api<FeatureStatus>(`/api/settings/features/${input.featureKey}`, {
        method: "PUT",
        body: JSON.stringify({ isEnabled: input.isEnabled }),
      }),
    onMutate: async (input) => {
      await queryClient.cancelQueries({ queryKey: ["features"] });
      const previous = queryClient.getQueryData<FeatureStatus[]>(["features"]);
      queryClient.setQueryData<FeatureStatus[]>(["features"], (current) =>
        (current ?? []).map((feature) =>
          feature.featureKey === input.featureKey
            ? {
                ...feature,
                isTenantEnabled: input.isEnabled,
                isEffectiveEnabled: feature.isInPlan && input.isEnabled,
              }
            : feature,
        ),
      );
      return { previous };
    },
    onError: (error, _input, context) => {
      if (context?.previous) queryClient.setQueryData(["features"], context.previous);
      toast.error(error instanceof Error ? error.message : t("errors.generic"));
    },
    onSuccess: (updated) => {
      toast.success(
        t("features.saved", {
          feature: t(`feature.${updated.featureKey}`, { defaultValue: updated.featureKey }),
          state: updated.isTenantEnabled ? t("features.on") : t("features.off"),
        }),
      );
      void queryClient.invalidateQueries({ queryKey: ["features"] });
      void queryClient.invalidateQueries({ queryKey: ["subscription"] });
    },
  });

  if (query.isLoading) return <PageSkeleton />;
  if (!query.data) {
    return (
      <p className="text-sm">
        {t("errors.load")}{" "}
        <button type="button" className="text-primary" onClick={() => void query.refetch()}>
          {t("common.retry")}
        </button>
      </p>
    );
  }

  const notifications = query.data.filter((feature) => NOTIFICATION_FEATURES.has(feature.featureKey));
  const modules = query.data.filter((feature) => !NOTIFICATION_FEATURES.has(feature.featureKey));

  return (
    <div>
      <PageHeader
        title={t("features.title")}
        description={t("features.description")}
        crumbs={[{ label: t("nav.settings"), href: "/app/settings/features" }, { label: t("nav.features") }]}
      />
      <FeatureGroup title={t("features.modules")} features={modules} disabled={isReadOnly} onToggle={(feature, isEnabled) => {
        if (!feature.isInPlan) {
          setLocked(feature.featureKey);
          return;
        }
        mutation.mutate({ featureKey: feature.featureKey, isEnabled });
      }} />
      <FeatureGroup title={t("features.notifications")} features={notifications} disabled={isReadOnly} onToggle={(feature, isEnabled) => {
        if (!feature.isInPlan) {
          setLocked(feature.featureKey);
          return;
        }
        mutation.mutate({ featureKey: feature.featureKey, isEnabled });
      }} />
      <Dialog
        open={Boolean(locked)}
        onOpenChange={(open) => !open && setLocked(null)}
        title={t("features.upgrade")}
        description={t("features.upgradeBody", {
          feature: t(`feature.${locked ?? ""}`, { defaultValue: locked ?? "" }),
        })}
      >
        <button type="button" className="text-sm text-primary" onClick={() => setLocked(null)}>
          {t("common.close")}
        </button>
      </Dialog>
    </div>
  );
}

function FeatureGroup({
  title,
  features,
  disabled,
  onToggle,
}: {
  title: string;
  features: FeatureStatus[];
  disabled: boolean;
  onToggle: (feature: FeatureStatus, isEnabled: boolean) => void;
}) {
  const { t } = useTranslation();
  return (
    <section className="mb-4 rounded-lg border border-border bg-card">
      <h2 className="border-b border-border px-4 py-3 text-sm font-semibold">{title}</h2>
      <ul>
        {features.map((feature) => {
          const label = t(`feature.${feature.featureKey}`, { defaultValue: feature.featureKey });
          return (
            <li key={feature.featureKey} className="flex items-center justify-between gap-3 border-b border-border px-4 py-3 last:border-0">
              <div>
                <p className="text-sm font-medium">{label}</p>
                <p className="text-xs text-muted">
                  {!feature.isInPlan
                    ? t("features.locked")
                    : feature.isOverridden
                      ? t("features.override")
                      : feature.isEffectiveEnabled
                        ? t("features.on")
                        : t("features.off")}
                </p>
              </div>
              {feature.isInPlan ? (
                <Switch
                  checked={feature.isTenantEnabled}
                  disabled={disabled}
                  aria-label={label}
                  onCheckedChange={(checked) => onToggle(feature, checked)}
                />
              ) : (
                <button type="button" className="inline-flex items-center gap-1 text-xs text-muted" onClick={() => onToggle(feature, true)}>
                  <Lock className="h-3.5 w-3.5" aria-hidden="true" />
                  {t("features.upgrade")}
                </button>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
