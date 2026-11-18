import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { t } from "@/config/strings";
import { z } from "zod";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { DataTable, type DataColumn } from "@/components/data-table/DataTable";
import { EmptyState } from "@/components/empty-state";
import { fieldError, FormField } from "@/components/form-field";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { FeatureGate } from "@/components/feature-gate";
import { useAuth } from "@/features/auth/AuthProvider";
import { AcademicNav, fieldClass, type Batch, type BatchGroup, type EducationLevel } from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";
import type { Branch } from "@/lib/types";

const batchSchema = z.object({
  name: z.string().trim().min(1).max(120),
  code: z.string().trim().min(1).max(32),
  educationLevelId: z.string().uuid(),
  branchId: z.string().optional(),
  batchGroupId: z.string().optional(),
  academicYear: z.string().trim().max(20).optional(),
  isActive: z.boolean(),
});

type BatchForm = z.infer<typeof batchSchema>;

const groupSchema = z.object({
  name: z.string().trim().min(1).max(120),
  code: z.string().trim().min(1).max(32),
  isActive: z.boolean(),
});

export function BatchesPage() {
  const toast = useToast();
  const { hasPermission, isReadOnly } = useAuth();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Batch | null>(null);
  const [open, setOpen] = useState(false);
  const [removeId, setRemoveId] = useState<string | null>(null);
  const [groupOpen, setGroupOpen] = useState(false);
  const canManage = hasPermission("academics.manage") && !isReadOnly;
  const batches = useQuery({ queryKey: ["batches"], queryFn: () => api<Batch[]>("/api/batches") });
  const levels = useQuery({ queryKey: ["levels"], queryFn: () => api<EducationLevel[]>("/api/education-levels") });
  const branches = useQuery({ queryKey: ["branches"], queryFn: () => api<Branch[]>("/api/branches") });
  const groups = useQuery({
    queryKey: ["batch-groups"],
    queryFn: () => api<BatchGroup[]>("/api/batch-groups"),
    retry: false,
  });

  const save = useMutation({
    mutationFn: (input: { id?: string; body: BatchForm }) => {
      const payload = {
        ...input.body,
        branchId: input.body.branchId || null,
        batchGroupId: input.body.batchGroupId || null,
        academicYear: input.body.academicYear || null,
      };
      return api<Batch>(input.id ? `/api/batches/${input.id}` : "/api/batches", {
        method: input.id ? "PUT" : "POST",
        body: JSON.stringify(payload),
      });
    },
    onSuccess: () => {
      toast.success(t("academic.saved"));
      setOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["batches"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/batches/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("academic.deleted"));
      setRemoveId(null);
      void queryClient.invalidateQueries({ queryKey: ["batches"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  const saveGroup = useMutation({
    mutationFn: (body: z.infer<typeof groupSchema>) =>
      api<BatchGroup>("/api/batch-groups", { method: "POST", body: JSON.stringify(body) }),
    onSuccess: () => {
      toast.success(t("academic.saved"));
      setGroupOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["batch-groups"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  if (!hasPermission("academics.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (batches.isLoading) return <PageSkeleton />;

  const columns: DataColumn<Batch>[] = [
    { id: "name", header: t("academic.code"), cell: (row) => <span className="tabular-nums">{row.code}</span>, exportValue: (row) => row.code, sortValue: (row) => row.code },
    { id: "title", header: t("nav.batches"), cell: (row) => row.name, exportValue: (row) => row.name, sortValue: (row) => row.name },
    { id: "level", header: t("academic.level"), cell: (row) => row.educationLevelName, exportValue: (row) => row.educationLevelName },
    { id: "group", header: t("academic.group"), cell: (row) => row.batchGroupName ?? t("common.none"), exportValue: (row) => row.batchGroupName ?? "" },
    { id: "count", header: t("academic.students"), cell: (row) => <span className="tabular-nums">{row.studentCount}</span>, sortValue: (row) => row.studentCount, exportValue: (row) => String(row.studentCount) },
    {
      id: "actions",
      header: "",
      enableHiding: false,
      cell: (row) =>
        canManage ? (
          <span className="flex gap-2">
            <button type="button" className="text-primary" onClick={() => { setEditing(row); setOpen(true); }}>{t("common.edit")}</button>
            <button type="button" className="text-danger" onClick={() => setRemoveId(row.id)}>{t("common.delete")}</button>
          </span>
        ) : null,
    },
  ];

  return (
    <div>
      <PageHeader
        title={t("academic.batchesTitle")}
        description={t("academic.batchesDescription")}
        crumbs={[{ label: t("nav.academic"), href: "/app/academics/levels" }, { label: t("nav.batches") }]}
        actions={canManage ? <Button type="button" onClick={() => { setEditing(null); setOpen(true); }}>{t("academic.newBatch")}</Button> : null}
      />
      <AcademicNav />
      {batches.data && batches.data.length > 0 ? (
        <DataTable data={batches.data} columns={columns} filename="batches.csv" getRowId={(row) => row.id} enableExport={false} />
      ) : (
        <EmptyState title={t("academic.emptyBatches")} action={canManage ? <Button type="button" onClick={() => { setEditing(null); setOpen(true); }}>{t("academic.newBatch")}</Button> : null} />
      )}
      <BatchDialog
        open={open}
        editing={editing}
        levels={(levels.data ?? []).filter((level) => level.isEnabled)}
        branches={branches.data ?? []}
        groups={groups.data ?? []}
        pending={save.isPending}
        onOpenChange={setOpen}
        onSubmit={(body) => save.mutate({ id: editing?.id, body })}
      />
      <FeatureGate feature="BatchGroups" fallback={null}>
        <section className="mt-8">
          <div className="mb-3 flex items-center justify-between gap-2">
            <div>
              <h2 className="text-base font-semibold">{t("academic.groupsTitle")}</h2>
              <p className="text-[13px] text-muted">{t("academic.groupsDescription")}</p>
            </div>
            {canManage ? <Button type="button" variant="outline" onClick={() => setGroupOpen(true)}>{t("academic.newGroup")}</Button> : null}
          </div>
          {groups.data && groups.data.length > 0 ? (
            <ul className="divide-y divide-border rounded-lg border border-border bg-card">
              {groups.data.map((group) => (
                <li key={group.id} className="flex items-center justify-between px-3 py-2 text-sm">
                  <span>{group.name}</span>
                  <span className="tabular-nums text-muted">{group.code}</span>
                </li>
              ))}
            </ul>
          ) : (
            <EmptyState title={t("academic.emptyGroups")} />
          )}
        </section>
      </FeatureGate>
      <GroupDialog open={groupOpen} pending={saveGroup.isPending} onOpenChange={setGroupOpen} onSubmit={(body) => saveGroup.mutate(body)} />
      <ConfirmDialog
        open={Boolean(removeId)}
        title={t("academic.deleteBatch")}
        description={t("academic.deleteBody")}
        confirmLabel={t("common.delete")}
        destructive
        pending={remove.isPending}
        onOpenChange={(next) => { if (!next) setRemoveId(null); }}
        onConfirm={() => { if (removeId) remove.mutate(removeId); }}
      />
    </div>
  );
}

function BatchDialog({
  open,
  editing,
  levels,
  branches,
  groups,
  pending,
  onOpenChange,
  onSubmit,
}: {
  open: boolean;
  editing: Batch | null;
  levels: EducationLevel[];
  branches: Branch[];
  groups: BatchGroup[];
  pending: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (body: BatchForm) => void;
}) {
  const form = useForm<BatchForm>({
    resolver: zodResolver(batchSchema),
    values: {
      name: editing?.name ?? "",
      code: editing?.code ?? "",
      educationLevelId: editing?.educationLevelId ?? levels[0]?.id ?? "",
      branchId: editing?.branchId ?? "",
      batchGroupId: editing?.batchGroupId ?? "",
      academicYear: editing?.academicYear ?? "",
      isActive: editing?.isActive ?? true,
    },
  });

  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={editing ? t("academic.editBatch") : t("academic.newBatch")}>
      <form className="grid gap-3" onSubmit={form.handleSubmit(onSubmit)}>
        <FormField label={t("staff.name")} htmlFor="batch-name" error={fieldError(form, "name")}>
          <Input id="batch-name" {...form.register("name")} />
        </FormField>
        <FormField label={t("academic.code")} htmlFor="batch-code" error={fieldError(form, "code")}>
          <Input id="batch-code" {...form.register("code")} />
        </FormField>
        <FormField label={t("academic.level")} htmlFor="batch-level" error={fieldError(form, "educationLevelId")}>
          <select id="batch-level" className={fieldClass} {...form.register("educationLevelId")}>
            {levels.map((level) => <option key={level.id} value={level.id}>{level.name}</option>)}
          </select>
        </FormField>
        <FormField label={t("academic.branch")} htmlFor="batch-branch">
          <select id="batch-branch" className={fieldClass} {...form.register("branchId")}>
            <option value="">{t("common.none")}</option>
            {branches.map((branch) => <option key={branch.id} value={branch.id}>{branch.name}</option>)}
          </select>
        </FormField>
        {groups.length > 0 ? (
          <FormField label={t("academic.group")} htmlFor="batch-group">
            <select id="batch-group" className={fieldClass} {...form.register("batchGroupId")}>
              <option value="">{t("common.none")}</option>
              {groups.map((group) => <option key={group.id} value={group.id}>{group.name}</option>)}
            </select>
          </FormField>
        ) : null}
        <FormField label={t("academic.year")} htmlFor="batch-year">
          <Input id="batch-year" {...form.register("academicYear")} />
        </FormField>
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" {...form.register("isActive")} />
          {t("common.active")}
        </label>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>{t("common.cancel")}</Button>
          <Button type="submit" disabled={pending}>{t("common.save")}</Button>
        </div>
      </form>
    </Dialog>
  );
}

function GroupDialog({
  open,
  pending,
  onOpenChange,
  onSubmit,
}: {
  open: boolean;
  pending: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (body: z.infer<typeof groupSchema>) => void;
}) {
  const form = useForm<z.infer<typeof groupSchema>>({
    resolver: zodResolver(groupSchema),
    defaultValues: { name: "", code: "", isActive: true },
  });
  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={t("academic.newGroup")}>
      <form className="grid gap-3" onSubmit={form.handleSubmit(onSubmit)}>
        <FormField label={t("academic.groupsTitle")} htmlFor="group-name" error={fieldError(form, "name")}>
          <Input id="group-name" {...form.register("name")} />
        </FormField>
        <FormField label={t("academic.code")} htmlFor="group-code" error={fieldError(form, "code")}>
          <Input id="group-code" {...form.register("code")} />
        </FormField>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>{t("common.cancel")}</Button>
          <Button type="submit" disabled={pending}>{t("common.save")}</Button>
        </div>
      </form>
    </Dialog>
  );
}
