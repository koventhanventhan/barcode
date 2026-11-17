import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { BranchSwitcher } from "@/components/layout/BranchSwitcher";
import { FeatureGate } from "@/components/feature-gate";
import { DataTable, type DataColumn } from "@/components/data-table/DataTable";
import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { PermissionGate } from "@/components/permission-gate";
import { useAuth } from "@/features/auth/AuthProvider";
import { fieldClass, type Batch, type EducationLevel } from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";
import { downloadApi } from "@/lib/download";

export type StudentListItem = {
  id: string;
  studentCode: string;
  fullName: string;
  guardianName: string;
  guardianPhone: string;
  educationLevelId: string;
  educationLevelName: string;
  status: string;
  batchNames: string;
  photoUrl: string | null;
  branchId?: string | null;
  branchName?: string | null;
};

type StudentPage = { items: StudentListItem[]; total: number; page: number; pageSize: number };

export function StudentsPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { hasPermission } = useAuth();
  const [search, setSearch] = useState("");
  const [applied, setApplied] = useState("");
  const [status, setStatus] = useState("");
  const [levelId, setLevelId] = useState("");
  const [batchId, setBatchId] = useState("");
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string[]>([]);
  const levels = useQuery({ queryKey: ["levels"], queryFn: () => api<EducationLevel[]>("/api/education-levels") });
  const batches = useQuery({ queryKey: ["batches"], queryFn: () => api<Batch[]>("/api/batches") });
  const students = useQuery({
    queryKey: ["students", applied, status, levelId, batchId, page],
    queryFn: () => {
      const params = new URLSearchParams({ page: String(page), pageSize: "50" });
      if (applied) params.set("search", applied);
      if (status) params.set("status", status);
      if (levelId) params.set("educationLevelId", levelId);
      if (batchId) params.set("batchId", batchId);
      return api<StudentPage>(`/api/students?${params.toString()}`);
    },
  });

  async function download(path: string, filename: string, init?: RequestInit) {
    try {
      await downloadApi(path, filename, init);
    } catch (error) {
      toast.error(error instanceof ApiError ? error.message : t("errors.generic"));
    }
  }

  if (!hasPermission("students.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (students.isLoading) return <PageSkeleton />;

  const rows = students.data?.items ?? [];
  const toggle = (id: string) => setSelected((current) => current.includes(id) ? current.filter((item) => item !== id) : [...current, id]);
  const columns: DataColumn<StudentListItem>[] = [
    {
      id: "pick",
      header: "",
      enableHiding: false,
      cell: (row) => <input type="checkbox" checked={selected.includes(row.id)} onChange={() => toggle(row.id)} aria-label={row.fullName} />,
    },
    { id: "code", header: t("students.code"), cell: (row) => <span className="tabular-nums">{row.studentCode}</span>, exportValue: (row) => row.studentCode, sortValue: (row) => row.studentCode },
    { id: "name", header: t("students.name"), cell: (row) => <Link className="font-medium text-primary" to={`/app/students/${row.id}`}>{row.fullName}</Link>, exportValue: (row) => row.fullName, sortValue: (row) => row.fullName },
    { id: "guardian", header: t("students.guardian"), cell: (row) => row.guardianName, exportValue: (row) => row.guardianName },
    { id: "phone", header: t("students.phone"), cell: (row) => <span className="tabular-nums">{row.guardianPhone}</span>, exportValue: (row) => row.guardianPhone },
    { id: "level", header: t("students.level"), cell: (row) => row.educationLevelName, exportValue: (row) => row.educationLevelName },
    { id: "branch", header: t("students.branch"), cell: (row) => row.branchName || t("common.none"), exportValue: (row) => row.branchName ?? "" },
    { id: "batches", header: t("students.batches"), cell: (row) => row.batchNames || t("common.none"), exportValue: (row) => row.batchNames },
    { id: "status", header: t("students.status"), cell: (row) => t(`students.${row.status.toLowerCase()}`, { defaultValue: row.status }), exportValue: (row) => row.status },
  ];

  return (
    <div>
      <BranchSwitcher />
      <PageHeader
        title={t("students.title")}
        description={t("students.description")}
        crumbs={[{ label: t("nav.students") }]}
        actions={
          <PermissionGate permission="students.edit">
            <>
              <Button asChild variant="outline"><Link to="/app/students/import">{t("students.import")}</Link></Button>
              <Button asChild><Link to="/app/students/new">{t("students.new")}</Link></Button>
            </>
          </PermissionGate>
        }
      />
      <form
        className="mb-3 grid gap-2 sm:grid-cols-4"
        onSubmit={(event) => {
          event.preventDefault();
          setPage(1);
          setApplied(search.trim());
        }}
      >
        <Input value={search} onChange={(event) => setSearch(event.target.value)} placeholder={t("students.search")} aria-label={t("students.search")} />
        <select className={fieldClass} value={status} aria-label={t("students.status")} onChange={(event) => { setStatus(event.target.value); setPage(1); }}>
          <option value="">{t("students.allStatuses")}</option>
          <option value="Active">{t("students.active")}</option>
          <option value="Inactive">{t("students.inactive")}</option>
          <option value="Left">{t("students.left")}</option>
        </select>
        <select className={fieldClass} value={levelId} aria-label={t("students.level")} onChange={(event) => { setLevelId(event.target.value); setPage(1); }}>
          <option value="">{t("students.allLevels")}</option>
          {(levels.data ?? []).filter((level) => level.isEnabled).map((level) => <option key={level.id} value={level.id}>{level.name}</option>)}
        </select>
        <select className={fieldClass} value={batchId} aria-label={t("students.batches")} onChange={(event) => { setBatchId(event.target.value); setPage(1); }}>
          <option value="">{t("students.allBatches")}</option>
          {(batches.data ?? []).map((batch) => <option key={batch.id} value={batch.id}>{batch.name}</option>)}
        </select>
      </form>
      <div className="mb-3 flex flex-wrap gap-2">
        <FeatureGate feature="DataExport" fallback={null}>
          <>
            <Button type="button" variant="outline" size="sm" onClick={() => void download("/api/students/export?format=csv", "students.csv")}>{t("students.exportCsv")}</Button>
            <Button type="button" variant="outline" size="sm" onClick={() => void download("/api/students/export?format=xlsx", "students.xlsx")}>{t("students.exportExcel")}</Button>
            <Button type="button" variant="outline" size="sm" onClick={() => void download("/api/students/export?format=pdf", "students.pdf")}>{t("students.exportPdf")}</Button>
          </>
        </FeatureGate>
        <FeatureGate feature="BarcodeGenerator" fallback={null}>
          <>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={selected.length === 0}
              onClick={() => void download("/api/students/id-cards", "id-cards.pdf", { method: "POST", body: JSON.stringify({ studentIds: selected, all: false }) })}
            >
              {t("students.printCards")}
            </Button>
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={selected.length === 0}
              onClick={() => void download("/api/students/labels", "barcode-labels.pdf", { method: "POST", body: JSON.stringify({ studentIds: selected, all: false }) })}
            >
              {t("students.labels")}
            </Button>
          </>
        </FeatureGate>
      </div>
      {rows.length === 0 ? <EmptyState title={t("students.empty")} /> : (
        <>
          <div className="grid gap-2 md:hidden">
            {rows.map((row) => (
              <article key={row.id} className="rounded-lg border border-border bg-card p-3">
                <div className="flex items-start justify-between gap-2">
                  <div>
                    <Link className="font-medium text-primary" to={`/app/students/${row.id}`}>{row.fullName}</Link>
                    <p className="text-[13px] tabular-nums text-muted">{row.studentCode}</p>
                  </div>
                  <input type="checkbox" checked={selected.includes(row.id)} onChange={() => toggle(row.id)} aria-label={row.fullName} />
                </div>
                <p className="mt-2 text-[13px]">{row.guardianName} · <span className="tabular-nums">{row.guardianPhone}</span></p>
                <p className="text-[13px] text-muted">{row.educationLevelName}{row.batchNames ? ` · ${row.batchNames}` : ""}</p>
              </article>
            ))}
          </div>
          <div className="hidden md:block">
            <DataTable data={rows} columns={columns} filename="students.csv" getRowId={(row) => row.id} pageSize={50} enableExport={false} />
          </div>
          <div className="mt-3 flex items-center justify-between text-[13px] text-muted">
            <p className="tabular-nums">{t("table.rows", { count: students.data?.total ?? 0 })}</p>
            <div className="flex gap-2">
              <Button type="button" variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage((value) => value - 1)}>{t("table.previous")}</Button>
              <Button type="button" variant="outline" size="sm" disabled={page * 50 >= (students.data?.total ?? 0)} onClick={() => setPage((value) => value + 1)}>{t("table.next")}</Button>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
