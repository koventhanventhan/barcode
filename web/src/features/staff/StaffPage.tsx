import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { t } from "@/config/strings";
import { z } from "zod";
import { DataTable, type DataColumn } from "@/components/data-table/DataTable";
import { EmptyState } from "@/components/empty-state";
import { FeatureGate, useFeature } from "@/components/feature-gate";
import { fieldError, FormField } from "@/components/form-field";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { useAuth } from "@/features/auth/AuthProvider";
import { fieldClass } from "@/features/academics/shared";
import type {
  StaffAttendanceEntry,
  StaffAttendanceSheet,
  StaffLeaveRequestDto,
} from "@/features/operations/api";
import { api, ApiError } from "@/lib/api";
import type { Branch } from "@/lib/types";
import { cn } from "@/lib/utils";

type StaffUser = {
  id: string;
  email: string;
  fullName: string;
  phone: string | null;
  roleType: string;
  customRoleName: string | null;
  isActive: boolean;
};

type Template = { key: string; name: string; roleType: string; permissions: string[] };

const schema = z.object({
  fullName: z.string().trim().min(1).max(200),
  email: z.string().trim().email(),
  password: z.string().min(8),
  phone: z.string().trim().max(40),
  templateKey: z.string(),
  branchId: z.string(),
  branchIds: z.array(z.string()),
});

