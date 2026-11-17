import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useParams } from "react-router-dom";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FEATURE_KEYS } from "@/domain/catalog";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import type { Plan, TenantDetail } from "@/lib/types";

export function TenantDetailPage() {
  const { id = "" } = useParams();
  const { t } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const auth = useAuth();
  const queryClient = useQueryClient();
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [visitOpen, setVisitOpen] = useState(false);
  const [planCode, setPlanCode] = useState("");
  const [months, setMonths] = useState(1);
  const [days, setDays] = useState(30);
  const [reason, setReason] = useState("");
  const tenant = useQuery({
    queryKey: ["tenant", id],
    queryFn: () => api<TenantDetail>(`/api/super-admin/tenants/${id}`),
  });
  const plans = useQuery({ queryKey: ["plans"], queryFn: () => api<Plan[]>("/api/super-admin/plans") });

  function refresh() {
    return Promise.all([
      queryClient.invalidateQueries({ queryKey: ["tenant", id] }),
      queryClient.invalidateQueries({ queryKey: ["tenants"] }),
    ]);
  }

  const action = useMutation({
    mutationFn: async (input: { path: string; body: unknown }) =>
      api(`${input.path}`, { method: "POST", body: JSON.stringify(input.body) }),
    onSuccess: async () => {
      toast.success(t("tenants.updated"));
      await refresh();
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  const remove = useMutation({
    mutationFn: () => api(`/api/super-admin/tenants/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      toast.success(t("tenants.deleted"));
      await queryClient.invalidateQueries({ queryKey: ["tenants"] });
      navigate("/admin/tenants");
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  const override = useMutation({
    mutationFn: (input: { feature: string; enabled: boolean }) =>
      api(`/api/super-admin/tenants/${id}/feature-overrides/${input.feature}`, {
        method: "PUT",
        body: JSON.stringify({ isEnabled: input.enabled, reason: "Set from the platform console" }),
      }),
    onSuccess: async () => {
      toast.success(t("tenants.updated"));
      await refresh();
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  if (tenant.isLoading) return <PageSkeleton />;
  if (!tenant.data) return <p className="text-sm">{t("errors.load")}</p>;
  const detail = tenant.data;
  const selectedPlan = planCode || detail.planCode || "Starter";

  return (
    <div>
      <PageHeader
        title={detail.name}
        description={t("tenants.detailDescription")}
        crumbs={[{ label: t("nav.tenants"), href: "/admin/tenants" }, { label: detail.name }]}
        actions={
          <>
            <Button variant="outline" onClick={() => setVisitOpen(true)}>
              {t("tenants.impersonate")}
            </Button>
            <Button variant="danger" onClick={() => setDeleteOpen(true)}>
              {t("common.delete")}
            </Button>
          </>
        }
      />
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label={t("tenants.subdomain")} value={detail.subdomain} />
        <StatCard label={t("tenants.status")} value={detail.subscription?.status ?? t("common.none")} />
        <StatCard label={t("tenants.users")} value={String(detail.usersCount)} />
        <StatCard label={t("tenants.branches")} value={String(detail.branchesCount)} />
      </div>
      <section className="mt-4 grid gap-3 rounded-lg border border-border bg-card p-4 md:grid-cols-2">
        <form
          className="grid gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            action.mutate({ path: `/api/super-admin/tenants/${id}/subscription/assign-plan`, body: { planCode: selectedPlan } });
          }}
        >
          <label className="text-[13px] font-medium" htmlFor="assignPlan">{t("tenants.assign")}</label>
          <select id="assignPlan" className="h-9 rounded-md border border-border bg-card px-2" value={selectedPlan} onChange={(event) => setPlanCode(event.target.value)}>
            {(plans.data ?? []).map((plan) => (
              <option key={plan.id} value={plan.code}>{plan.name}</option>
            ))}
          </select>
          <Button type="submit" size="sm">{t("tenants.assign")}</Button>
        </form>
        <form
          className="grid gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            action.mutate({ path: `/api/super-admin/tenants/${id}/subscription/activate`, body: { durationMonths: months } });
          }}
        >
          <label className="text-[13px] font-medium" htmlFor="months">{t("tenants.months")}</label>
          <Input id="months" type="number" min={1} value={months} onChange={(event) => setMonths(Number(event.target.value))} />
          <Button type="submit" size="sm">{t("tenants.activate")}</Button>
        </form>
        <form
          className="grid gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            action.mutate({ path: `/api/super-admin/tenants/${id}/subscription/extend`, body: { daysToAdd: days } });
          }}
        >
          <label className="text-[13px] font-medium" htmlFor="days">{t("tenants.days")}</label>
          <Input id="days" type="number" min={1} value={days} onChange={(event) => setDays(Number(event.target.value))} />
          <Button type="submit" size="sm" variant="outline">{t("tenants.extend")}</Button>
        </form>
        <form
          className="grid gap-2"
          onSubmit={(event) => {
            event.preventDefault();
            action.mutate({ path: `/api/super-admin/tenants/${id}/subscription/suspend`, body: { reason } });
          }}
        >
          <label className="text-[13px] font-medium" htmlFor="reason">{t("tenants.reason")}</label>
          <Input id="reason" value={reason} onChange={(event) => setReason(event.target.value)} required />
          <Button type="submit" size="sm" variant="danger">{t("tenants.suspend")}</Button>
        </form>
      </section>
      <section className="mt-4 rounded-lg border border-border bg-card">
        <h2 className="border-b border-border px-4 py-3 text-sm font-semibold">{t("tenants.overrides")}</h2>
        <ul>
          {FEATURE_KEYS.map((feature) => {
            const current = detail.featureOverrides.find((item) => item.featureKey === feature);
            return (
              <li key={feature} className="flex items-center justify-between gap-3 border-b border-border px-4 py-2 text-sm last:border-0">
                <span>{t(`feature.${feature}`)}</span>
                <span className="flex items-center gap-2">
                  <span className="text-xs text-muted">{current ? (current.isEnabled ? t("features.on") : t("features.off")) : t("common.none")}</span>
                  <Button size="sm" variant="outline" onClick={() => override.mutate({ feature, enabled: !(current?.isEnabled ?? false) })}>
                    {current?.isEnabled ? t("features.off") : t("features.on")}
                  </Button>
                </span>
              </li>
            );
          })}
        </ul>
      </section>
      <ConfirmDialog
        open={deleteOpen}
        title={t("common.delete")}
        description={t("tenants.deleteBody", { name: detail.name })}
        confirmLabel={t("common.delete")}
        destructive
        pending={remove.isPending}
        onOpenChange={setDeleteOpen}
        onConfirm={() => remove.mutate()}
      />
      <ConfirmDialog
        open={visitOpen}
        title={t("tenants.impersonate")}
        description={t("tenants.impersonateBody", { name: detail.name })}
        confirmLabel={t("tenants.impersonate")}
        onOpenChange={setVisitOpen}
        onConfirm={() => {
          void auth.impersonate(detail.id, detail.subdomain).then(() => navigate("/app"));
        }}
      />
    </div>
  );
}
