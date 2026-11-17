import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/features/auth/AuthProvider";
import { fieldClass, type Batch } from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";
import { colomboToday, type RegisterSession } from "./api";

export function RegisterPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { hasPermission, isReadOnly } = useAuth();
  const today = colomboToday();
  const [date, setDate] = useState(today);
  const [batchId, setBatchId] = useState("");
  const batches = useQuery({ queryKey: ["batches"], queryFn: () => api<Batch[]>("/api/batches") });
  const register = useQuery({
    queryKey: ["attendance-register", date, batchId],
    queryFn: () => api<RegisterSession[]>(`/api/attendance/register?date=${date}${batchId ? `&batchId=${batchId}` : ""}`),
  });
  const save = useMutation({
    mutationFn: (input: { sessionId: string; studentId: string; status: string; reason: string }) =>
      api(`/api/attendance/sessions/${input.sessionId}/marks`, {
        method: "POST",
        body: JSON.stringify({ studentId: input.studentId, status: input.status, reason: input.reason || null }),
      }),
    onSuccess: () => {
      toast.success(t("attendance.saved"));
      void queryClient.invalidateQueries({ queryKey: ["attendance-register"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  if (!hasPermission("attendance.mark")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  return (
    <div className="grid gap-4">
      <PageHeader
        title={t("attendance.registerTitle")}
        description={t("attendance.registerDescription")}
        crumbs={[{ label: t("nav.attendance"), href: "/app/attendance" }, { label: t("nav.register") }]}
        actions={<Button asChild variant="outline"><Link to="/app/attendance/reports">{t("nav.attendanceReports")}</Link></Button>}
      />
      <div className="grid gap-2 sm:grid-cols-2">
        <Input type="date" aria-label={t("attendance.date")} value={date} onChange={(event) => setDate(event.target.value)} />
        <select className={fieldClass} aria-label={t("students.batches")} value={batchId} onChange={(event) => setBatchId(event.target.value)}>
          <option value="">{t("students.allBatches")}</option>
          {(batches.data ?? []).map((batch) => <option key={batch.id} value={batch.id}>{batch.name}</option>)}
        </select>
      </div>
      {register.isLoading ? <PageSkeleton /> : (register.data ?? []).length === 0 ? <p className="text-sm text-muted">{t("attendance.noSessions")}</p> : (
        (register.data ?? []).map((item) => (
          <section key={item.session.id} className="rounded-lg border border-border">
            <header className="flex items-center justify-between border-b border-border px-3 py-2">
              <h2 className="text-sm font-semibold">{item.session.className}</h2>
              <span className="text-[13px] text-muted">{t(`attendance.status.${item.session.status}`)}</span>
            </header>
            <ul className="divide-y divide-border">
              {item.students.map((student) => (
                <li key={student.studentId} className="grid gap-2 px-3 py-3 sm:grid-cols-[1fr_140px_1fr_auto] sm:items-center">
                  <div>
                    <Link className="font-medium text-primary" to={`/app/students/${student.studentId}`}>{student.fullName}</Link>
                    <p className="text-[13px] tabular-nums text-muted">{student.studentCode}</p>
                  </div>
                  <select
                    className={fieldClass}
                    aria-label={t("attendance.markStatus", { name: student.fullName })}
                    defaultValue={student.status ?? ""}
                    key={`${student.studentId}-${student.status}`}
                    id={`status-${student.studentId}`}
                  >
                    <option value="">{t("attendance.unmarked")}</option>
                    <option value="Present">{t("attendance.present")}</option>
                    <option value="Late">{t("attendance.late")}</option>
                    <option value="Absent">{t("attendance.absent")}</option>
                  </select>
                  <Input id={`reason-${student.studentId}`} placeholder={t("attendance.reason")} defaultValue={student.reason ?? ""} />
                  <Button
                    type="button"
                    size="sm"
                    variant="outline"
                    disabled={isReadOnly || save.isPending || item.session.status === "Scheduled" || item.session.status === "Cancelled"}
                    onClick={() => {
                      const status = (document.getElementById(`status-${student.studentId}`) as HTMLSelectElement).value;
                      const reason = (document.getElementById(`reason-${student.studentId}`) as HTMLInputElement).value;
                      if (!status) {
                        if (!reason.trim()) {
                          toast.error(t("attendance.reasonRequired"));
                          return;
                        }
                        void api(`/api/attendance/sessions/${item.session.id}/marks/${student.studentId}/undo`, {
                          method: "POST",
                          body: JSON.stringify({ reason }),
                        }).then(() => queryClient.invalidateQueries({ queryKey: ["attendance-register"] })).catch((error: unknown) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")));
                        return;
                      }
                      save.mutate({ sessionId: item.session.id, studentId: student.studentId, status, reason });
                    }}
                  >
                    {t("common.save")}
                  </Button>
                </li>
              ))}
            </ul>
          </section>
        ))
      )}
    </div>
  );
}
