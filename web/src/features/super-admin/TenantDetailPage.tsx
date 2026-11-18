import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { t } from "@/config/strings";
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
import { AddOnsPanel } from "@/features/super-admin/AddOnsPanel";
import { formatMoney, type PlatformSubscriptionInvoiceDto } from "@/features/fees/api";
import {
  formatBytes,
  type BackupRestoreResult,
  type TenantBackupRecord,
} from "@/features/operations/api";
import { api } from "@/lib/api";
import { downloadApi } from "@/lib/download";
import type { Plan, TenantDetail } from "@/lib/types";

export function TenantDetailPage() {
  const { id = "" } = useParams();
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
  const [invoiceAmount, setInvoiceAmount] = useState("15000");
  const [invoiceMonths, setInvoiceMonths] = useState(1);
  const [invoiceDays, setInvoiceDays] = useState(30);
  const [paymentRef, setPaymentRef] = useState("");
  const [restoreResult, setRestoreResult] = useState<BackupRestoreResult | null>(null);
  const tenant = useQuery({
    queryKey: ["tenant", id],
    queryFn: () => api<TenantDetail>(`/api/super-admin/tenants/${id}`),
  });
  const plans = useQuery({ queryKey: ["plans"], queryFn: () => api<Plan[]>("/api/super-admin/plans") });
  const platformInvoices = useQuery({
    queryKey: ["platform-invoices", id],
    queryFn: () => api<PlatformSubscriptionInvoiceDto[]>(`/api/super-admin/tenants/${id}/invoices`),
  });
  const tenantBackups = useQuery({
    queryKey: ["super-admin-tenant-backups", id],
    queryFn: () => api<TenantBackupRecord[]>(`/api/super-admin/tenants/${id}/backups`),
  });

  const restoreBackup = useMutation({
    mutationFn: (input: { backupId: string; dryRun: boolean }) =>
      api<BackupRestoreResult>(
        `/api/super-admin/tenants/${id}/backups/${input.backupId}/restore`,
        {
          method: "POST",
          body: JSON.stringify({ dryRun: input.dryRun }),
        },
      ),
    onSuccess: (res) => {
      setRestoreResult(res);
      toast.success(
        res.dryRun
          ? t("backups.dryRunValidToast")
          : t("backups.restoredToast", { count: res.restoredTotal }),
      );
      void refresh();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  function refresh() {
    return Promise.all([
      queryClient.invalidateQueries({ queryKey: ["tenant", id] }),
      queryClient.invalidateQueries({ queryKey: ["tenants"] }),
      queryClient.invalidateQueries({ queryKey: ["platform-invoices", id] }),
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

  const clearOverride = useMutation({
    mutationFn: (feature: string) =>
      api(`/api/super-admin/tenants/${id}/feature-overrides/${feature}`, {
        method: "DELETE",
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
      <section className="mt-4 rounded-lg border border-border bg-card p-4">
        <h2 className="text-sm font-semibold">{t("tenants.billingTitle")}</h2>
        <p className="mb-3 text-xs text-muted">{t("tenants.billingDescription")}</p>
        <form
          className="mb-4 grid gap-2 sm:grid-cols-4"
          onSubmit={(event) => {
            event.preventDefault();
            action.mutate({
              path: `/api/super-admin/tenants/${id}/invoices`,
              body: {
                amount: Number(invoiceAmount),
                periodMonths: invoiceMonths,
                daysToExtend: invoiceDays,
              },
            });
          }}
        >
          <Input
            type="number"
            min="1"
            step="0.01"
            value={invoiceAmount}
            onChange={(event) => setInvoiceAmount(event.target.value)}
            aria-label={t("fees.amount")}
            required
          />
          <Input
            type="number"
            min="1"
            value={invoiceMonths}
            onChange={(event) => setInvoiceMonths(Number(event.target.value))}
            aria-label={t("tenants.months")}
            required
          />
          <Input
            type="number"
            min="1"
            value={invoiceDays}
            onChange={(event) => setInvoiceDays(Number(event.target.value))}
            aria-label={t("tenants.days")}
            required
          />
          <Button type="submit" size="sm" disabled={action.isPending}>
            {t("tenants.createInvoice")}
          </Button>
        </form>
        {(platformInvoices.data ?? []).length > 0 ? (
          <div className="mb-3">
            <Input
              value={paymentRef}
              onChange={(event) => setPaymentRef(event.target.value)}
              placeholder={t("tenants.paymentReferencePlaceholder")}
              className="max-w-xs text-xs"
            />
          </div>
        ) : null}
        <ul className="divide-y divide-border">
          {(platformInvoices.data ?? []).map((inv) => (
            <li key={inv.id} className="flex flex-wrap items-center justify-between gap-2 py-2.5 text-sm">
              <div>
                <span className="font-mono font-medium">{inv.invoiceNumber}</span>
                <span className="ml-2 text-xs text-muted">
                  {inv.planName} · {formatMoney(inv.amount, inv.currency)} · +{inv.daysToExtend}d · {inv.status}
                </span>
              </div>
              <div className="flex items-center gap-2">
                {inv.status === "Pending" ? (
                  <Button
                    size="sm"
                    onClick={() =>
                      action.mutate({
                        path: `/api/super-admin/invoices/${inv.id}/mark-paid`,
                        body: { paymentReference: paymentRef || undefined },
                      })
                    }
                  >
                    {t("tenants.markPaidExtend")}
                  </Button>
                ) : null}
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() =>
                    void downloadApi(
                      `/api/super-admin/invoices/${inv.id}/pdf`,
                      `${inv.invoiceNumber.toLowerCase()}.pdf`,
                    )
                  }
                >
                  {t("students.exportPdf")}
                </Button>
              </div>
            </li>
          ))}
        </ul>
      </section>
      <section className="mt-4 rounded-lg border border-border bg-card p-4">
        <h2 className="text-sm font-semibold">{t("backups.supportRestoreTitle")}</h2>
        <p className="mb-3 text-xs text-muted">{t("backups.supportRestoreDescription")}</p>
        {(tenantBackups.data ?? []).length === 0 ? (
          <p className="text-xs text-muted">{t("backups.empty")}</p>
        ) : (
          <ul className="divide-y divide-border text-sm">
            {(tenantBackups.data ?? []).map((bkp) => (
              <li
                key={bkp.id}
                className="flex flex-wrap items-center justify-between gap-2 py-2.5"
              >
                <div>
                  <span className="font-mono text-xs font-medium">{bkp.fileName}</span>
                  <span className="ml-2 text-xs text-muted">
                    {formatBytes(bkp.sizeBytes)} · {bkp.totalRecords} {t("backups.records")} ·{" "}
                    {new Date(bkp.createdAt).toLocaleString()}
                  </span>
                </div>
                <div className="flex items-center gap-2">
                  <Button
                    size="sm"
                    variant="outline"
                    disabled={restoreBackup.isPending}
                    onClick={() => restoreBackup.mutate({ backupId: bkp.id, dryRun: true })}
                  >
                    {t("backups.validateDryRun")}
                  </Button>
                  <Button
                    size="sm"
                    disabled={restoreBackup.isPending}
                    onClick={() => restoreBackup.mutate({ backupId: bkp.id, dryRun: false })}
                  >
                    {t("backups.restoreNow")}
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        )}
        {restoreResult ? (
          <div className="mt-3 rounded-md border border-border bg-secondary p-3 text-xs">
            <p className="font-semibold">
              {restoreResult.dryRun ? t("backups.validateDryRun") : t("backups.restoreNow")} —{" "}
              {restoreResult.fileName}
            </p>
            <p className="mt-1 text-muted">
              Restorable:{" "}
              {Object.entries(restoreResult.restorableCounts)
                .map(([k, v]) => `${k}: ${v}`)
                .join(", ")}{" "}
              {restoreResult.dryRun ? "" : `(Restored: ${restoreResult.restoredTotal})`}
            </p>
          </div>
        ) : null}
      </section>
      <AddOnsPanel tenantId={id} planCode={detail.subscription?.planCode ?? ""} />
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
                  {current ? (
                    <Button size="sm" variant="outline" onClick={() => clearOverride.mutate(feature)}>
                      {t("common.reset")}
                    </Button>
                  ) : null}
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
