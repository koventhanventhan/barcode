import { useQuery } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import type { Branch, SmsQuotaSummary, SubscriptionSummary } from "@/lib/types";
import { useAuth } from "@/features/auth/AuthProvider";
import { type ExamSummary } from "@/features/exams/api";
import { SmsQuotaCard } from "@/features/features/SmsQuotaCard";
import { OnboardingChecklist } from "@/features/marketing/OnboardingChecklist";

export function DashboardPage() {
  const { t } = useTranslation();
  const { hasPermission } = useAuth();
  const subscription = useQuery({
    queryKey: ["subscription"],
    queryFn: () => api<SubscriptionSummary>("/api/subscription"),
  });
  const branches = useQuery({
    queryKey: ["branches"],
    enabled: hasPermission("branches.view"),
    queryFn: () => api<Branch[]>("/api/branches"),
  });
  const smsQuota = useQuery({
    queryKey: ["sms-quota"],
    queryFn: () => api<SmsQuotaSummary>("/api/notifications/quota"),
  });
  const exams = useQuery({
    queryKey: ["exam-summary"],
    enabled: hasPermission("reports.view") || hasPermission("exams.manage"),
    queryFn: () => api<ExamSummary>("/api/exams/summary"),
  });

  if (subscription.isLoading) return <PageSkeleton />;
  if (subscription.isError || !subscription.data) {
    return (
      <div className="grid gap-3">
        <p className="text-sm">{t("dashboard.noSubscription")}</p>
        <Button variant="outline" onClick={() => void subscription.refetch()}>
          {t("common.retry")}
        </Button>
      </div>
    );
  }

  const data = subscription.data;
  const enabled = data.features.filter((feature) => feature.isEffectiveEnabled).length;
  const inPlan = data.features.filter((feature) => feature.isInPlan).length;
  const limitRows = data.limits.map((limit) => ({
    name: t(`limit.${limit.limitKey}`, { defaultValue: limit.limitKey }),
    used: limit.currentCount,
    max: limit.maxAllowed ?? limit.currentCount,
  }));

  return (
    <div>
      <PageHeader title={t("dashboard.title")} description={t("dashboard.description")} crumbs={[{ label: t("nav.overview") }, { label: t("nav.dashboard") }]} />
      {hasPermission("settings.manage") || hasPermission("students.edit") ? <OnboardingChecklist /> : null}
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label={t("dashboard.plan")} value={data.planName} hint={data.planCode} />
        <StatCard label={t("dashboard.status")} value={data.status} />
        <StatCard label={t("dashboard.featuresOn")} value={`${enabled}`} hint={`${inPlan} ${t("dashboard.inPlan").toLowerCase()}`} />
        <StatCard label={t("dashboard.branches")} value={branches.data ? String(branches.data.length) : "—"} />
        {exams.data ? <StatCard label={t("dashboard.upcomingExams")} value={String(exams.data.upcomingCount)} hint={exams.data.upcoming[0]?.name} /> : null}
      </div>
      {smsQuota.data ? (
        <div className="mt-4">
          <SmsQuotaCard quota={smsQuota.data} showLogLink />
        </div>
      ) : null}
      <div className="mt-4 grid gap-3 xl:grid-cols-2">
        <section className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("dashboard.limitsTitle")}</h2>
          <p className="mb-3 text-xs text-muted">{t("dashboard.limitsDescription")}</p>
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={limitRows} margin={{ left: 8, right: 8 }}>
                <CartesianGrid stroke="var(--border)" vertical={false} />
                <XAxis dataKey="name" tick={{ fontSize: 11 }} interval={0} angle={-25} height={60} textAnchor="end" />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip />
                <Bar dataKey="used" fill="var(--primary)" name={t("dashboard.used")} />
                <Bar dataKey="max" fill="var(--accent)" name={t("dashboard.max")} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </section>
        <section className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("dashboard.featureSplitTitle")}</h2>
          <p className="mb-3 text-xs text-muted">{t("dashboard.featureSplitDescription")}</p>
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={[
                  { name: t("dashboard.inPlan"), count: inPlan },
                  { name: t("dashboard.notInPlan"), count: data.features.length - inPlan },
                ]}
              >
                <CartesianGrid stroke="var(--border)" vertical={false} />
                <XAxis dataKey="name" tick={{ fontSize: 12 }} />
                <YAxis allowDecimals={false} tick={{ fontSize: 11 }} />
                <Tooltip />
                <Bar dataKey="count" fill="var(--primary)" />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </section>
      </div>
    </div>
  );
}
