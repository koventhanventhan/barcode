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
import { useAuth } from "@/features/auth/AuthProvider";
import { AcademicNav, fieldClass, type EducationLevel, type Subject } from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";

const schema = z.object({
  name: z.string().trim().min(1).max(120),
  code: z.string().trim().min(1).max(32),
  educationLevelId: z.string().optional(),
  isActive: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

export function SubjectsPage() {
  const toast = useToast();
  const { hasPermission, isReadOnly } = useAuth();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Subject | null>(null);
  const [open, setOpen] = useState(false);
  const [removeId, setRemoveId] = useState<string | null>(null);
  const canManage = hasPermission("academics.manage") && !isReadOnly;
  const subjects = useQuery({ queryKey: ["subjects"], queryFn: () => api<Subject[]>("/api/subjects") });
  const levels = useQuery({ queryKey: ["levels"], queryFn: () => api<EducationLevel[]>("/api/education-levels") });
  const save = useMutation({
    mutationFn: (input: { id?: string; body: FormValues }) =>
      api(input.id ? `/api/subjects/${input.id}` : "/api/subjects", {
        method: input.id ? "PUT" : "POST",
        body: JSON.stringify({ ...input.body, educationLevelId: input.body.educationLevelId || null }),
      }),
    onSuccess: () => {
      toast.success(t("academic.saved"));
      setOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["subjects"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/subjects/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("academic.deleted"));
      setRemoveId(null);
      void queryClient.invalidateQueries({ queryKey: ["subjects"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  if (!hasPermission("academics.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (subjects.isLoading) return <PageSkeleton />;

  const columns: DataColumn<Subject>[] = [
    { id: "code", header: t("academic.code"), cell: (row) => <span className="tabular-nums">{row.code}</span>, exportValue: (row) => row.code, sortValue: (row) => row.code },
    { id: "name", header: t("staff.name"), cell: (row) => row.name, exportValue: (row) => row.name, sortValue: (row) => row.name },
    { id: "level", header: t("academic.level"), cell: (row) => row.educationLevelName ?? t("common.none"), exportValue: (row) => row.educationLevelName ?? "" },
    {
      id: "actions",
      header: "",
      cell: (row) => canManage ? (
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
        title={t("academic.subjectsTitle")}
        description={t("academic.subjectsDescription")}
        crumbs={[{ label: t("nav.academic"), href: "/app/academics/levels" }, { label: t("nav.subjects") }]}
        actions={canManage ? <Button type="button" onClick={() => { setEditing(null); setOpen(true); }}>{t("academic.newSubject")}</Button> : null}
      />
      <AcademicNav />
      {subjects.data && subjects.data.length > 0 ? (
        <DataTable data={subjects.data} columns={columns} filename="subjects.csv" getRowId={(row) => row.id} enableExport={false} />
      ) : (
        <EmptyState title={t("academic.emptySubjects")} />
      )}
      <SubjectDialog
        open={open}
        editing={editing}
        levels={(levels.data ?? []).filter((level) => level.isEnabled)}
        pending={save.isPending}
        onOpenChange={setOpen}
        onSubmit={(body) => save.mutate({ id: editing?.id, body })}
      />
      <ConfirmDialog
        open={Boolean(removeId)}
        title={t("academic.deleteSubject")}
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

function SubjectDialog({
  open,
  editing,
  levels,
  pending,
  onOpenChange,
  onSubmit,
}: {
  open: boolean;
  editing: Subject | null;
  levels: EducationLevel[];
  pending: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (body: FormValues) => void;
}) {
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: {
      name: editing?.name ?? "",
      code: editing?.code ?? "",
      educationLevelId: editing?.educationLevelId ?? "",
      isActive: editing?.isActive ?? true,
    },
  });
  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={editing ? t("academic.editSubject") : t("academic.newSubject")}>
      <form className="grid gap-3" onSubmit={form.handleSubmit(onSubmit)}>
        <FormField label={t("staff.name")} htmlFor="subject-name" error={fieldError(form, "name")}>
          <Input id="subject-name" {...form.register("name")} />
        </FormField>
        <FormField label={t("academic.code")} htmlFor="subject-code" error={fieldError(form, "code")}>
          <Input id="subject-code" {...form.register("code")} />
        </FormField>
        <FormField label={t("academic.level")} htmlFor="subject-level">
          <select id="subject-level" className={fieldClass} {...form.register("educationLevelId")}>
            <option value="">{t("common.none")}</option>
            {levels.map((level) => <option key={level.id} value={level.id}>{level.name}</option>)}
          </select>
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
