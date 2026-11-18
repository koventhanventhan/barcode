import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Archive, Download, Play, Trash2 } from "lucide-react";
import { useEffect, useState } from "react";
import { t } from "@/config/strings";
import { FeatureGate, useFeature } from "@/components/feature-gate";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { useAuth } from "@/features/auth/AuthProvider";
import {
  formatBytes,
  type ReportCleanupResult,
  type ReportCleanupWorkspace,
  type TenantBackupPolicy,
  type TenantBackupRecord,
  type TenantBackupWorkspace,
  type TenantCleanupPolicy,
} from "@/features/operations/api";
import { api, ApiError } from "@/lib/api";
import { downloadApi } from "@/lib/download";

export function BackupsAndCleanupPage() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { hasPermission, isReadOnly } = useAuth();
  const canManage = hasPermission("settings.manage") && !isReadOnly;

  const autoBackupFeature = useFeature("AutoBackup");
  const reportCleanupFeature = useFeature("ReportCleanup");

  const [backupPolicy, setBackupPolicy] = useState<TenantBackupPolicy | null>(null);
  const [cleanupPolicy, setCleanupPolicy] = useState<TenantCleanupPolicy | null>(null);
  const [lastCleanupResult, setLastCleanupResult] = useState<ReportCleanupResult | null>(null);

  const backupsQuery = useQuery({
    queryKey: ["tenant-backups"],
    enabled: autoBackupFeature.isEnabled,
    queryFn: () => api<TenantBackupWorkspace>("/api/backups"),
  });

  const cleanupQuery = useQuery({
    queryKey: ["report-cleanup"],
    enabled: reportCleanupFeature.isEnabled,
    queryFn: () => api<ReportCleanupWorkspace>("/api/settings/cleanup"),
  });

  useEffect(() => {
    if (backupsQuery.data?.policy && !backupPolicy) {
      setBackupPolicy(backupsQuery.data.policy);
    }
  }, [backupsQuery.data, backupPolicy]);

  useEffect(() => {
    if (cleanupQuery.data?.policy && !cleanupPolicy) {
      setCleanupPolicy(cleanupQuery.data.policy);
    }
  }, [cleanupQuery.data, cleanupPolicy]);

  const runBackup = useMutation({
    mutationFn: () => api<TenantBackupRecord>("/api/backups/run", { method: "POST" }),
    onSuccess: (created) => {
      toast.success(t("backups.completedToast", { file: created.fileName }));
      void queryClient.invalidateQueries({ queryKey: ["tenant-backups"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const saveBackupPolicy = useMutation({
    mutationFn: (policy: TenantBackupPolicy) =>
      api<TenantBackupPolicy>("/api/backups/policy", {
        method: "PUT",
        body: JSON.stringify(policy),
      }),
    onSuccess: (saved) => {
      setBackupPolicy(saved);
      toast.success(t("backups.policySaved"));
      void queryClient.invalidateQueries({ queryKey: ["tenant-backups"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const saveCleanupPolicy = useMutation({
    mutationFn: (policy: TenantCleanupPolicy) =>
      api<TenantCleanupPolicy>("/api/settings/cleanup/policy", {
        method: "PUT",
        body: JSON.stringify(policy),
      }),
    onSuccess: (saved) => {
      setCleanupPolicy(saved);
      toast.success(t("cleanup.policySaved"));
      void queryClient.invalidateQueries({ queryKey: ["report-cleanup"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const runCleanup = useMutation({
    mutationFn: (dryRun: boolean) =>
      api<ReportCleanupResult>("/api/settings/cleanup/run", {
        method: "POST",
        body: JSON.stringify({ dryRun }),
      }),
    onSuccess: (result) => {
      setLastCleanupResult(result);
      toast.success(
        result.dryRun
          ? t("cleanup.dryRunToast", { count: result.totalPurged })
          : t("cleanup.executedToast", { count: result.totalPurged }),
      );
      void queryClient.invalidateQueries({ queryKey: ["report-cleanup"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  if (!hasPermission("settings.manage")) {
    return <p className="text-sm">{t("auth.deniedBody")}</p>;
  }

  return (
    <div className="grid gap-6">
      <PageHeader
        title={t("backups.title")}
        description={t("backups.description")}
        crumbs={[
          { label: t("nav.settings"), href: "/app/settings/features" },
          { label: t("nav.backups") },
        ]}
        actions={
          autoBackupFeature.isEnabled ? (
            <Button
              disabled={!canManage || runBackup.isPending}
              onClick={() => runBackup.mutate()}
            >
              <Play className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
              {runBackup.isPending ? t("common.saving") : t("backups.backupNow")}
            </Button>
          ) : null
        }
      />

      <FeatureGate
        feature="AutoBackup"
        fallback={
          <section className="rounded-lg border border-border bg-card p-4 text-sm text-muted">
            {t("backups.featureOffNotice")}
          </section>
        }
      >
        {backupsQuery.isLoading ? (
          <PageSkeleton />
        ) : (
          <div className="grid gap-4">
            {backupPolicy ? (
              <section className="rounded-lg border border-border bg-card p-4">
                <h2 className="text-sm font-semibold">{t("backups.policyTitle")}</h2>
                <p className="mb-3 text-xs text-muted">{t("backups.policyDescription")}</p>
                <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                  <div className="flex items-center justify-between rounded-md border border-border p-3">
                    <div>
                      <p className="text-sm font-medium">{t("backups.scheduledEnabled")}</p>
                      <p className="text-xs text-muted">{t("backups.scheduledHint")}</p>
                    </div>
                    <Switch
                      checked={backupPolicy.isScheduledEnabled}
                      disabled={!canManage}
                      aria-label={t("backups.scheduledEnabled")}
                      onCheckedChange={(checked) =>
                        setBackupPolicy({ ...backupPolicy, isScheduledEnabled: checked })
                      }
                    />
                  </div>

                  <label className="grid gap-1 text-xs">
                    <span className="font-medium text-muted">{t("backups.retentionDays")}</span>
                    <Input
                      type="number"
                      min={1}
                      max={365}
                      disabled={!canManage}
                      value={backupPolicy.retentionDays}
                      onChange={(e) =>
                        setBackupPolicy({
                          ...backupPolicy,
                          retentionDays: Number(e.target.value) || 1,
                        })
                      }
                    />
                  </label>

                  <label className="grid gap-1 text-xs">
                    <span className="font-medium text-muted">{t("backups.maxBackups")}</span>
                    <Input
                      type="number"
                      min={1}
                      max={100}
                      disabled={!canManage}
                      value={backupPolicy.maxBackupsToKeep}
                      onChange={(e) =>
                        setBackupPolicy({
                          ...backupPolicy,
                          maxBackupsToKeep: Number(e.target.value) || 1,
                        })
                      }
                    />
                  </label>

                  <div className="flex items-end">
                    <Button
                      disabled={!canManage || saveBackupPolicy.isPending}
                      onClick={() => saveBackupPolicy.mutate(backupPolicy)}
                    >
                      {t("common.save")}
                    </Button>
                  </div>
                </div>
              </section>
            ) : null}

            <section className="rounded-lg border border-border bg-card">
              <div className="border-b border-border px-4 py-3">
                <h2 className="text-sm font-semibold">{t("backups.historyTitle")}</h2>
                <p className="text-xs text-muted">{t("backups.historyDescription")}</p>
              </div>
              {(backupsQuery.data?.history ?? []).length === 0 ? (
                <p className="p-4 text-sm text-muted">{t("backups.empty")}</p>
              ) : (
                <ul className="divide-y divide-border">
                  {(backupsQuery.data?.history ?? []).map((item) => (
                    <li
                      key={item.id}
                      className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 text-sm"
                    >
                      <div>
                        <p className="font-mono text-xs font-medium">{item.fileName}</p>
                        <p className="text-xs text-muted">
                          {item.trigger} · {item.status} · {formatBytes(item.sizeBytes)} ·{" "}
                          {item.totalRecords} {t("backups.records")} ·{" "}
                          {new Date(item.createdAt).toLocaleString()}
                        </p>
                      </div>
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={() =>
                          void downloadApi(`/api/backups/${item.id}/download`, item.fileName)
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
          </div>
        )}
      </FeatureGate>

      <FeatureGate
        feature="ReportCleanup"
        fallback={
          <section className="rounded-lg border border-border bg-card p-4">
            <h2 className="text-sm font-semibold">{t("cleanup.title")}</h2>
            <p className="mt-1 text-xs text-muted">{t("cleanup.lockedNotice")}</p>
          </section>
        }
      >
        <section className="rounded-lg border border-border bg-card p-4">
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 className="text-sm font-semibold">{t("cleanup.title")}</h2>
              <p className="text-xs text-muted">{t("cleanup.description")}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button
                size="sm"
                variant="outline"
                disabled={!canManage || runCleanup.isPending}
                onClick={() => runCleanup.mutate(true)}
              >
                <Archive className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                {t("cleanup.dryRun")}
              </Button>
              <Button
                size="sm"
                variant="danger"
                disabled={!canManage || runCleanup.isPending}
                onClick={() => runCleanup.mutate(false)}
              >
                <Trash2 className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                {t("cleanup.purgeNow")}
              </Button>
            </div>
          </div>

          {cleanupQuery.data?.preview ? (
            <div className="mb-4 grid gap-3 sm:grid-cols-3">
              <StatCard
                label={t("cleanup.eligibleAuditLogs")}
                value={String(cleanupQuery.data.preview.eligibleAuditLogs)}
              />
              <StatCard
                label={t("cleanup.eligibleMessages")}
                value={String(cleanupQuery.data.preview.eligibleMessages)}
              />
              <StatCard
                label={t("cleanup.eligibleSoftDeleted")}
                value={String(cleanupQuery.data.preview.eligibleSoftDeletedRecords)}
              />
            </div>
          ) : null}

          {cleanupPolicy ? (
            <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("cleanup.auditDays")}</span>
                <Input
                  type="number"
                  min={1}
                  max={3650}
                  disabled={!canManage}
                  value={cleanupPolicy.auditLogRetentionDays}
                  onChange={(e) =>
                    setCleanupPolicy({
                      ...cleanupPolicy,
                      auditLogRetentionDays: Number(e.target.value) || 30,
                    })
                  }
                />
              </label>

              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("cleanup.messageDays")}</span>
                <Input
                  type="number"
                  min={1}
                  max={3650}
                  disabled={!canManage}
                  value={cleanupPolicy.notificationRetentionDays}
                  onChange={(e) =>
                    setCleanupPolicy({
                      ...cleanupPolicy,
                      notificationRetentionDays: Number(e.target.value) || 30,
                    })
                  }
                />
              </label>

              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("cleanup.softDeleteDays")}</span>
                <Input
                  type="number"
                  min={1}
                  max={3650}
                  disabled={!canManage}
                  value={cleanupPolicy.softDeletedRetentionDays}
                  onChange={(e) =>
                    setCleanupPolicy({
                      ...cleanupPolicy,
                      softDeletedRetentionDays: Number(e.target.value) || 30,
                    })
                  }
                />
              </label>

              <div className="flex items-center justify-between rounded-md border border-border p-3">
                <span className="text-xs font-medium">{t("cleanup.archiveBeforePurge")}</span>
                <Switch
                  checked={cleanupPolicy.archiveBeforePurge}
                  disabled={!canManage}
                  aria-label={t("cleanup.archiveBeforePurge")}
                  onCheckedChange={(checked) =>
                    setCleanupPolicy({ ...cleanupPolicy, archiveBeforePurge: checked })
                  }
                />
              </div>

              <div className="sm:col-span-2 lg:col-span-4">
                <Button
                  size="sm"
                  disabled={!canManage || saveCleanupPolicy.isPending}
                  onClick={() => saveCleanupPolicy.mutate(cleanupPolicy)}
                >
                  {t("common.save")}
                </Button>
              </div>
            </div>
          ) : null}

          {lastCleanupResult ? (
            <div className="mt-3 rounded-md border border-border bg-secondary p-3 text-xs">
              {lastCleanupResult.dryRun ? t("cleanup.dryRun") : t("cleanup.executedLabel")}:{" "}
              {lastCleanupResult.purgedAuditLogs} audit logs,{" "}
              {lastCleanupResult.purgedMessages} messages,{" "}
              {lastCleanupResult.purgedSoftDeletedRecords} soft-deleted records.
            </div>
          ) : null}
        </section>
      </FeatureGate>
    </div>
  );
}
