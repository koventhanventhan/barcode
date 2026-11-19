import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { LifeBuoy, Plus, Send } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { EmptyState } from "@/components/empty-state";
import { useFeature } from "@/components/feature-gate";
import { fieldError, FormField } from "@/components/form-field";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { t } from "@/config/strings";
import { fieldClass } from "@/features/academics/shared";
import { useAuth } from "@/features/auth/AuthProvider";
import {
  closeSupportTicket,
  createSupportTicket,
  getTenantSupportTicket,
  getTenantSupportTickets,
  replySupportTicket,
  SUPPORT_TICKET_CATEGORIES,
  SUPPORT_TICKET_STATUSES,
} from "@/features/support/api";
import { ApiError } from "@/lib/api";
import { cn } from "@/lib/utils";

const createSchema = z.object({
  subject: z.string().trim().min(3).max(200),
  category: z.enum(SUPPORT_TICKET_CATEGORIES),
  body: z.string().trim().min(5).max(4000),
});

export function SupportTicketsPage() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { hasPermission, isReadOnly } = useAuth();
  const prioritySupport = useFeature("PrioritySupport");

  const [statusFilter, setStatusFilter] = useState("");
  const [page, setPage] = useState(1);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [newOpen, setNewOpen] = useState(false);
  const [closeOpen, setCloseOpen] = useState(false);
  const [replyBody, setReplyBody] = useState("");

  const listQuery = useQuery({
    queryKey: ["support-tickets", statusFilter, page],
    queryFn: () => getTenantSupportTickets({ status: statusFilter || undefined, page, pageSize: 20 }),
  });

  const activeId = selectedId ?? listQuery.data?.items[0]?.id ?? null;

  const detailQuery = useQuery({
    queryKey: ["support-ticket", activeId],
    enabled: Boolean(activeId),
    queryFn: () => getTenantSupportTicket(activeId!),
  });

  const form = useForm<z.infer<typeof createSchema>>({
    resolver: zodResolver(createSchema),
    defaultValues: { subject: "", category: "General", body: "" },
  });

  const createMutation = useMutation({
    mutationFn: createSupportTicket,
    onSuccess: (created) => {
      toast.success(t("support.createdToast", { number: created.ticketNumber }));
      setNewOpen(false);
      form.reset({ subject: "", category: "General", body: "" });
      setSelectedId(created.id);
      void queryClient.invalidateQueries({ queryKey: ["support-tickets"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const replyMutation = useMutation({
    mutationFn: (input: { id: string; body: string }) => replySupportTicket(input.id, input.body),
    onSuccess: () => {
      toast.success(t("support.replySent"));
      setReplyBody("");
      void queryClient.invalidateQueries({ queryKey: ["support-tickets"] });
      void queryClient.invalidateQueries({ queryKey: ["support-ticket", activeId] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const closeMutation = useMutation({
    mutationFn: (id: string) => closeSupportTicket(id),
    onSuccess: () => {
      toast.success(t("support.closedToast"));
      setCloseOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["support-tickets"] });
      void queryClient.invalidateQueries({ queryKey: ["support-ticket", activeId] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  if (!hasPermission("settings.manage")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (listQuery.isLoading) return <PageSkeleton />;
  if (listQuery.isError) return <p className="text-sm text-danger">{t("errors.load")}</p>;

  const items = listQuery.data?.items ?? [];
  const totalCount = listQuery.data?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / 20));
  const detail = detailQuery.data;

  return (
    <div className="grid gap-5">
      <PageHeader
        title={t("support.title")}
        description={t("support.description")}
        crumbs={[{ label: t("nav.settings") }, { label: t("support.title") }]}
        actions={
          <Button type="button" disabled={isReadOnly} onClick={() => setNewOpen(true)}>
            <Plus className="mr-1.5 h-4 w-4" aria-hidden="true" />
            {t("support.newTicket")}
          </Button>
        }
      />

      <section className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-border bg-card px-4 py-3 text-sm">
        <div className="flex items-center gap-2">
          <LifeBuoy className="h-4 w-4 text-primary" aria-hidden="true" />
          <span className="font-medium">
            {prioritySupport.isEnabled ? t("support.priorityBadge") : t("support.standardBadge")}
          </span>
          <span className="text-xs text-muted">
            {prioritySupport.isEnabled ? t("support.priorityActiveHint") : t("support.priorityUpgradeHint")}
          </span>
        </div>
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
      </section>

      {items.length === 0 ? (
        <EmptyState
          title={t("support.emptyTitle")}
          description={t("support.emptyDescription")}
          action={
            !isReadOnly ? (
              <Button type="button" onClick={() => setNewOpen(true)}>
                {t("support.newTicket")}
              </Button>
            ) : undefined
          }
        />
      ) : (
        <div className="grid gap-4 lg:grid-cols-[minmax(280px,360px)_1fr]">
          <section className="rounded-lg border border-border bg-card">
            <ul className="divide-y divide-border">
              {items.map((ticket) => (
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
                      <span className="font-mono text-xs text-muted">{ticket.ticketNumber}</span>
                      <div className="flex items-center gap-1.5">
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
                    <p className="mt-0.5 truncate text-xs text-muted">{ticket.lastMessagePreview}</p>
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
              <div className="p-6 text-sm text-muted">{t("support.selectTicket")}</div>
            ) : (
              <>
                <header className="flex flex-wrap items-start justify-between gap-3 border-b border-border px-4 py-3">
                  <div>
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-mono text-xs text-muted">{detail.ticketNumber}</span>
                      <span className="rounded bg-secondary px-2 py-0.5 text-xs font-medium">
                        {t(`support.cat_${detail.category}`)}
                      </span>
                      <span className="rounded bg-secondary px-2 py-0.5 text-xs font-medium">
                        {t(`support.status_${detail.status}`)}
                      </span>
                      {detail.isPriority ? (
                        <span className="rounded bg-primary/15 px-2 py-0.5 text-xs font-medium text-primary">
                          {t("support.priorityBadge")}
                        </span>
                      ) : null}
                    </div>
                    <h2 className="mt-1 text-base font-semibold">{detail.subject}</h2>
                    <p className="text-xs text-muted">
                      {detail.createdByName} ({detail.createdByEmail}) ·{" "}
                      {new Date(detail.createdAt).toLocaleString()}
                    </p>
                  </div>
                  {detail.status !== "Closed" && !isReadOnly ? (
                    <Button type="button" size="sm" variant="outline" onClick={() => setCloseOpen(true)}>
                      {t("support.closeTicket")}
                    </Button>
                  ) : null}
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

                {detail.status === "Closed" ? (
                  <p className="border-t border-border px-4 py-3 text-xs text-muted">
                    {t("support.ticketClosedHint")}
                  </p>
                ) : (
                  <form
                    className="grid gap-2 border-t border-border p-4"
                    onSubmit={(e) => {
                      e.preventDefault();
                      if (!replyBody.trim() || !detail) return;
                      replyMutation.mutate({ id: detail.id, body: replyBody.trim() });
                    }}
                  >
                    <Textarea
                      rows={3}
                      aria-label={t("support.sendReply")}
                      placeholder={t("support.replyPlaceholder")}
                      disabled={isReadOnly}
                      value={replyBody}
                      onChange={(e) => setReplyBody(e.target.value)}
                    />
                    <div className="flex justify-end">
                      <Button
                        type="submit"
                        size="sm"
                        disabled={isReadOnly || replyMutation.isPending || !replyBody.trim()}
                      >
                        <Send className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                        {t("support.sendReply")}
                      </Button>
                    </div>
                  </form>
                )}
              </>
            )}
          </section>
        </div>
      )}

      <Dialog
        open={newOpen}
        onOpenChange={setNewOpen}
        title={t("support.newTicket")}
        description={t("support.newTicketDescription")}
      >
        <form className="grid gap-3" onSubmit={form.handleSubmit((values) => createMutation.mutate(values))}>
          <FormField label={t("support.subject")} htmlFor="ticket-subject" error={fieldError(form, "subject")}>
            <Input id="ticket-subject" {...form.register("subject")} />
          </FormField>
          <FormField label={t("support.category")} htmlFor="ticket-category" error={fieldError(form, "category")}>
            <select id="ticket-category" className={fieldClass} {...form.register("category")}>
              {SUPPORT_TICKET_CATEGORIES.map((cat) => (
                <option key={cat} value={cat}>
                  {t(`support.cat_${cat}`)}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t("support.message")} htmlFor="ticket-body" error={fieldError(form, "body")}>
            <Textarea id="ticket-body" rows={4} {...form.register("body")} />
          </FormField>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setNewOpen(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={createMutation.isPending}>
              {createMutation.isPending ? t("common.saving") : t("support.submitTicket")}
            </Button>
          </div>
        </form>
      </Dialog>

      <ConfirmDialog
        open={closeOpen}
        onOpenChange={setCloseOpen}
        title={t("support.closeConfirmTitle")}
        description={t("support.closeConfirmBody")}
        confirmLabel={t("support.closeTicket")}
        pending={closeMutation.isPending}
        onConfirm={() => {
          if (detail) closeMutation.mutate(detail.id);
        }}
      />
    </div>
  );
}
