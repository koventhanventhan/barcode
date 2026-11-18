import { useQuery } from "@tanstack/react-query";
import { HardDrive } from "lucide-react";
import { useTranslation } from "react-i18next";
import { formatBytes } from "@/features/operations/api";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";

type StorageUsage = {
  usedBytes: number;
  quotaBytes: number | null;
  maxGigabytes: number | null;
  usagePercentage: number;
};

export function StorageUsageCard() {
  const { t } = useTranslation();
  const usage = useQuery({
    queryKey: ["storage-usage"],
    queryFn: () => api<StorageUsage>("/api/storage/usage"),
  });
  if (!usage.data) return null;
  const pct = Math.min(100, Math.max(0, usage.data.usagePercentage));
  const quota = usage.data.quotaBytes == null ? t("common.unlimited") : formatBytes(usage.data.quotaBytes);
  return (
    <section className="mb-4 rounded-lg border border-border bg-card p-4">
      <div className="flex items-center gap-2">
        <HardDrive className="h-4 w-4 text-primary" aria-hidden="true" />
        <h2 className="text-sm font-semibold">{t("storage.title")}</h2>
      </div>
      <p className="mt-3 text-lg font-semibold tabular-nums">
        {formatBytes(usage.data.usedBytes)}
        <span className="text-sm font-medium text-muted"> / {quota}</span>
      </p>
      {usage.data.quotaBytes != null ? (
        <div className="mt-3">
          <div
            className="h-2 w-full overflow-hidden rounded-full bg-secondary"
            role="progressbar"
            aria-valuenow={Math.round(pct)}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-label={t("storage.title")}
          >
            <div className={cn("h-full", pct >= 100 ? "bg-danger" : pct >= 80 ? "bg-warning" : "bg-primary")} style={{ width: `${pct}%` }} />
          </div>
          <p className="mt-1 text-xs text-muted">{t("storage.hint")}</p>
        </div>
      ) : (
        <p className="mt-2 text-xs text-muted">{t("storage.hint")}</p>
      )}
    </section>
  );
}
