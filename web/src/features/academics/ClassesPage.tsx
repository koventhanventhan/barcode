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
import {
  AcademicNav,
  WEEKDAYS,
  fieldClass,
  minutesToTime,
  timeToMinutes,
  type Batch,
  type ClassSchedule,
  type Subject,
  type TeacherOption,
} from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";

const schema = z.object({
  name: z.string().trim().max(120).optional(),
  batchId: z.string().uuid(),
  subjectId: z.string().uuid(),
  teacherUserId: z.string().uuid(),
  weekday: z.coerce.number().int().min(1).max(7),
  starts: z.string().regex(/^\d{2}:\d{2}/),
  ends: z.string().regex(/^\d{2}:\d{2}/),
  room: z.string().trim().max(50).optional(),
  isActive: z.boolean(),
});

type FormValues = z.infer<typeof schema>;

export function ClassesPage() {
  const toast = useToast();
  const { hasPermission, isReadOnly } = useAuth();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<ClassSchedule | null>(null);
  const [open, setOpen] = useState(false);
  const [removeId, setRemoveId] = useState<string | null>(null);
  const canManage = hasPermission("academics.manage") && !isReadOnly;
  const classes = useQuery({ queryKey: ["classes"], queryFn: () => api<ClassSchedule[]>("/api/class-schedules") });
  const batches = useQuery({ queryKey: ["batches"], queryFn: () => api<Batch[]>("/api/batches") });
  const subjects = useQuery({ queryKey: ["subjects"], queryFn: () => api<Subject[]>("/api/subjects") });
  const teachers = useQuery({ queryKey: ["teachers"], queryFn: () => api<TeacherOption[]>("/api/class-schedules/teachers") });
  const save = useMutation({
    mutationFn: (input: { id?: string; body: FormValues }) =>
      api(input.id ? `/api/class-schedules/${input.id}` : "/api/class-schedules", {
        method: input.id ? "PUT" : "POST",
        body: JSON.stringify({
          name: input.body.name || null,
          batchId: input.body.batchId,
          subjectId: input.body.subjectId,
          teacherUserId: input.body.teacherUserId,
          weekday: Number(input.body.weekday),
          startMinutes: timeToMinutes(input.body.starts),
          endMinutes: timeToMinutes(input.body.ends),
          room: input.body.room || null,
          isActive: input.body.isActive,
        }),
      }),
    onSuccess: () => {
      toast.success(t("academic.saved"));
      setOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["classes"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });
  const remove = useMutation({
    mutationFn: (id: string) => api(`/api/class-schedules/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("academic.deleted"));
      setRemoveId(null);
      void queryClient.invalidateQueries({ queryKey: ["classes"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  if (!hasPermission("academics.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (classes.isLoading) return <PageSkeleton />;
  const weekday = (value: number) => t(`academic.${["", "mon", "tue", "wed", "thu", "fri", "sat", "sun"][value]}`);
  const columns: DataColumn<ClassSchedule>[] = [
    { id: "name", header: t("staff.name"), cell: (row) => row.name, exportValue: (row) => row.name, sortValue: (row) => row.name },
    { id: "when", header: t("academic.weekday"), cell: (row) => `${weekday(row.weekday)} ${minutesToTime(row.startMinutes)}–${minutesToTime(row.endMinutes)}`, exportValue: (row) => `${row.weekday} ${minutesToTime(row.startMinutes)}` },
    { id: "room", header: t("academic.room"), cell: (row) => row.room ?? t("common.none"), exportValue: (row) => row.room ?? "" },
    { id: "teacher", header: t("academic.teacher"), cell: (row) => row.teacherName, exportValue: (row) => row.teacherName },
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
        title={t("academic.classesTitle")}
        description={t("academic.classesDescription")}
        crumbs={[{ label: t("nav.academic"), href: "/app/academics/levels" }, { label: t("nav.classes") }]}
        actions={canManage ? <Button type="button" onClick={() => { setEditing(null); setOpen(true); }}>{t("academic.newClass")}</Button> : null}
      />
      <AcademicNav />
      {classes.data && classes.data.length > 0 ? (
        <DataTable data={classes.data} columns={columns} filename="classes.csv" getRowId={(row) => row.id} enableExport={false} />
      ) : (
        <EmptyState title={t("academic.emptyClasses")} />
      )}
      <ClassDialog
        open={open}
        editing={editing}
        batches={batches.data ?? []}
        subjects={subjects.data ?? []}
        teachers={teachers.data ?? []}
        pending={save.isPending}
        onOpenChange={setOpen}
        onSubmit={(body) => save.mutate({ id: editing?.id, body })}
      />
      <ConfirmDialog
        open={Boolean(removeId)}
        title={t("academic.deleteClass")}
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

function ClassDialog({
  open,
  editing,
  batches,
  subjects,
  teachers,
  pending,
  onOpenChange,
  onSubmit,
}: {
  open: boolean;
  editing: ClassSchedule | null;
  batches: Batch[];
  subjects: Subject[];
  teachers: TeacherOption[];
  pending: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (body: FormValues) => void;
}) {
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: {
      name: editing?.name ?? "",
      batchId: editing?.batchId ?? batches[0]?.id ?? "",
      subjectId: editing?.subjectId ?? subjects[0]?.id ?? "",
      teacherUserId: editing?.teacherUserId ?? teachers[0]?.id ?? "",
      weekday: editing?.weekday ?? 1,
      starts: minutesToTime(editing?.startMinutes ?? 16 * 60),
      ends: minutesToTime(editing?.endMinutes ?? 18 * 60),
      room: editing?.room ?? "",
      isActive: editing?.isActive ?? true,
    },
  });
  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={editing ? t("academic.editClass") : t("academic.newClass")}>
      <form className="grid gap-3 sm:grid-cols-2" onSubmit={form.handleSubmit(onSubmit)}>
        <FormField label={t("nav.batches")} htmlFor="class-batch" error={fieldError(form, "batchId")}>
          <select id="class-batch" className={fieldClass} {...form.register("batchId")}>
            {batches.map((batch) => <option key={batch.id} value={batch.id}>{batch.name}</option>)}
          </select>
        </FormField>
        <FormField label={t("academic.subject")} htmlFor="class-subject" error={fieldError(form, "subjectId")}>
          <select id="class-subject" className={fieldClass} {...form.register("subjectId")}>
            {subjects.map((subject) => <option key={subject.id} value={subject.id}>{subject.name}</option>)}
          </select>
        </FormField>
        <FormField label={t("academic.teacher")} htmlFor="class-teacher" error={fieldError(form, "teacherUserId")}>
          <select id="class-teacher" className={fieldClass} {...form.register("teacherUserId")}>
            {teachers.map((teacher) => <option key={teacher.id} value={teacher.id}>{teacher.fullName}</option>)}
          </select>
        </FormField>
        <FormField label={t("academic.weekday")} htmlFor="class-day">
          <select id="class-day" className={fieldClass} {...form.register("weekday")}>
            {WEEKDAYS.map((day) => <option key={day} value={day}>{t(`academic.${["", "mon", "tue", "wed", "thu", "fri", "sat", "sun"][day]}`)}</option>)}
          </select>
        </FormField>
        <FormField label={t("academic.starts")} htmlFor="class-start" error={fieldError(form, "starts")}>
          <Input id="class-start" type="time" {...form.register("starts")} />
        </FormField>
        <FormField label={t("academic.ends")} htmlFor="class-end" error={fieldError(form, "ends")}>
          <Input id="class-end" type="time" {...form.register("ends")} />
        </FormField>
        <FormField label={t("academic.room")} htmlFor="class-room">
          <Input id="class-room" {...form.register("room")} />
        </FormField>
        <FormField label={t("staff.name")} htmlFor="class-name">
          <Input id="class-name" {...form.register("name")} />
        </FormField>
        <label className="flex items-center gap-2 text-sm sm:col-span-2">
          <input type="checkbox" {...form.register("isActive")} />
          {t("common.active")}
        </label>
        <div className="flex justify-end gap-2 sm:col-span-2">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>{t("common.cancel")}</Button>
          <Button type="submit" disabled={pending}>{t("common.save")}</Button>
        </div>
      </form>
    </Dialog>
  );
}
