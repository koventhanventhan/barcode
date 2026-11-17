import { useMutation } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { PageHeader } from "@/components/page-header";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/features/auth/AuthProvider";
import { api, ApiError } from "@/lib/api";
import { downloadApi } from "@/lib/download";

export type ImportReport = {
  imported: number;
  failed: number;
  errors: { rowNumber: number; field: string; message: string }[];
};

export function ImportReportView({ report }: { report: ImportReport }) {
  const { t } = useTranslation();
  return (
    <div className="grid gap-3">
      <p className="text-sm">{t("students.imported", { count: report.imported })}</p>
      {report.errors.length === 0 ? <p className="text-sm text-muted">{t("students.noErrors")}</p> : (
        <>
          <p className="text-sm">{t("students.failed", { count: report.failed })}</p>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="w-full text-left text-sm">
              <thead className="bg-secondary text-xs uppercase text-muted">
                <tr>
                  <th className="px-3 py-2">{t("students.row")}</th>
                  <th className="px-3 py-2">{t("students.field")}</th>
                  <th className="px-3 py-2">{t("students.problem")}</th>
                </tr>
              </thead>
              <tbody>
                {report.errors.map((error, index) => (
                  <tr key={`${error.rowNumber}-${error.field}-${index}`} className="border-t border-border">
                    <td className="px-3 py-2 tabular-nums">{error.rowNumber}</td>
                    <td className="px-3 py-2">{error.field}</td>
                    <td className="px-3 py-2">{error.message}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}

export function ImportStudentsPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { hasPermission } = useAuth();
  const [file, setFile] = useState<File | null>(null);
  const [report, setReport] = useState<ImportReport | null>(null);
  const upload = useMutation({
    mutationFn: async () => {
      if (!file) throw new ApiError(400, t("students.chooseFile"));
      const body = new FormData();
      body.append("file", file);
      return api<ImportReport>("/api/students/import", { method: "POST", body });
    },
    onSuccess: (data) => setReport(data),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  if (!hasPermission("students.edit")) return <p className="text-sm">{t("auth.deniedBody")}</p>;

  return (
    <div className="grid max-w-3xl gap-4">
      <PageHeader
        title={t("students.importTitle")}
        description={t("students.importDescription")}
        crumbs={[{ label: t("nav.students"), href: "/app/students" }, { label: t("students.import") }]}
      />
      <div className="flex flex-wrap gap-2">
        <Button type="button" variant="outline" onClick={() => void downloadApi("/api/students/import-template?format=csv", "student-import.csv")}>{t("students.templateCsv")}</Button>
        <Button type="button" variant="outline" onClick={() => void downloadApi("/api/students/import-template?format=xlsx", "student-import.xlsx")}>{t("students.templateExcel")}</Button>
      </div>
      <label className="grid gap-1 text-[13px] font-medium">
        {t("students.chooseFile")}
        <input
          type="file"
          accept=".csv,.xlsx,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
          onChange={(event) => setFile(event.target.files?.[0] ?? null)}
        />
      </label>
      <div className="flex gap-2">
        <Button type="button" disabled={!file || upload.isPending} onClick={() => upload.mutate()}>{t("students.upload")}</Button>
        <Button type="button" variant="outline" asChild><Link to="/app/students">{t("common.back")}</Link></Button>
      </div>
      {report ? <ImportReportView report={report} /> : null}
    </div>
  );
}
