import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { RefreshCw, RotateCcw } from "lucide-react";
import { useState } from "react";
import { t } from "@/config/strings";
import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import {
  NOTIFICATION_CHANNELS,
  NOTIFICATION_MESSAGE_TYPES,
  NOTIFICATION_STATUSES,
} from "@/domain/catalog";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import type {
  NotificationMessage,
  NotificationMessageLogResponse,
} from "@/lib/types";
import { SmsQuotaCard } from "./SmsQuotaCard";

export function MessageLogPage() {
  const toast = useToast();
  const { isReadOnly, hasPermission } = useAuth();
  const canManage = hasPermission("settings.manage") && !isReadOnly;
  const queryClient = useQueryClient();

  const [messageType, setMessageType] = useState("");
  const [channel, setChannel] = useState("");
  const [status, setStatus] = useState("");
  const [fromDate, setFromDate] = useState("");
  const [toDate, setToDate] = useState("");
  const [search, setSearch] = useState("");

  const queryParams = new URLSearchParams();
  if (messageType) queryParams.set("messageType", messageType);
  if (channel) queryParams.set("channel", channel);
  if (status) queryParams.set("status", status);
  if (fromDate) queryParams.set("fromDate", fromDate);
  if (toDate) queryParams.set("toDate", toDate);
  if (search.trim()) queryParams.set("search", search.trim());

  const logQuery = useQuery({
    queryKey: ["notification-messages", messageType, channel, status, fromDate, toDate, search],
    queryFn: () =>
      api<NotificationMessageLogResponse>(
        `/api/notifications/messages?${queryParams.toString()}`,
      ),
  });

  const retryMutation = useMutation({
    mutationFn: (id: string) =>
      api<NotificationMessage>(`/api/notifications/messages/${id}/retry`, {
        method: "POST",
      }),
    onSuccess: (updated) => {
      toast.success(
        t("notifications.retryDone", {
          status: t(`notifications.status.${updated.status}`, {
            defaultValue: updated.status,
          }),
        }),
      );
      void queryClient.invalidateQueries({ queryKey: ["notification-messages"] });
      void queryClient.invalidateQueries({ queryKey: ["notification-settings"] });
      void queryClient.invalidateQueries({ queryKey: ["sms-quota"] });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : t("errors.generic"));
    },
  });

  if (logQuery.isLoading) return <PageSkeleton />;
  if (!logQuery.data) {
    return (
      <p className="text-sm">
        {t("errors.load")}{" "}
        <button
          type="button"
          className="text-primary"
          onClick={() => void logQuery.refetch()}
        >
          {t("common.retry")}
        </button>
      </p>
    );
  }

  const { items, quota } = logQuery.data;

  return (
    <div>
      <PageHeader
        title={t("notifications.logTitle")}
        description={t("notifications.logDescription")}
        crumbs={[
          { label: t("nav.settings"), href: "/app/settings/features" },
          { label: t("notifications.logTitle") },
        ]}
        actions={
          <Button
            variant="outline"
            onClick={() => void logQuery.refetch()}
          >
            <RefreshCw className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
            {t("notifications.refresh")}
          </Button>
        }
      />

      <SmsQuotaCard quota={quota} />

      <section className="mb-4 rounded-lg border border-border bg-card p-4">
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-6">
          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("notifications.filterType")}</span>
            <select
              aria-label={t("notifications.filterType")}
              className="h-9 rounded-md border border-border bg-background px-2 text-sm"
              value={messageType}
              onChange={(e) => setMessageType(e.target.value)}
            >
              <option value="">{t("notifications.allTypes")}</option>
              {NOTIFICATION_MESSAGE_TYPES.map((type) => (
                <option key={type} value={type}>
                  {t(`notifications.type.${type}`, { defaultValue: type })}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("notifications.filterChannel")}</span>
            <select
              aria-label={t("notifications.filterChannel")}
              className="h-9 rounded-md border border-border bg-background px-2 text-sm"
              value={channel}
              onChange={(e) => setChannel(e.target.value)}
            >
              <option value="">{t("notifications.allChannels")}</option>
              {NOTIFICATION_CHANNELS.map((ch) => (
                <option key={ch} value={ch}>
                  {t(`notifications.channel.${ch}`, { defaultValue: ch })}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("notifications.filterStatus")}</span>
            <select
              aria-label={t("notifications.filterStatus")}
              className="h-9 rounded-md border border-border bg-background px-2 text-sm"
              value={status}
              onChange={(e) => setStatus(e.target.value)}
            >
              <option value="">{t("notifications.allStatuses")}</option>
              {NOTIFICATION_STATUSES.map((st) => (
                <option key={st} value={st}>
                  {t(`notifications.status.${st}`, { defaultValue: st })}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("notifications.fromDate")}</span>
            <Input
              type="date"
              value={fromDate}
              onChange={(e) => setFromDate(e.target.value)}
            />
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("notifications.toDate")}</span>
            <Input
              type="date"
              value={toDate}
              onChange={(e) => setToDate(e.target.value)}
            />
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("notifications.filterSearch")}</span>
            <Input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t("notifications.searchPlaceholder")}
            />
          </label>
        </div>
      </section>

      {items.length === 0 ? (
        <EmptyState
          title={t("notifications.emptyLogTitle")}
          description={t("notifications.emptyLogDescription")}
        />
      ) : (
        <div className="overflow-x-auto rounded-lg border border-border bg-card">
          <table className="w-full border-collapse text-left text-sm">
            <thead>
              <tr className="border-b border-border bg-secondary/50 text-xs font-medium text-muted">
                <th className="px-3 py-2.5">{t("notifications.colTime")}</th>
                <th className="px-3 py-2.5">{t("notifications.colType")}</th>
                <th className="px-3 py-2.5">{t("notifications.colChannel")}</th>
                <th className="px-3 py-2.5">{t("notifications.colRecipient")}</th>
                <th className="px-3 py-2.5">{t("notifications.colMessage")}</th>
                <th className="px-3 py-2.5">{t("notifications.colSegments")}</th>
                <th className="px-3 py-2.5">{t("notifications.colStatus")}</th>
                <th className="px-3 py-2.5 text-right">{t("notifications.colActions")}</th>
              </tr>
            </thead>
            <tbody>
              {items.map((msg) => {
                const canRetry =
                  canManage &&
                  (msg.status === "Failed" || msg.status === "Skipped");
                return (
                  <tr
                    key={msg.id}
                    className="border-b border-border last:border-0 hover:bg-secondary/30"
                  >
                    <td className="whitespace-nowrap px-3 py-2.5 text-xs tabular-nums text-muted">
                      {new Date(msg.createdAt).toLocaleString()}
                    </td>
                    <td className="px-3 py-2.5 font-medium">
                      {t(`notifications.type.${msg.messageType}`, {
                        defaultValue: msg.messageType,
                      })}
                    </td>
                    <td className="px-3 py-2.5 text-xs">
                      <span className="rounded border border-border px-1.5 py-0.5">
                        {t(`notifications.channel.${msg.channel}`, {
                          defaultValue: msg.channel,
                        })}
                      </span>
                    </td>
                    <td className="px-3 py-2.5 text-xs">
                      <div className="font-medium tabular-nums">{msg.recipient}</div>
                      {msg.studentName ? (
                        <div className="text-muted">
                          {msg.studentName}
                          {msg.studentCode ? ` (${msg.studentCode})` : ""}
                        </div>
                      ) : null}
                    </td>
                    <td className="max-w-md px-3 py-2.5 text-xs">
                      <p className="line-clamp-2">{msg.body}</p>
                    </td>
                    <td className="px-3 py-2.5 text-xs tabular-nums">
                      {msg.channel === "Sms" ? (
                        <span>
                          {msg.smsSegments}
                          {msg.extraSegments > 0
                            ? ` (+${msg.extraSegments} ${t("notifications.extraShort")})`
                            : ""}
                        </span>
                      ) : (
                        "—"
                      )}
                    </td>
                    <td className="px-3 py-2.5 text-xs">
                      <div className="font-medium">
                        {t(`notifications.status.${msg.status}`, {
                          defaultValue: msg.status,
                        })}
                        {msg.isDeadLetter ? ` (${t("notifications.deadLetter")})` : ""}
                      </div>
                      {msg.statusReason ? (
                        <div className="text-[11px] text-muted">{msg.statusReason}</div>
                      ) : null}
                      {msg.attemptCount > 0 ? (
                        <div className="text-[11px] tabular-nums text-muted">
                          {t("notifications.attempts", {
                            count: msg.attemptCount,
                            max: msg.maxAttempts,
                          })}
                        </div>
                      ) : null}
                    </td>
                    <td className="px-3 py-2.5 text-right">
                      {canRetry ? (
                        <Button
                          variant="outline"
                          onClick={() => retryMutation.mutate(msg.id)}
                          disabled={retryMutation.isPending}
                        >
                          <RotateCcw className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
                          {t("common.retry")}
                        </Button>
                      ) : null}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
