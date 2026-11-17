import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useFeature } from "@/components/feature-gate";
import { PageHeader } from "@/components/page-header";
import { ApiError, api } from "@/lib/api";

export function ParentHomePage() {
  const { t } = useTranslation();
  const feature = useFeature("ParentPortal");
  const status = useQuery({
    queryKey: ["parent-portal"],
    enabled: feature.isEnabled,
    retry: false,
    queryFn: () => api<{ status: string }>("/api/parent/portal"),
  });
  const blocked =
    !feature.isLoading &&
    !feature.isEnabled ||
    (status.error instanceof ApiError &&
      (status.error.code === "FEATURE_DISABLED" || status.error.code === "FEATURE_NOT_IN_PLAN"));

  return (
    <div>
      <PageHeader title={t("parent.title")} description={t("parent.description")} />
      {feature.isLoading ? null : blocked ? (
        <section className="max-w-lg rounded-lg border border-border bg-card p-5">
          <h2 className="text-base font-semibold">{t("parent.offTitle")}</h2>
          <p className="mt-2 text-sm text-muted">{t("parent.offBody")}</p>
        </section>
      ) : (
        <section className="max-w-lg rounded-lg border border-border bg-card p-5">
          <p className="text-sm">{t("parent.ready")}</p>
        </section>
      )}
    </div>
  );
}
