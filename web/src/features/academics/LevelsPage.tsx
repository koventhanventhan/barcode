import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { t } from "@/config/strings";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Switch } from "@/components/ui/switch";
import { useAuth } from "@/features/auth/AuthProvider";
import { AcademicNav, type EducationLevel } from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";
import type { SubscriptionSummary } from "@/lib/types";

export function LevelsPage() {
  const toast = useToast();
  const { isReadOnly, hasPermission } = useAuth();
  const queryClient = useQueryClient();
  const levels = useQuery({ queryKey: ["levels"], queryFn: () => api<EducationLevel[]>("/api/education-levels") });
  const subscription = useQuery({ queryKey: ["subscription"], queryFn: () => api<SubscriptionSummary>("/api/subscription") });
  const save = useMutation({
    mutationFn: (enabledLevelIds: string[]) =>
      api<EducationLevel[]>("/api/education-levels/selection", {
        method: "PUT",
        body: JSON.stringify({ enabledLevelIds }),
      }),
    onSuccess: (data) => {
      queryClient.setQueryData(["levels"], data);
      toast.success(t("academic.saved"));
      void queryClient.invalidateQueries({ queryKey: ["subscription"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  if (!hasPermission("academics.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (levels.isLoading) return <PageSkeleton />;
  if (!levels.data) {
    return (
      <p className="text-sm">
        {t("errors.load")}{" "}
        <button type="button" className="text-primary" onClick={() => void levels.refetch()}>
          {t("common.retry")}
        </button>
      </p>
    );
  }

  const limit = subscription.data?.limits.find((item) => item.limitKey === "EducationLevels");
  const enabled = levels.data.filter((level) => level.isEnabled).length;
  const usage = limit?.isUnlimited
    ? t("academic.levelsUnlimited", { enabled })
    : t("academic.levelsUsage", { enabled, max: limit?.maxAllowed ?? enabled });

  return (
    <div>
      <PageHeader
        title={t("academic.levelsTitle")}
        description={t("academic.levelsDescription")}
        crumbs={[{ label: t("nav.academic"), href: "/app/academics/levels" }, { label: t("nav.levels") }]}
      />
      <AcademicNav />
      <p className="mb-3 text-sm tabular-nums text-muted">{usage}</p>
      <ul className="divide-y divide-border rounded-lg border border-border bg-card">
        {levels.data.map((level) => (
          <li key={level.id} className="flex items-center justify-between gap-3 px-3 py-3">
            <div>
              <p className="text-sm font-medium">{level.name}</p>
              <p className="text-[13px] text-muted">
                {level.code} · {level.description}
              </p>
            </div>
            <Switch
              checked={level.isEnabled}
              disabled={isReadOnly || !hasPermission("academics.manage") || save.isPending}
              onCheckedChange={(checked) => {
                const ids = levels.data
                  .filter((item) => (item.id === level.id ? checked : item.isEnabled))
                  .map((item) => item.id);
                save.mutate(ids);
              }}
              aria-label={level.name}
            />
          </li>
        ))}
      </ul>
    </div>
  );
}
