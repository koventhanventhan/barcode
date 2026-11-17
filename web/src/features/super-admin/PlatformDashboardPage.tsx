import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Download, Megaphone, Play, ShieldAlert, Trash2 } from "lucide-react";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { fieldClass } from "@/features/academics/shared";
import { formatMoney } from "@/features/fees/api";
import {
  formatBytes,
  type AuditLogItem,
  type PlatformAnnouncement,
  type PlatformBackupRecord,
  type PlatformBackupWorkspace,
  type SuperAdminDashboard,
} from "@/features/operations/api";
import { api, ApiError } from "@/lib/api";
import { downloadApi } from "@/lib/download";

export function PlatformDashboardPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();

  const [maintEnabled, setMaintEnabled] = useState(false);
  const [maintMessage, setMaintMessage] = useState("");
  const [retentionDays, setRetentionDays] = useState(14);

  const [annTitle, setAnnTitle] = useState("");
  const [annBody, setAnnBody] = useState("");
  const [annSeverity, setAnnSeverity] = useState<"Info" | "Warning" | "Critical">("Info");

  const [auditTenantFilter, setAuditTenantFilter] = useState("");
  const [auditActionFilter, setAuditActionFilter] = useState("");

  const dashboard = useQuery({
    queryKey: ["super-admin-dashboard"],
    queryFn: () => api<SuperAdminDashboard>("/api/super-admin/dashboard"),
  });

  const platformBackups = useQuery({
    queryKey: ["platform-backups"],
    queryFn: () => api<PlatformBackupWorkspace>("/api/super-admin/platform-backups"),
  });

  const announcements = useQuery({
    queryKey: ["platform-announcements"],
    queryFn: () => api<PlatformAnnouncement[]>("/api/super-admin/announcements"),
  });

  const auditParams = new URLSearchParams();
  if (auditTenantFilter) auditParams.set("tenantId", auditTenantFilter);
  if (auditActionFilter.trim()) auditParams.set("action", auditActionFilter.trim());
  auditParams.set("take", "50");

  const auditLogs = useQuery({
    queryKey: ["super-admin-audit", auditTenantFilter, auditActionFilter],
    queryFn: () =>
      api<AuditLogItem[]>(`/api/super-admin/audit-logs?${auditParams.toString()}`),
  });

  useEffect(() => {
    if (dashboard.data?.maintenance) {
      setMaintEnabled(dashboard.data.maintenance.isMaintenanceMode);
      setMaintMessage(dashboard.data.maintenance.message ?? "");
    }
  }, [dashboard.data]);

  useEffect(() => {
    if (platformBackups.data?.retentionDays) {
      setRetentionDays(platformBackups.data.retentionDays);
    }
  }, [platformBackups.data]);

  const updateMaintenance = useMutation({
    mutationFn: () =>
      api("/api/super-admin/maintenance", {
        method: "PUT",
        body: JSON.stringify({
          isMaintenanceMode: maintEnabled,
          message: maintMessage || null,
        }),
      }),
    onSuccess: () => {
      toast.success(t("superAdmin.maintenanceSaved"));
      void queryClient.invalidateQueries({ queryKey: ["super-admin-dashboard"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const runPlatformBackup = useMutation({
    mutationFn: () =>
      api<PlatformBackupRecord>("/api/super-admin/platform-backups/run", {
        method: "POST",
        body: JSON.stringify({ retentionDays }),
      }),
    onSuccess: (created) => {
      toast.success(t("superAdmin.platformBackupCompleted", { file: created.fileName }));
      void queryClient.invalidateQueries({ queryKey: ["platform-backups"] });
      void queryClient.invalidateQueries({ queryKey: ["super-admin-audit"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const saveRetention = useMutation({
    mutationFn: () =>
      api<PlatformBackupWorkspace>("/api/super-admin/platform-backups/retention", {
        method: "PUT",
        body: JSON.stringify({ retentionDays }),
      }),
    onSuccess: () => {
      toast.success(t("backups.policySaved"));
      void queryClient.invalidateQueries({ queryKey: ["platform-backups"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const publishAnnouncement = useMutation({
    mutationFn: () =>
      api("/api/super-admin/announcements", {
        method: "POST",
        body: JSON.stringify({
          title: annTitle,
          body: annBody,
          severity: annSeverity,
        }),
      }),
    onSuccess: () => {
      toast.success(t("superAdmin.announcementPublished"));
      setAnnTitle("");
      setAnnBody("");
      void queryClient.invalidateQueries({ queryKey: ["platform-announcements"] });
      void queryClient.invalidateQueries({ queryKey: ["super-admin-dashboard"] });
      void queryClient.invalidateQueries({ queryKey: ["super-admin-audit"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const toggleAnnouncement = useMutation({
    mutationFn: (input: { id: string; isActive: boolean }) =>
      api(`/api/super-admin/announcements/${input.id}/status`, {
        method: "PUT",
        body: JSON.stringify({ isActive: input.isActive }),
      }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["platform-announcements"] });
      void queryClient.invalidateQueries({ queryKey: ["super-admin-dashboard"] });
    },
  });

  const deleteAnnouncement = useMutation({
    mutationFn: (id: string) =>
      api(`/api/super-admin/announcements/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["platform-announcements"] });
      void queryClient.invalidateQueries({ queryKey: ["super-admin-dashboard"] });
    },
  });

  if (dashboard.isLoading) return <PageSkeleton />;
  if (!dashboard.data) return <p className="text-sm">{t("errors.load")}</p>;
  const data = dashboard.data;

  return (
    <div className="grid gap-5">
      <PageHeader
        title={t("superAdmin.dashboardTitle")}
        description={t("superAdmin.dashboardDescription")}
        crumbs={[{ label: t("nav.platform") }, { label: t("superAdmin.dashboardTitle") }]}
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label={t("superAdmin.totalTenants")}
          value={String(data.tenants.total)}
          hint={`${data.tenants.active} active · ${data.tenants.trial} trial · ${data.tenants.expired} expired · ${data.tenants.suspended} suspended`}
        />
        <StatCard
          label={t("superAdmin.mrr")}
          value={formatMoney(data.mrr, data.currency)}
          hint={`${t("superAdmin.collectedThisMonth")}: ${formatMoney(data.collectedThisMonth, data.currency)}`}
        />
        <StatCard
          label={t("superAdmin.trialsExpiring")}
          value={String(data.trialsExpiring.length)}
        />
        <StatCard
          label={t("superAdmin.failedJobs")}
          value={String(
            data.failedJobs.failedNotificationsCount + data.failedJobs.failedBackupsCount,
          )}
          hint={`${data.failedJobs.deadLetterNotificationsCount} dead-letter`}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="rounded-lg border border-border bg-card p-4">
          <div className="mb-3 flex items-center justify-between gap-2">
            <div className="flex items-center gap-2">
              <ShieldAlert className="h-4 w-4 text-primary" aria-hidden="true" />
              <div>
                <h2 className="text-sm font-semibold">{t("superAdmin.maintenanceTitle")}</h2>
                <p className="text-xs text-muted">{t("superAdmin.maintenanceDescription")}</p>
              </div>
            </div>
            <Switch
              checked={maintEnabled}
              aria-label={t("superAdmin.maintenanceTitle")}
              onCheckedChange={setMaintEnabled}
            />
          </div>
          <div className="flex flex-wrap gap-2">
            <Input
              className="flex-1"
              value={maintMessage}
              onChange={(e) => setMaintMessage(e.target.value)}
              placeholder={t("superAdmin.maintenanceMessagePlaceholder")}
            />
            <Button
              size="sm"
              disabled={updateMaintenance.isPending}
              onClick={() => updateMaintenance.mutate()}
            >
              {t("common.save")}
            </Button>
          </div>
        </section>

        <section className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("superAdmin.trialsExpiring")}</h2>
          <p className="mb-2 text-xs text-muted">{t("superAdmin.trialsExpiringHint")}</p>
          {data.trialsExpiring.length === 0 ? (
            <p className="text-xs text-muted">{t("superAdmin.noExpiringTrials")}</p>
          ) : (
            <ul className="divide-y divide-border text-sm">
              {data.trialsExpiring.map((tr) => (
                <li
                  key={tr.tenantId}
                  className="flex items-center justify-between gap-2 py-2"
                >
                  <div>
                    <Link
                      to={`/admin/tenants/${tr.tenantId}`}
                      className="font-medium text-primary hover:underline"
                    >
                      {tr.tenantName}
                    </Link>
                    <span className="ml-2 text-xs text-muted">
                      ({tr.subdomain}) · {tr.planName}
                    </span>
                  </div>
                  <span className="rounded bg-secondary px-2 py-0.5 text-xs font-medium tabular-nums">
                    {tr.daysRemaining}d left
                  </span>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="rounded-lg border border-border bg-card">
          <div className="border-b border-border px-4 py-3">
            <h2 className="text-sm font-semibold">{t("superAdmin.smsUsageTitle")}</h2>
          </div>
          <ul className="divide-y divide-border text-sm">
            {data.smsUsageByTenant.map((row) => (
              <li
                key={row.tenantId}
                className="flex items-center justify-between gap-2 px-4 py-2.5"
              >
                <div>
                  <Link
                    to={`/admin/tenants/${row.tenantId}`}
                    className="font-medium text-primary hover:underline"
                  >
                    {row.tenantName}
                  </Link>
                  <span className="ml-2 text-xs text-muted">({row.subdomain})</span>
                </div>
                <span className="font-mono text-xs tabular-nums">
                  {row.usedSegmentsThisMonth} / {row.monthlyQuota ?? "∞"} ({row.usagePercentage}%)
                  {row.extraSegmentsThisMonth > 0
                    ? ` · +${row.extraSegmentsThisMonth} extra`
                    : ""}
                </span>
              </li>
            ))}
          </ul>
        </section>

        <section className="rounded-lg border border-border bg-card">
          <div className="border-b border-border px-4 py-3">
            <h2 className="text-sm font-semibold">{t("superAdmin.storageUsageTitle")}</h2>
          </div>
          <ul className="divide-y divide-border text-sm">
            {data.storageByTenant.map((row) => (
              <li
                key={row.tenantId}
                className="flex items-center justify-between gap-2 px-4 py-2.5"
              >
                <div>
                  <Link
                    to={`/admin/tenants/${row.tenantId}`}
                    className="font-medium text-primary hover:underline"
                  >
                    {row.tenantName}
                  </Link>
                  <span className="ml-2 text-xs text-muted">({row.subdomain})</span>
                </div>
                <span className="font-mono text-xs tabular-nums">
                  {formatBytes(row.usedBytes)} / {row.limitGb ?? "∞"} GB
                </span>
              </li>
            ))}
          </ul>
        </section>
      </div>

      <section className="rounded-lg border border-border bg-card p-4">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="text-sm font-semibold">{t("superAdmin.platformBackupsTitle")}</h2>
            <p className="text-xs text-muted">{t("superAdmin.platformBackupsDescription")}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            <label className="flex items-center gap-1.5 text-xs">
              <span className="text-muted">{t("backups.retentionDays")} (14–30):</span>
              <Input
                type="number"
                min={14}
                max={30}
                value={retentionDays}
                onChange={(e) => setRetentionDays(Number(e.target.value) || 14)}
                className="w-20"
              />
            </label>
            <Button
              size="sm"
              variant="outline"
              disabled={saveRetention.isPending}
              onClick={() => saveRetention.mutate()}
            >
              {t("common.save")}
            </Button>
            <Button
              size="sm"
              disabled={runPlatformBackup.isPending}
              onClick={() => runPlatformBackup.mutate()}
            >
              <Play className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
              {t("superAdmin.runDatabaseBackup")}
            </Button>
          </div>
        </div>
        {(platformBackups.data?.history ?? []).length === 0 ? (
          <p className="text-xs text-muted">{t("backups.empty")}</p>
        ) : (
          <ul className="divide-y divide-border text-sm">
            {(platformBackups.data?.history ?? []).map((item) => (
              <li
                key={item.id}
                className="flex flex-wrap items-center justify-between gap-2 py-2.5"
              >
                <div>
                  <p className="font-mono text-xs font-medium">{item.fileName}</p>
                  <p className="text-xs text-muted">
                    {item.sqlCommandSummary} · {formatBytes(item.sizeBytes)} ·{" "}
                    {item.retentionDays}d retention ·{" "}
                    {new Date(item.createdAt).toLocaleString()}
                  </p>
                </div>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() =>
                    void downloadApi(
                      `/api/super-admin/platform-backups/${item.id}/download`,
                      item.fileName,
                    )
                  }
                >
                  <Download className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                  {t("backups.download")}
                </Button>
              </li>
            ))}
          </ul>
        )}
      </section>

      <div className="grid gap-4 lg:grid-cols-2">
        <section className="rounded-lg border border-border bg-card p-4">
          <div className="mb-3 flex items-center gap-2">
            <Megaphone className="h-4 w-4 text-primary" aria-hidden="true" />
            <div>
              <h2 className="text-sm font-semibold">{t("superAdmin.announcementsTitle")}</h2>
              <p className="text-xs text-muted">{t("superAdmin.announcementsDescription")}</p>
            </div>
          </div>
          <form
            className="mb-4 grid gap-2"
            onSubmit={(e) => {
              e.preventDefault();
              publishAnnouncement.mutate();
            }}
          >
            <div className="grid gap-2 sm:grid-cols-[1fr_auto]">
              <Input
                required
                value={annTitle}
                onChange={(e) => setAnnTitle(e.target.value)}
                placeholder={t("superAdmin.announcementTitlePlaceholder")}
              />
              <select
                className={fieldClass}
                value={annSeverity}
                onChange={(e) =>
                  setAnnSeverity(e.target.value as "Info" | "Warning" | "Critical")
                }
              >
                <option value="Info">Info</option>
                <option value="Warning">Warning</option>
                <option value="Critical">Critical</option>
              </select>
            </div>
            <Textarea
              rows={2}
              required
              value={annBody}
              onChange={(e) => setAnnBody(e.target.value)}
              placeholder={t("superAdmin.announcementBodyPlaceholder")}
            />
            <div>
              <Button type="submit" size="sm" disabled={publishAnnouncement.isPending}>
                {t("superAdmin.publishAnnouncement")}
              </Button>
            </div>
          </form>

          <ul className="divide-y divide-border text-sm">
            {(announcements.data ?? []).map((ann) => (
              <li
                key={ann.id}
                className="flex items-start justify-between gap-2 py-2.5"
              >
                <div>
                  <p className="font-medium">
                    [{ann.severity}] {ann.title}
                  </p>
                  <p className="text-xs text-muted">{ann.body}</p>
                </div>
                <div className="flex items-center gap-2">
                  <Switch
                    checked={ann.isActive}
                    aria-label={ann.title}
                    onCheckedChange={(checked) =>
                      toggleAnnouncement.mutate({ id: ann.id, isActive: checked })
                    }
                  />
                  <button
                    type="button"
                    className="text-muted hover:text-danger"
                    aria-label={t("common.delete")}
                    onClick={() => deleteAnnouncement.mutate(ann.id)}
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>

        <section className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("superAdmin.failedJobsAndErrors")}</h2>
          <p className="mb-2 text-xs text-muted">{t("superAdmin.failedJobsHint")}</p>
          {data.failedJobs.recentFailures.length === 0 ? (
            <p className="text-xs text-muted">{t("superAdmin.noRecentFailures")}</p>
          ) : (
            <ul className="divide-y divide-border text-xs">
              {data.failedJobs.recentFailures.map((f) => (
                <li key={f.id} className="py-2">
                  <div className="flex justify-between font-medium">
                    <span>
                      {f.category} · {f.tenantName ?? "Platform"}
                    </span>
                    <span className="text-muted">
                      {new Date(f.failedAt).toLocaleString()}
                    </span>
                  </div>
                  <p className="text-muted">
                    {f.target} — {f.error}
                  </p>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>

      <section className="rounded-lg border border-border bg-card p-4">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <div>
            <h2 className="text-sm font-semibold">{t("superAdmin.auditLogTitle")}</h2>
            <p className="text-xs text-muted">{t("superAdmin.auditLogDescription")}</p>
          </div>
          <div className="flex flex-wrap gap-2">
            <select
              aria-label={t("nav.tenants")}
              className={fieldClass}
              value={auditTenantFilter}
              onChange={(e) => setAuditTenantFilter(e.target.value)}
            >
              <option value="">{t("superAdmin.allTenants")}</option>
              {data.smsUsageByTenant.map((tItem) => (
                <option key={tItem.tenantId} value={tItem.tenantId}>
                  {tItem.tenantName}
                </option>
              ))}
            </select>
            <Input
              value={auditActionFilter}
              onChange={(e) => setAuditActionFilter(e.target.value)}
              placeholder={t("superAdmin.filterActionPlaceholder")}
              className="w-56"
            />
          </div>
        </div>
        <div className="max-h-80 overflow-y-auto">
          <ul className="divide-y divide-border text-xs">
            {(auditLogs.data ?? []).map((log) => (
              <li
                key={log.id}
                className="flex flex-wrap items-center justify-between gap-2 py-2"
              >
                <div>
                  <span className="font-mono font-semibold">{log.action}</span>
                  <span className="ml-2 text-muted">
                    {log.entityName} {log.entityId ? `#${log.entityId.slice(0, 8)}` : ""} ·{" "}
                    {log.actorEmail ?? "system"}
                    {log.isImpersonated ? " (impersonated)" : ""}
                  </span>
                </div>
                <span className="text-muted tabular-nums">
                  {new Date(log.createdAt).toLocaleString()}
                </span>
              </li>
            ))}
          </ul>
        </div>
      </section>
    </div>
  );
}