export function StaffPage() {
  const toast = useToast();
  const { hasPermission, isReadOnly } = useAuth();
  const queryClient = useQueryClient();
  const staffLeaveFeature = useFeature("StaffLeave");

  const [tab, setTab] = useState<"directory" | "attendance" | "leaves">("directory");
  const [open, setOpen] = useState(false);
  const [leaveOpen, setLeaveOpen] = useState(false);

  const todayStr = new Date().toISOString().slice(0, 10);
  const [attendanceDate, setAttendanceDate] = useState<string>(todayStr);
  const [draftEntries, setDraftEntries] = useState<StaffAttendanceEntry[]>([]);

  const [leaveStaffId, setLeaveStaffId] = useState<string>("");
  const [leaveType, setLeaveType] = useState<string>("Casual");
  const [leaveStart, setLeaveStart] = useState<string>(todayStr);
  const [leaveEnd, setLeaveEnd] = useState<string>(todayStr);
  const [leaveReason, setLeaveReason] = useState<string>("");

  const users = useQuery({ queryKey: ["users"], queryFn: () => api<StaffUser[]>("/api/users") });
  const templates = useQuery({
    queryKey: ["staff-templates"],
    queryFn: () => api<Template[]>("/api/staff/role-templates"),
  });
  const branches = useQuery({ queryKey: ["branches"], queryFn: () => api<Branch[]>("/api/branches") });

  const attendanceSheet = useQuery({
    queryKey: ["staff-attendance", attendanceDate],
    enabled: tab === "attendance" && staffLeaveFeature.isEnabled,
    queryFn: () => api<StaffAttendanceSheet>(`/api/staff/attendance?date=${attendanceDate}`),
  });

  const leavesQuery = useQuery({
    queryKey: ["staff-leaves"],
    enabled: tab === "leaves" && staffLeaveFeature.isEnabled,
    queryFn: () => api<StaffLeaveRequestDto[]>("/api/staff/leaves"),
  });

  useEffect(() => {
    if (attendanceSheet.data?.entries) {
      setDraftEntries(attendanceSheet.data.entries);
    }
  }, [attendanceSheet.data]);

  const toggle = useMutation({
    mutationFn: (input: { id: string; isActive: boolean }) =>
      api(`/api/users/${input.id}/status`, {
        method: "PUT",
        body: JSON.stringify({ isActive: input.isActive }),
      }),
    onSuccess: (_data, input) => {
      toast.success(input.isActive ? t("staff.enabled") : t("staff.disabled"));
      void queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  const create = useMutation({
    mutationFn: (body: z.infer<typeof schema>) =>
      api("/api/staff", {
        method: "POST",
        body: JSON.stringify({
          ...body,
          phone: body.phone || null,
          branchId: body.branchIds[0] || body.branchId || null,
          branchIds: body.branchIds.length > 0 ? body.branchIds : body.branchId ? [body.branchId] : [],
          templateKey: body.templateKey || null,
        }),
      }),
    onSuccess: () => {
      toast.success(t("staff.saved"));
      setOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  const saveAttendance = useMutation({
    mutationFn: () =>
      api<StaffAttendanceSheet>("/api/staff/attendance", {
        method: "PUT",
        body: JSON.stringify({
          date: attendanceDate,
          entries: draftEntries.map((e) => ({
            staffUserId: e.staffUserId,
            status: e.status,
            checkInMinutes: e.checkInMinutes,
            checkOutMinutes: e.checkOutMinutes,
            notes: e.notes || null,
          })),
        }),
      }),
    onSuccess: () => {
      toast.success(t("staff.attendanceSaved"));
      void queryClient.invalidateQueries({ queryKey: ["staff-attendance", attendanceDate] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const createLeave = useMutation({
    mutationFn: () =>
      api("/api/staff/leaves", {
        method: "POST",
        body: JSON.stringify({
          staffUserId: leaveStaffId || null,
          leaveType,
          startDate: leaveStart,
          endDate: leaveEnd,
          reason: leaveReason,
        }),
      }),
    onSuccess: () => {
      toast.success(t("staff.leaveRequested"));
      setLeaveOpen(false);
      setLeaveReason("");
      void queryClient.invalidateQueries({ queryKey: ["staff-leaves"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const reviewLeave = useMutation({
    mutationFn: (input: { id: string; approved: boolean }) =>
      api(`/api/staff/leaves/${input.id}/review`, {
        method: "POST",
        body: JSON.stringify({ approved: input.approved }),
      }),
    onSuccess: () => {
      toast.success(t("staff.leaveReviewed"));
      void queryClient.invalidateQueries({ queryKey: ["staff-leaves"] });
      void queryClient.invalidateQueries({ queryKey: ["staff-attendance"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  if (!hasPermission("users.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (users.isLoading) return <PageSkeleton />;
  const rows = (users.data ?? []).filter((user) => user.roleType !== "Parent");
  const canManage = hasPermission("users.manage") && !isReadOnly;

  const columns: DataColumn<StaffUser>[] = [
    {
      id: "name",
      header: t("staff.name"),
      cell: (row) => row.fullName,
      exportValue: (row) => row.fullName,
      sortValue: (row) => row.fullName,
    },
    { id: "email", header: t("staff.email"), cell: (row) => row.email, exportValue: (row) => row.email },
    {
      id: "role",
      header: t("staff.role"),
      cell: (row) => row.customRoleName ?? row.roleType,
      exportValue: (row) => row.customRoleName ?? row.roleType,
    },
    {
      id: "status",
      header: t("staff.status"),
      cell: (row) => (
        <Switch
          checked={row.isActive}
          disabled={!canManage || toggle.isPending}
          aria-label={row.fullName}
          onCheckedChange={(checked) => toggle.mutate({ id: row.id, isActive: checked })}
        />
      ),
      exportValue: (row) => (row.isActive ? "active" : "inactive"),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t("staff.title")}
        description={t("staff.description")}
        crumbs={[{ label: t("nav.staff") }]}
        actions={
          <div className="flex flex-wrap gap-2">
            {tab === "leaves" && staffLeaveFeature.isEnabled ? (
              <Button
                type="button"
                variant="outline"
                disabled={isReadOnly}
                onClick={() => {
                  setLeaveStaffId(rows[0]?.id ?? "");
                  setLeaveOpen(true);
                }}
              >
                {t("staff.requestLeave")}
              </Button>
            ) : null}
            {canManage ? (
              <Button type="button" onClick={() => setOpen(true)}>
                {t("staff.new")}
              </Button>
            ) : null}
          </div>
        }
      />

      <div className="mb-4 flex gap-1 border-b border-border pb-2">
        {(
          [
            ["directory", t("staff.tabDirectory")],
            ["attendance", t("staff.tabAttendance")],
            ["leaves", t("staff.tabLeaves")],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            type="button"
            onClick={() => setTab(key)}
            className={cn(
              "rounded-md px-3 py-1.5 text-sm",
              tab === key ? "bg-secondary font-medium" : "text-muted hover:bg-secondary",
            )}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === "directory" ? (
        rows.length === 0 ? (
          <EmptyState title={t("staff.empty")} />
        ) : (
          <DataTable
            data={rows}
            columns={columns}
            filename="staff.csv"
            getRowId={(row) => row.id}
            enableExport={false}
          />
        )
      ) : null}

      {tab === "attendance" ? (
        <FeatureGate
          feature="StaffLeave"
          fallback={
            <section className="rounded-lg border border-border bg-card p-4 text-sm text-muted">
              {t("staff.leaveFeatureLocked")}
            </section>
          }
        >
          {attendanceSheet.isLoading ? (
            <PageSkeleton />
          ) : (
            <div className="grid gap-4">
              <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-card p-3">
                <label className="flex items-center gap-2 text-xs">
                  <span className="font-medium text-muted">{t("attendance.date")}</span>
                  <Input
                    type="date"
                    value={attendanceDate}
                    onChange={(e) => setAttendanceDate(e.target.value)}
                    className="w-44"
                  />
                </label>
                {canManage ? (
                  <Button
                    size="sm"
                    disabled={saveAttendance.isPending || draftEntries.length === 0}
                    onClick={() => saveAttendance.mutate()}
                  >
                    {t("staff.saveAttendance")}
                  </Button>
                ) : null}
              </div>

              {attendanceSheet.data ? (
                <div className="grid gap-3 sm:grid-cols-4">
                  <StatCard label={t("attendance.present")} value={String(attendanceSheet.data.presentCount)} />
                  <StatCard label={t("attendance.absent")} value={String(attendanceSheet.data.absentCount)} />
                  <StatCard label={t("attendance.late")} value={String(attendanceSheet.data.lateCount)} />
                  <StatCard label={t("staff.onLeave")} value={String(attendanceSheet.data.onLeaveCount)} />
                </div>
              ) : null}

              <div className="rounded-lg border border-border bg-card">
                <ul className="divide-y divide-border">
                  {draftEntries.map((entry, idx) => (
                    <li
                      key={entry.staffUserId}
                      className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 text-sm"
                    >
                      <div>
                        <p className="font-medium">{entry.fullName}</p>
                        <p className="text-xs text-muted">
                          {entry.roleName} · {entry.email}
                        </p>
                      </div>
                      <div className="flex flex-wrap items-center gap-2">
                        <select
                          aria-label={entry.fullName}
                          disabled={!canManage}
                          className={cn(fieldClass, "w-36")}
                          value={entry.status}
                          onChange={(e) => {
                            const copy = [...draftEntries];
                            copy[idx] = {
                              ...entry,
                              status: e.target.value as StaffAttendanceEntry["status"],
                            };
                            setDraftEntries(copy);
                          }}
                        >
                          <option value="Present">{t("attendance.present")}</option>
                          <option value="Absent">{t("attendance.absent")}</option>
                          <option value="Late">{t("attendance.late")}</option>
                          <option value="HalfDay">{t("staff.halfDay")}</option>
                          <option value="OnLeave">{t("staff.onLeave")}</option>
                        </select>
                        <Input
                          disabled={!canManage}
                          placeholder={t("staff.notesPlaceholder")}
                          value={entry.notes ?? ""}
                          onChange={(e) => {
                            const copy = [...draftEntries];
                            copy[idx] = { ...entry, notes: e.target.value };
                            setDraftEntries(copy);
                          }}
                          className="w-48 text-xs"
                        />
                      </div>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          )}
        </FeatureGate>
      ) : null}

      {tab === "leaves" ? (
        <FeatureGate
          feature="StaffLeave"
          fallback={
            <section className="rounded-lg border border-border bg-card p-4 text-sm text-muted">
              {t("staff.leaveFeatureLocked")}
            </section>
          }
        >
          {leavesQuery.isLoading ? (
            <PageSkeleton />
          ) : (leavesQuery.data ?? []).length === 0 ? (
            <EmptyState title={t("staff.emptyLeaves")} />
          ) : (
            <section className="rounded-lg border border-border bg-card">
              <ul className="divide-y divide-border">
                {(leavesQuery.data ?? []).map((leave) => (
                  <li
                    key={leave.id}
                    className="flex flex-wrap items-center justify-between gap-3 px-4 py-3 text-sm"
                  >
                    <div>
                      <p className="font-medium">
                        {leave.staffFullName} · {leave.leaveType} ({leave.daysCount}d)
                      </p>
                      <p className="text-xs text-muted">
                        {leave.startDate} → {leave.endDate} · {leave.reason}
                      </p>
                    </div>
                    <div className="flex items-center gap-2">
                      <span className="rounded bg-secondary px-2 py-0.5 text-xs font-medium">
                        {leave.status}
                      </span>
                      {canManage && leave.status === "Pending" ? (
                        <>
                          <Button
                            size="sm"
                            onClick={() => reviewLeave.mutate({ id: leave.id, approved: true })}
                          >
                            {t("staff.approve")}
                          </Button>
                          <Button
                            size="sm"
                            variant="danger"
                            onClick={() => reviewLeave.mutate({ id: leave.id, approved: false })}
                          >
                            {t("staff.reject")}
                          </Button>
                        </>
                      ) : null}
                    </div>
                  </li>
                ))}
              </ul>
            </section>
          )}
        </FeatureGate>
      ) : null}

      <StaffDialog
        open={open}
        templates={templates.data ?? []}
        branches={branches.data ?? []}
        pending={create.isPending}
        onOpenChange={setOpen}
        onSubmit={(body) => create.mutate(body)}
      />

      <Dialog
        open={leaveOpen}
        onOpenChange={setLeaveOpen}
        title={t("staff.requestLeave")}
        description={t("staff.requestLeaveDescription")}
      >
        <form
          className="grid gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            createLeave.mutate();
          }}
        >
          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("staff.name")}</span>
            <select
              className={fieldClass}
              value={leaveStaffId}
              onChange={(e) => setLeaveStaffId(e.target.value)}
            >
              {rows.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.fullName}
                </option>
              ))}
            </select>
          </label>
          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("staff.leaveType")}</span>
            <select
              className={fieldClass}
              value={leaveType}
              onChange={(e) => setLeaveType(e.target.value)}
            >
              <option value="Casual">Casual</option>
              <option value="Medical">Medical</option>
              <option value="Annual">Annual</option>
              <option value="Unpaid">Unpaid</option>
              <option value="Other">Other</option>
            </select>
          </label>
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="grid gap-1 text-xs">
              <span className="font-medium text-muted">{t("staff.startDate")}</span>
              <Input
                type="date"
                value={leaveStart}
                onChange={(e) => setLeaveStart(e.target.value)}
              />
            </label>
            <label className="grid gap-1 text-xs">
              <span className="font-medium text-muted">{t("staff.endDate")}</span>
              <Input
                type="date"
                value={leaveEnd}
                onChange={(e) => setLeaveEnd(e.target.value)}
              />
            </label>
          </div>
          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("tenants.reason")}</span>
            <Textarea
              rows={3}
              required
              value={leaveReason}
              onChange={(e) => setLeaveReason(e.target.value)}
            />
          </label>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setLeaveOpen(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={createLeave.isPending}>
              {t("common.save")}
            </Button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}

function StaffDialog({
  open,
  templates,
  branches,
  pending,
  onOpenChange,
  onSubmit,
}: {
  open: boolean;
  templates: Template[];
  branches: Branch[];
  pending: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (body: z.infer<typeof schema>) => void;
}) {
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: "",
      email: "",
      password: "",
      phone: "",
      templateKey: templates[0]?.key ?? "front-desk",
      branchId: "",
      branchIds: [],
    },
  });
  const template = templates.find((item) => item.key === form.watch("templateKey"));
  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={t("staff.new")} description={t("staff.description")}>
      <form className="grid gap-3" onSubmit={form.handleSubmit(onSubmit)}>
        <FormField label={t("staff.name")} htmlFor="staff-name" error={fieldError(form, "fullName")}>
          <Input id="staff-name" {...form.register("fullName")} />
        </FormField>
        <FormField label={t("staff.email")} htmlFor="staff-email" error={fieldError(form, "email")}>
          <Input id="staff-email" type="email" {...form.register("email")} />
        </FormField>
        <FormField label={t("staff.password")} htmlFor="staff-password" error={fieldError(form, "password")} hint={t("auth.passwordHint")}>
          <Input id="staff-password" type="password" {...form.register("password")} />
        </FormField>
        <FormField label={t("staff.phone")} htmlFor="staff-phone">
          <Input id="staff-phone" {...form.register("phone")} />
        </FormField>
        <FormField label={t("staff.template")} htmlFor="staff-template" error={fieldError(form, "templateKey")}>
          <select id="staff-template" className={fieldClass} {...form.register("templateKey")}>
            {templates.map((item) => (
              <option key={item.key} value={item.key}>
                {item.name}
              </option>
            ))}
          </select>
        </FormField>
        {template ? <p className="text-[13px] text-muted">{template.permissions.join(", ")}</p> : null}
        <fieldset>
          <legend className="mb-1 text-[13px] font-medium">{t("staff.branch")}</legend>
          <div className="grid gap-1">
            {branches.map((branch) => {
              const selected = form.watch("branchIds");
              return (
                <label key={branch.id} className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={selected.includes(branch.id)}
                    onChange={(event) => {
                      const next = event.target.checked
                        ? [...selected, branch.id]
                        : selected.filter((id) => id !== branch.id);
                      form.setValue("branchIds", next);
                      form.setValue("branchId", next[0] ?? "");
                    }}
                  />
                  {branch.name}
                </label>
              );
            })}
          </div>
        </fieldset>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            {t("common.cancel")}
          </Button>
          <Button type="submit" disabled={pending}>
            {t("common.save")}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
