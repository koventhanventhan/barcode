import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy, Download, QrCode, RefreshCw } from "lucide-react";
import { useRef, useState } from "react";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { EmptyState } from "@/components/empty-state";
import { QrCodeSvg } from "@/components/qr-code";
import { downloadQrSvg } from "@/lib/download";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { t } from "@/config/strings";
import { fieldClass } from "@/features/academics/shared";
import {
  approveStaffRegistration,
  getStaffRegistrationLink,
  getStaffRegistrations,
  rejectStaffRegistration,
  rotateStaffRegistrationLink,
  updateStaffRegistrationLink,
  type StaffRegistrationRequestDto,
} from "@/features/staff/registrationApi";
import { ApiError } from "@/lib/api";
import type { Branch } from "@/lib/types";
import { cn } from "@/lib/utils";

type Template = { key: string; name: string; roleType: string; permissions: string[] };

export function StaffRegistrationsPanel({
  templates,
  branches,
  canManage,
}: {
  templates: Template[];
  branches: Branch[];
  canManage: boolean;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const qrRef = useRef<SVGSVGElement | null>(null);

  const [statusFilter, setStatusFilter] = useState<string>("Pending");
  const [page, setPage] = useState(1);
  const [rotateOpen, setRotateOpen] = useState(false);

  const [approveTarget, setApproveTarget] = useState<StaffRegistrationRequestDto | null>(null);
  const [approveTemplateKey, setApproveTemplateKey] = useState("");
  const [approveBranchIds, setApproveBranchIds] = useState<string[]>([]);
  const [approveNotes, setApproveNotes] = useState("");

  const [rejectTarget, setRejectTarget] = useState<StaffRegistrationRequestDto | null>(null);
  const [rejectNotes, setRejectNotes] = useState("");

  const linkQuery = useQuery({
    queryKey: ["staff-registration-link"],
    queryFn: getStaffRegistrationLink,
  });

  const requestsQuery = useQuery({
    queryKey: ["staff-registrations", statusFilter, page],
    queryFn: () =>
      getStaffRegistrations({
        status: statusFilter === "All" ? undefined : statusFilter,
        page,
        pageSize: 20,
      }),
  });

  const updateLink = useMutation({
    mutationFn: updateStaffRegistrationLink,
    onSuccess: () => {
      toast.success(t("staff.linkUpdated"));
      void queryClient.invalidateQueries({ queryKey: ["staff-registration-link"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const rotateLink = useMutation({
    mutationFn: rotateStaffRegistrationLink,
    onSuccess: () => {
      toast.success(t("staff.linkRotated"));
      setRotateOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["staff-registration-link"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const approveMutation = useMutation({
    mutationFn: (input: {
      id: string;
      templateKey: string;
      branchIds: string[];
      reviewNotes: string | null;
    }) =>
      approveStaffRegistration(input.id, {
        templateKey: input.templateKey || null,
        branchId: input.branchIds[0] || null,
        branchIds: input.branchIds,
        reviewNotes: input.reviewNotes,
      }),
    onSuccess: () => {
      toast.success(t("staff.registrationApproved"));
      setApproveTarget(null);
      void queryClient.invalidateQueries({ queryKey: ["staff-registrations"] });
      void queryClient.invalidateQueries({ queryKey: ["staff-registrations-count"] });
      void queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const rejectMutation = useMutation({
    mutationFn: (input: { id: string; reviewNotes: string | null }) =>
      rejectStaffRegistration(input.id, input.reviewNotes),
    onSuccess: () => {
      toast.success(t("staff.registrationRejected"));
      setRejectTarget(null);
      setRejectNotes("");
      void queryClient.invalidateQueries({ queryKey: ["staff-registrations"] });
      void queryClient.invalidateQueries({ queryKey: ["staff-registrations-count"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const link = linkQuery.data;
  const requests = requestsQuery.data?.items ?? [];
  const totalCount = requestsQuery.data?.totalCount ?? 0;
  const totalPages = Math.max(1, Math.ceil(totalCount / 20));

  function openApproveDialog(item: StaffRegistrationRequestDto) {
    const defaultTpl =
      item.requestedRoleType === "Teacher"
        ? templates.find((tpl) => tpl.key === "class-teacher")?.key ?? templates[0]?.key ?? ""
        : templates.find((tpl) => tpl.key === "front-desk")?.key ?? templates[0]?.key ?? "";
    setApproveTarget(item);
    setApproveTemplateKey(defaultTpl);
    setApproveBranchIds(item.branchId ? [item.branchId] : []);
    setApproveNotes("");
  }

  async function copyUrl(url: string) {
    try {
      await navigator.clipboard.writeText(url);
      toast.success(t("staff.linkCopied"));
    } catch {
      toast.error(t("errors.generic"));
    }
  }

  return (
    <div className="grid gap-4">
      <section className="rounded-lg border border-border bg-card p-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h2 className="text-sm font-semibold">{t("staff.registrationLinkTitle")}</h2>
            <p className="mt-1 text-xs text-muted">{t("staff.registrationLinkDescription")}</p>
          </div>
          {canManage && (!link || !link.hasLink) ? (
            <Button
              type="button"
              size="sm"
              disabled={rotateLink.isPending}
              onClick={() => rotateLink.mutate()}
            >
              <QrCode className="mr-1.5 h-4 w-4" aria-hidden="true" />
              {t("staff.generateLink")}
            </Button>
          ) : null}
        </div>

        {linkQuery.isLoading ? (
          <div className="mt-4">
            <PageSkeleton />
          </div>
        ) : link?.hasLink && link.registrationUrl ? (
          <div className="mt-4 grid gap-4 md:grid-cols-[1fr_auto]">
            <div className="grid gap-3">
              <div className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border px-3 py-2">
                <span className="text-sm font-medium">{t("staff.enableLink")}</span>
                <Switch
                  aria-label={t("staff.enableLink")}
                  checked={link.isEnabled}
                  disabled={!canManage || updateLink.isPending}
                  onCheckedChange={(checked) =>
                    updateLink.mutate({
                      isEnabled: checked,
                      defaultBranchId: link.defaultBranchId,
                    })
                  }
                />
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <Input
                  readOnly
                  aria-label={t("staff.registrationLinkTitle")}
                  value={link.registrationUrl}
                  className="flex-1 font-mono text-xs"
                />
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => void copyUrl(link.registrationUrl!)}
                >
                  <Copy className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                  {t("staff.copyLink")}
                </Button>
                {canManage ? (
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={rotateLink.isPending}
                    onClick={() => setRotateOpen(true)}
                  >
                    <RefreshCw className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                    {t("staff.rotateLink")}
                  </Button>
                ) : null}
              </div>

              {branches.length > 1 ? (
                <label className="grid gap-1 text-xs sm:max-w-xs">
                  <span className="font-medium text-muted">{t("staff.defaultBranch")}</span>
                  <select
                    className={fieldClass}
                    disabled={!canManage || updateLink.isPending}
                    value={link.defaultBranchId ?? ""}
                    onChange={(e) =>
                      updateLink.mutate({
                        isEnabled: link.isEnabled,
                        defaultBranchId: e.target.value || null,
                      })
                    }
                  >
                    <option value="">{t("common.none")}</option>
                    {branches.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.name}
                      </option>
                    ))}
                  </select>
                </label>
              ) : null}
            </div>

            {link.qr ? (
              <div className="flex flex-col items-center gap-2 rounded-md border border-border bg-background p-3">
                <QrCodeSvg ref={qrRef} qr={link.qr} label={t("staff.qrAlt")} className="h-36 w-36" />
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  onClick={() => downloadQrSvg(qrRef.current)}
                >
                  <Download className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                  {t("staff.downloadQr")}
                </Button>
              </div>
            ) : null}
          </div>
        ) : null}
      </section>

      <section className="rounded-lg border border-border bg-card">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border px-4 py-3">
          <h2 className="text-sm font-semibold">{t("staff.registrationsTitle")}</h2>
          <div className="flex flex-wrap gap-1">
            {(
              [
                ["Pending", t("staff.filterPending")],
                ["Approved", t("staff.filterApproved")],
                ["Rejected", t("staff.filterRejected")],
                ["All", t("staff.filterAll")],
              ] as const
            ).map(([key, label]) => (
              <button
                key={key}
                type="button"
                onClick={() => {
                  setStatusFilter(key);
                  setPage(1);
                }}
                className={cn(
                  "rounded-md px-2.5 py-1 text-xs",
                  statusFilter === key ? "bg-secondary font-medium" : "text-muted hover:bg-secondary",
                )}
              >
                {label}
              </button>
            ))}
          </div>
        </div>

        {requestsQuery.isLoading ? (
          <div className="p-4">
            <PageSkeleton />
          </div>
        ) : requestsQuery.isError ? (
          <p className="p-4 text-sm text-danger">{t("errors.load")}</p>
        ) : requests.length === 0 ? (
          <div className="p-4">
            <EmptyState title={t("staff.registrationsEmpty")} />
          </div>
        ) : (
          <>
            <ul className="divide-y divide-border">
              {requests.map((item) => (
                <li
                  key={item.id}
                  className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 text-sm"
                >
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium">{item.fullName}</span>
                      <span className="rounded bg-secondary px-2 py-0.5 text-xs">
                        {item.requestedRoleType}
                      </span>
                      <span className="rounded bg-secondary px-2 py-0.5 text-xs font-medium">
                        {item.status}
                      </span>
                    </div>
                    <p className="mt-0.5 text-xs text-muted">
                      {item.email}
                      {item.phone ? ` · ${item.phone}` : ""}
                      {item.branchName ? ` · ${item.branchName}` : ""}
                      {" · "}
                      {new Date(item.createdAt).toLocaleString()}
                    </p>
                    {item.notes ? <p className="mt-1 text-xs text-muted">{item.notes}</p> : null}
                    {item.reviewNotes ? (
                      <p className="mt-1 text-xs text-muted">{item.reviewNotes}</p>
                    ) : null}
                  </div>
                  {canManage && item.status === "Pending" ? (
                    <div className="flex items-center gap-2">
                      <Button size="sm" onClick={() => openApproveDialog(item)}>
                        {t("staff.approve")}
                      </Button>
                      <Button
                        size="sm"
                        variant="danger"
                        onClick={() => {
                          setRejectTarget(item);
                          setRejectNotes("");
                        }}
                      >
                        {t("staff.reject")}
                      </Button>
                    </div>
                  ) : null}
                </li>
              ))}
            </ul>
            {totalPages > 1 ? (
              <div className="flex items-center justify-between border-t border-border px-4 py-2 text-xs">
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
          </>
        )}
      </section>

      <ConfirmDialog
        open={rotateOpen}
        onOpenChange={setRotateOpen}
        title={t("staff.rotateConfirmTitle")}
        description={t("staff.rotateConfirmBody")}
        confirmLabel={t("staff.rotateLink")}
        destructive
        pending={rotateLink.isPending}
        onConfirm={() => rotateLink.mutate()}
      />

      <Dialog
        open={Boolean(approveTarget)}
        onOpenChange={(open) => {
          if (!open) setApproveTarget(null);
        }}
        title={t("staff.approveRegistration")}
        description={approveTarget ? `${approveTarget.fullName} (${approveTarget.email})` : undefined}
      >
        <form
          className="grid gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (!approveTarget) return;
            approveMutation.mutate({
              id: approveTarget.id,
              templateKey: approveTemplateKey,
              branchIds: approveBranchIds,
              reviewNotes: approveNotes.trim() || null,
            });
          }}
        >
          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("staff.template")}</span>
            <select
              className={fieldClass}
              value={approveTemplateKey}
              onChange={(e) => setApproveTemplateKey(e.target.value)}
            >
              {templates.map((tpl) => (
                <option key={tpl.key} value={tpl.key}>
                  {tpl.name} ({tpl.roleType})
                </option>
              ))}
            </select>
          </label>

          {branches.length > 0 ? (
            <fieldset>
              <legend className="mb-1 text-xs font-medium text-muted">{t("staff.branch")}</legend>
              <div className="grid gap-1">
                {branches.map((branch) => (
                  <label key={branch.id} className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={approveBranchIds.includes(branch.id)}
                      onChange={(e) => {
                        setApproveBranchIds((prev) =>
                          e.target.checked
                            ? [...prev, branch.id]
                            : prev.filter((id) => id !== branch.id),
                        );
                      }}
                    />
                    {branch.name}
                  </label>
                ))}
              </div>
            </fieldset>
          ) : null}

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("staff.reviewNotes")}</span>
            <Textarea
              rows={2}
              value={approveNotes}
              onChange={(e) => setApproveNotes(e.target.value)}
            />
          </label>

          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setApproveTarget(null)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={approveMutation.isPending}>
              {approveMutation.isPending ? t("common.saving") : t("staff.approve")}
            </Button>
          </div>
        </form>
      </Dialog>

      <Dialog
        open={Boolean(rejectTarget)}
        onOpenChange={(open) => {
          if (!open) setRejectTarget(null);
        }}
        title={t("staff.rejectRegistration")}
        description={rejectTarget ? `${rejectTarget.fullName} (${rejectTarget.email})` : undefined}
      >
        <form
          className="grid gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            if (!rejectTarget) return;
            rejectMutation.mutate({
              id: rejectTarget.id,
              reviewNotes: rejectNotes.trim() || null,
            });
          }}
        >
          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("staff.reviewNotes")}</span>
            <Textarea
              rows={2}
              value={rejectNotes}
              onChange={(e) => setRejectNotes(e.target.value)}
            />
          </label>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setRejectTarget(null)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" variant="danger" disabled={rejectMutation.isPending}>
              {rejectMutation.isPending ? t("common.saving") : t("staff.reject")}
            </Button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}
