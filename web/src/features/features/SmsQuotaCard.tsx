import { AlertTriangle, MessageSquare } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import type { SmsQuotaSummary } from "@/lib/types";
import { cn } from "@/lib/utils";

export function SmsQuotaCard({
  quota,
  showLogLink = false,
}: {
  quota: SmsQuotaSummary;
  showLogLink?: boolean;
}) {
  const { t } = useTranslation();
  const pct = Math.min(100, Math.max(0, quota.usagePercentage));
  const barColor = quota.isExceeded100Percent
    ? "bg-danger"
    : quota.isWarning80Percent
      ? "bg-warning"
      : "bg-primary";

  return (
    <section className="mb-4 rounded-lg border border-border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <MessageSquare className="h-4 w-4 text-primary" aria-hidden="true" />
          <h2 className="text-sm font-semibold">{t("notifications.smsQuotaTitle")}</h2>
        </div>
        {showLogLink ? (
          <Link to="/app/settings/messages" className="text-xs font-medium text-primary hover:underline">
            {t("notifications.viewMessageLog")}
          </Link>
        ) : null}
      </div>

      <div className="mt-3 grid gap-3 sm:grid-cols-4">
        <div>
          <p className="text-xs text-muted">{t("notifications.usedSegments")}</p>
          <p className="text-lg font-semibold tabular-nums">
            {quota.usedSegments}
            {quota.monthlyQuota !== null ? ` / ${quota.monthlyQuota}` : ""}
          </p>
        </div>
        <div>
          <p className="text-xs text-muted">{t("notifications.remainingSegments")}</p>
          <p className="text-lg font-semibold tabular-nums">
            {quota.monthlyQuota !== null ? quota.remainingSegments : t("common.unlimited")}
          </p>
        </div>
        <div>
          <p className="text-xs text-muted">{t("notifications.extraSegments")}</p>
          <p className="text-lg font-semibold tabular-nums">{quota.extraSegments}</p>
        </div>
        <div>
          <p className="text-xs text-muted">{t("notifications.overQuotaPolicy")}</p>
          <p className="text-sm font-medium">
            {quota.overQuotaPolicy === "Block"
              ? t("notifications.policyBlock")
              : t("notifications.policyAllow")}
          </p>
        </div>
      </div>

      {quota.monthlyQuota !== null ? (
        <div className="mt-3">
          <div className="mb-1 flex items-center justify-between text-xs text-muted">
            <span>{t("notifications.monthlyUsage")}</span>
            <span className="tabular-nums">{quota.usagePercentage.toFixed(1)}%</span>
          </div>
          <div
            className="h-2 w-full overflow-hidden rounded-full bg-secondary"
            role="progressbar"
            aria-valuenow={Math.round(pct)}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-label={t("notifications.smsQuotaTitle")}
          >
            <div className={cn("h-full transition-all", barColor)} style={{ width: `${pct}%` }} />
          </div>
        </div>
      ) : null}

      {quota.isExceeded100Percent ? (
        <div className="mt-3 flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-xs text-danger">
          <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
          <span>{t("notifications.quotaExceededWarning", { extra: quota.extraSegments })}</span>
        </div>
      ) : quota.isWarning80Percent ? (
        <div className="mt-3 flex items-center gap-2 rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-foreground">
          <AlertTriangle className="h-4 w-4 shrink-0 text-warning" aria-hidden="true" />
          <span>{t("notifications.quota80Warning", { remaining: quota.remainingSegments })}</span>
        </div>
      ) : null}
    </section>
  );
}
