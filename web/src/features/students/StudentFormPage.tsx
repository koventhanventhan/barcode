import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useParams } from "react-router-dom";
import { z } from "zod";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { FeatureGate } from "@/components/feature-gate";
import { fieldError, FormField } from "@/components/form-field";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { useAuth } from "@/features/auth/AuthProvider";
import { fieldClass, type Batch, type EducationLevel } from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";
import { downloadApi } from "@/lib/download";

const schema = z.object({
  fullName: z.string().trim().min(1).max(200),
  guardianName: z.string().trim().min(1).max(200),
  guardianPhone: z.string().trim().min(9).max(20),
  guardianPhoneSecondary: z.string().trim().max(20),
  address: z.string().trim().min(1).max(300),
  city: z.string().trim().max(80),
  educationLevelId: z.string().uuid(),
  gender: z.enum(["Female", "Male", "Other", "Unspecified"]),
  status: z.enum(["Active", "Inactive", "Left"]),
  dateOfBirth: z.string(),
  admissionDate: z.string(),
  notes: z.string().max(2000),
  batchIds: z.array(z.string()),
});

type FormValues = z.infer<typeof schema>;

type StudentDetail = FormValues & {
  id: string;
  studentCode: string;
  photoUrl: string | null;
  educationLevelName: string;
  enrollments: { batchId: string }[];
};

