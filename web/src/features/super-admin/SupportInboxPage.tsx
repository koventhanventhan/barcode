import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Inbox, Send } from "lucide-react";
import { useState } from "react";
import { Link } from "react-router-dom";
import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { t } from "@/config/strings";
import { fieldClass } from "@/features/academics/shared";
import {
  getSuperAdminSupportInbox,
  getSuperAdminSupportTicket,
  replySuperAdminSupportTicket,
  SUPPORT_TICKET_STATUSES,
  updateSuperAdminSupportTicketStatus,
  type SupportTicketStatus,
} from "@/features/support/api";
import { ApiError } from "@/lib/api";
import { cn } from "@/lib/utils";

export function SupportInboxPage() {
  const toast = useToast();
  const queryClient = useQueryClient();

  const [statusFilter, setStatusFilter] = useState("");
  const [priorityOnly, setPriorityOnly] = useState(false);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [replyBody, setReplyBody] = useState("");
  const [replyStatus, setReplyStatus] = useState<"" | SupportTicketStatus>("");

  const inboxQuery = useQuery({
    queryKey: ["super-admin-support-inbox", statusFilter, priorityOnly, search, page],
    queryFn: () =>
      getSuperAdminSupportInbox({
        status: statusFilter || undefined,
        priorityOnly: priorityOnly || undefined,
        search: search || undefined,
        page,
        pageSize: 20,
      }),
  });

  const activeId = selectedId ?? inboxQuery.data?.items[0]?.id ?? null;

  const detailQuery = useQuery({
    queryKey: ["super-admin-support-ticket", activeId],
    enabled: Boolean(activeId),
    queryFn: () => getSuperAdminSupportTicket(activeId!),
  });

  const replyMutation = useMutation({
    mutationFn: (input: { id: string; body: string; status: SupportTicketStatus | null }) =>
      replySuperAdminSupportTicket(input.id, { body: input.body, status: input.status }),
    onSuccess: () => {
      toast.success(t("support.superAdminReplySent"));
      setReplyBody("");
      setReplyStatus("");
      void queryClient.invalidateQueries({ queryKey: ["super-admin-support-inbox"] });
      void queryClient.invalidateQueries({ queryKey: ["super-admin-support-ticket", activeId] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const statusMutation = useMutation({
    mutationFn: (input: { id: string; status: SupportTicketStatus }) =>
      updateSuperAdminSupportTicketStatus(input.id, input.status),
    onSuccess: () => {
      toast.success(t("support.statusUpdated"));
      void queryClient.invalidateQueries({ queryKey: ["super-admin-support-inbox"] });
      void queryClient.invalidateQueries({ queryKey: ["super-admin-support-ticket", activeId] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  if (inboxQuery.isLoading) return <PageSkeleton />;
  if (inboxQuery.isError || !inboxQuery.data) return <p className="text-sm text-danger">{t("errors.load")}</p>;

  const inbox = inboxQuery.data;
  const totalPages = Math.max(1, Math.ceil(inbox.totalCount / inbox.pageSize));
  const detail = detailQuery.data;

  return (
    <div className="grid gap-5">
      <PageHeader
        title={t("support.inboxTitle")}
        description={t("support.inboxDescription")}
        crumbs={[{ label: t("nav.platform") }, { label: t("support.inboxTitle") }]}
      />

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard label={t("support.openTickets")} value={String(inbox.openCount)} />
        <StatCard label={t("support.priorityOpen")} value={String(inbox.priorityOpenCount)} />
        <StatCard label={t("support.waitingOnInstitute")} value={String(inbox.waitingCount)} />
        <StatCard label={t("support.resolvedTickets")} value={String(inbox.resolvedCount)} />
      </div>

      <section className="flex flex-wrap items-center gap-3 rounded-lg border border-border bg-card p-3">
        <Input
          aria-label={t("common.search")}
          placeholder={t("support.searchPlaceholder")}
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          className="w-full sm:w-64"
        />
        <select
          aria-label={t("notifications.filterStatus")}
          className={cn(fieldClass, "w-44")}
          value={statusFilter}
          onChange={(e) => {
            setStatusFilter(e.target.value);
            setPage(1);
          }}
        >
          <option value="">{t("support.allStatuses")}</option>
          {SUPPORT_TICKET_STATUSES.map((s) => (
            <option key={s} value={s}>
              {t(`support.status_${s}`)}
            </option>
          ))}
        </select>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={priorityOnly}
            onChange={(e) => {
              setPriorityOnly(e.target.checked);
              setPage(1);
            }}
          />
          {t("support.priorityOnly")}
        </label>
      </section>

      {inbox.items.length === 0 ? (
        <EmptyState title={t("support.emptyTitle")} description={t("support.inboxDescription")} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-[minmax(300px,390px)_1fr]">
          <section className="rounded-lg border border-border bg-card">
            <ul className="divide-y divide-border">
              {inbox.items.map((ticket) => (
                <li key={ticket.id}>
                  <button
                    type="button"
                    onClick={() => setSelectedId(ticket.id)}
                    className={cn(
                      "w-full px-4 py-3 text-left transition-colors hover:bg-secondary",
                      activeId === ticket.id && "bg-secondary",
                    )}
                  >
                    <div className="flex flex-wrap items-center justify-between gap-1">
                      <span className="font-mono text-xs text-muted">
                        {ticket.ticketNumber} · {ticket.tenantSubdomain}
                      </span>
                      <div className="flex items-center gap-1">
                        {ticket.isPriority ? (
                          <span className="rounded bg-primary/15 px-1.5 py-0.5 text-[11px] font-medium text-primary">
                            {t("support.priorityBadge")}
                          </span>
                        ) : null}
                        <span className="rounded bg-secondary px-1.5 py-0.5 text-[11px] font-medium">
                          {t(`support.status_${ticket.status}`)}
                        </span>
                      </div>
                    </div>
                    <p className="mt-1 truncate text-sm font-medium">{ticket.subject}</p>
                    <p className="mt-0.5 truncate text-xs text-muted">
                      {ticket.tenantName}
                      {ticket.planName ? ` · ${ticket.planName}` : ""}
                    </p>
                  </button>
                </li>
              ))}
            </ul>
            {totalPages > 1 ? (
              <div className="flex items-center justify-between border-t border-border px-3 py-2 text-xs">
                <Button
                  size="sm"
                  variant="outline"
                  disabled={page <= 1}
                  onClick={() => setPage((p) => Math.max(1, p - 1))}
                >
                  {t("table.previous")}
                </Button>
                <span>{t("table.page", { page, pages: totalPages })}</span>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={page >= totalPages}
                  onClick={() => setPage((p) => p + 1)}
                >
                  {t("table.next")}
                </Button>
              </div>
            ) : null}
          </section>

          <section className="flex flex-col rounded-lg border border-border bg-card">
            {detailQuery.isLoading ? (
              <div className="p-4">
                <PageSkeleton />
              </div>
            ) : !detail ? (
              <div className="flex items-center gap-2 p-6 text-sm text-muted">
                <Inbox className="h-4 w-4" aria-hidden="true" />
                <span>{t("support.selectTicket")}</span>
              </div>
            ) : (
              <>
                <header className="flex flex-wrap items-start justify-between gap-3 border-b border-border px-4 py-3">
                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-xs text-muted">{detail.ticketNumber}</span>
                      <span className="rounded bg-secondary px-2 py-0.5 text-xs font-medium">
                        {t(`support.cat_${detail.category}`)}
                      </span>
                      {detail.isPriority ? (
                        <span className="rounded bg-primary/15 px-2 py-0.5 text-xs font-medium text-primary">
                          {t("support.priorityBadge")}
                        </span>
                      ) : null}
                    </div>
                    <h2 className="mt-1 text-base font-semibold">{detail.subject}</h2>
                    <p className="text-xs text-muted">
                      <Link to={`/admin/tenants/${detail.tenantId}`} className="text-primary hover:underline">
                        {detail.tenantName} ({detail.tenantSubdomain})
                      </Link>
                      {detail.planName ? ` · ${detail.planName}` : ""} · {detail.createdByName} ({detail.createdByEmail})
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <select
                      aria-label={t("support.updateStatus")}
                      className={cn(fieldClass, "w-44")}
                      value={detail.status}
                      disabled={statusMutation.isPending}
                      onChange={(e) =>
                        statusMutation.mutate({
                          id: detail.id,
                          status: e.target.value as SupportTicketStatus,
                        })
                      }
                    >
                      {SUPPORT_TICKET_STATUSES.map((s) => (
                        <option key={s} value={s}>
                          {t(`support.status_${s}`)}
                        </option>
                      ))}
                    </select>
                  </div>
                </header>

                <div className="grid max-h-[26rem] gap-3 overflow-y-auto p-4">
                  {detail.messages.map((msg) => (
                    <article
                      key={msg.id}
                      className={cn(
                        "rounded-md border p-3 text-sm",
                        msg.isFromSuperAdmin
                          ? "border-primary/30 bg-primary/5"
                          : "border-border bg-background",
                      )}
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-muted">
                        <span className="font-medium text-foreground">{msg.senderName}</span>
                        <span>{new Date(msg.createdAt).toLocaleString()}</span>
                      </div>
                      <p className="mt-1.5 whitespace-pre-wrap text-sm">{msg.body}</p>
                    </article>
                  ))}
                </div>

                <form
                  className="grid gap-2 border-t border-border p-4"
                  onSubmit={(e) => {
                    e.preventDefault();
                    if (!replyBody.trim() || !detail) return;
                    replyMutation.mutate({
                      id: detail.id,
                      body: replyBody.trim(),
                      status: replyStatus || null,
                    });
                  }}
                >
                  <Textarea
                    rows={3}
                    aria-label={t("support.sendReply")}
                    placeholder={t("support.replyPlaceholder")}
                    value={replyBody}
                    onChange={(e) => setReplyBody(e.target.value)}
                  />
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <select
                      aria-label={t("support.updateStatus")}
                      className={cn(fieldClass, "w-48")}
                      value={replyStatus}
                      onChange={(e) => setReplyStatus(e.target.value as "" | SupportTicketStatus)}
                    >
                      <option value="">{t("support.status_WaitingOnInstitute")}</option>
                      {SUPPORT_TICKET_STATUSES.map((s) => (
                        <option key={s} value={s}>
                          {t(`support.status_${s}`)}
                        </option>
                      ))}
                    </select>
                    <Button type="submit" size="sm" disabled={replyMutation.isPending || !replyBody.trim()}>
                      <Send className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                      {t("support.sendReply")}
                    </Button>
                  </div>
                </form>
              </>
            )}
          </section>
        </div>
      )}
    </div>
  );
}