export function StudentFormPage() {
  const { id } = useParams();
  const isNew = !id;
  const { t } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { hasPermission, isReadOnly } = useAuth();
  const [confirmDelete, setConfirmDelete] = useState(false);
  const student = useQuery({
    queryKey: ["student", id],
    enabled: Boolean(id),
    queryFn: () => api<StudentDetail>(`/api/students/${id}`),
  });
  const levels = useQuery({ queryKey: ["levels"], queryFn: () => api<EducationLevel[]>("/api/education-levels") });
  const batches = useQuery({ queryKey: ["batches"], queryFn: () => api<Batch[]>("/api/batches") });
  const enabled = (levels.data ?? []).filter((level) => level.isEnabled);
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    values: {
      fullName: student.data?.fullName ?? "",
      guardianName: student.data?.guardianName ?? "",
      guardianPhone: student.data?.guardianPhone ?? "",
      guardianPhoneSecondary: student.data?.guardianPhoneSecondary ?? "",
      address: student.data?.address ?? "",
      city: student.data?.city ?? "",
      educationLevelId: student.data?.educationLevelId ?? enabled[0]?.id ?? "",
      gender: student.data?.gender ?? "Unspecified",
      status: student.data?.status ?? "Active",
      dateOfBirth: student.data?.dateOfBirth ?? "",
      admissionDate: student.data?.admissionDate ?? "",
      notes: student.data?.notes ?? "",
      batchIds: student.data?.enrollments?.map((item) => item.batchId) ?? [],
    },
  });
  const levelId = form.watch("educationLevelId");
  const history = useQuery({
    queryKey: ["student-attendance", id],
    enabled: Boolean(id),
    queryFn: () => api<{ sessionDate: string; className: string; status: string }[]>(`/api/attendance/students/${id}`),
  });
  const save = useMutation({
    mutationFn: async (values: FormValues) => {
      const payload = {
        ...values,
        guardianPhoneSecondary: values.guardianPhoneSecondary || null,
        city: values.city || null,
        dateOfBirth: values.dateOfBirth || null,
        admissionDate: values.admissionDate || null,
        notes: values.notes || null,
      };
      const saved = await api<StudentDetail>(isNew ? "/api/students" : `/api/students/${id}`, {
        method: isNew ? "POST" : "PUT",
        body: JSON.stringify(payload),
      });
      const file = (document.getElementById("student-photo") as HTMLInputElement | null)?.files?.[0];
      if (file) {
        const body = new FormData();
        body.append("file", file);
        return api<StudentDetail>(`/api/students/${saved.id}/photo`, { method: "POST", body });
      }
      return saved;
    },
    onSuccess: (saved) => {
      toast.success(t("students.saved"));
      void queryClient.invalidateQueries({ queryKey: ["students"] });
      void queryClient.invalidateQueries({ queryKey: ["student", saved.id] });
      if (isNew) navigate(`/app/students/${saved.id}`);
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });
  const remove = useMutation({
    mutationFn: () => api(`/api/students/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("students.deleted"));
      navigate("/app/students");
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  if (!hasPermission(isNew ? "students.edit" : "students.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (!isNew && student.isLoading) return <PageSkeleton />;
  const canEdit = hasPermission("students.edit") && !isReadOnly;
  const levelBatches = (batches.data ?? []).filter((batch) => batch.educationLevelId === levelId && batch.isActive);

  return (
    <div>
      <PageHeader
        title={isNew ? t("students.createTitle") : student.data?.fullName ?? t("students.edit")}
        description={isNew ? t("students.createDescription") : student.data?.studentCode}
        crumbs={[{ label: t("nav.students"), href: "/app/students" }, { label: isNew ? t("students.new") : student.data?.studentCode ?? t("students.edit") }]}
        actions={!isNew && student.data ? (
          <FeatureGate feature="BarcodeGenerator" fallback={null}>
            <Button type="button" variant="outline" onClick={() => void downloadApi(`/api/students/${student.data.id}/id-card`, `${student.data.studentCode}-id-card.pdf`).catch((error: unknown) => toast.error(error instanceof Error ? error.message : t("errors.generic")))}>
              {t("students.printCard")}
            </Button>
          </FeatureGate>
        ) : null}
      />
      <form className="grid max-w-3xl gap-3 sm:grid-cols-2" onSubmit={form.handleSubmit((values) => save.mutate(values))}>
        <FormField label={t("students.name")} htmlFor="fullName" error={fieldError(form, "fullName")}>
          <Input id="fullName" disabled={!canEdit} {...form.register("fullName")} />
        </FormField>
        <FormField label={t("students.guardian")} htmlFor="guardianName" error={fieldError(form, "guardianName")}>
          <Input id="guardianName" disabled={!canEdit} {...form.register("guardianName")} />
        </FormField>
        <FormField label={t("students.phone")} htmlFor="guardianPhone" error={fieldError(form, "guardianPhone")}>
          <Input id="guardianPhone" disabled={!canEdit} {...form.register("guardianPhone")} />
        </FormField>
        <FormField label={t("students.phone2")} htmlFor="guardianPhoneSecondary">
          <Input id="guardianPhoneSecondary" disabled={!canEdit} {...form.register("guardianPhoneSecondary")} />
        </FormField>
        <FormField label={t("students.address")} htmlFor="address" error={fieldError(form, "address")}>
          <Input id="address" disabled={!canEdit} {...form.register("address")} />
        </FormField>
        <FormField label={t("students.city")} htmlFor="city">
          <Input id="city" disabled={!canEdit} {...form.register("city")} />
        </FormField>
        <FormField label={t("students.level")} htmlFor="educationLevelId" error={fieldError(form, "educationLevelId")}>
          <select id="educationLevelId" className={fieldClass} disabled={!canEdit} {...form.register("educationLevelId")}>
            {enabled.map((level) => <option key={level.id} value={level.id}>{level.name}</option>)}
          </select>
        </FormField>
        <FormField label={t("students.gender")} htmlFor="gender">
          <select id="gender" className={fieldClass} disabled={!canEdit} {...form.register("gender")}>
            <option value="Unspecified">{t("students.unspecified")}</option>
            <option value="Female">{t("students.female")}</option>
            <option value="Male">{t("students.male")}</option>
            <option value="Other">{t("students.other")}</option>
          </select>
        </FormField>
        <FormField label={t("students.status")} htmlFor="status">
          <select id="status" className={fieldClass} disabled={!canEdit} {...form.register("status")}>
            <option value="Active">{t("students.active")}</option>
            <option value="Inactive">{t("students.inactive")}</option>
            <option value="Left">{t("students.left")}</option>
          </select>
        </FormField>
        <FormField label={t("students.dob")} htmlFor="dateOfBirth">
          <Input id="dateOfBirth" type="date" disabled={!canEdit} {...form.register("dateOfBirth")} />
        </FormField>
        <FormField label={t("students.admitted")} htmlFor="admissionDate">
          <Input id="admissionDate" type="date" disabled={!canEdit} {...form.register("admissionDate")} />
        </FormField>
        <div className="sm:col-span-2">
          <p className="mb-1 text-[13px] font-medium">{t("students.batches")}</p>
          <div className="grid gap-1">
            {levelBatches.length === 0 ? <p className="text-[13px] text-muted">{t("academic.emptyBatches")}</p> : levelBatches.map((batch) => (
              <label key={batch.id} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  value={batch.id}
                  disabled={!canEdit}
                  checked={form.watch("batchIds").includes(batch.id)}
                  onChange={(event) => {
                    const current = form.getValues("batchIds");
                    form.setValue("batchIds", event.target.checked ? [...current, batch.id] : current.filter((item) => item !== batch.id));
                  }}
                />
                {batch.name}
              </label>
            ))}
          </div>
        </div>
        <FormField label={t("students.notes")} htmlFor="notes">
          <Textarea id="notes" disabled={!canEdit} {...form.register("notes")} />
        </FormField>
        {canEdit ? (
          <FormField label={t("students.photo")} htmlFor="student-photo">
            <Input id="student-photo" type="file" accept="image/jpeg,image/png,image/webp" />
          </FormField>
        ) : null}
        {student.data?.photoUrl ? <img src={student.data.photoUrl} alt="" className="h-24 w-24 rounded-md border border-border object-cover" /> : null}
        <div className="flex flex-wrap gap-2 sm:col-span-2">
          {canEdit ? <Button type="submit" disabled={save.isPending}>{t("common.save")}</Button> : null}
          <Button type="button" variant="outline" asChild><Link to="/app/students">{t("common.back")}</Link></Button>
          {!isNew && canEdit ? <Button type="button" variant="danger" onClick={() => setConfirmDelete(true)}>{t("common.delete")}</Button> : null}
        </div>
      </form>
      {!isNew && (history.data?.length ?? 0) > 0 ? (
        <section className="mt-6 max-w-3xl">
          <h2 className="mb-2 text-sm font-semibold">{t("attendance.history")}</h2>
          <ul className="divide-y divide-border rounded-lg border border-border">
            {history.data?.slice(0, 8).map((item) => (
              <li key={`${item.sessionDate}-${item.className}`} className="flex items-center justify-between px-3 py-2 text-sm">
                <span>{item.className}</span>
                <span className="tabular-nums text-muted">{item.sessionDate} · {item.status}</span>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
      <ConfirmDialog
        open={confirmDelete}
        title={t("students.deleteTitle")}
        description={t("students.deleteBody")}
        confirmLabel={t("common.delete")}
        destructive
        pending={remove.isPending}
        onOpenChange={setConfirmDelete}
        onConfirm={() => remove.mutate()}
      />
    </div>
  );
}
