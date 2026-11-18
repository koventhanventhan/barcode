import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { t } from "@/config/strings";
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { BranchSwitcher } from "@/components/layout/BranchSwitcher";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import { colomboToday, type AttendanceReport } from "./api";

export function ReportsPage() {
  const { hasPermission } = useAuth();
  const today = colomboToday();
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);
  const report = useQuery({
    queryKey: ["attendance-report", from, to],
    enabled: hasPermission("reports.view"),
    queryFn: () => api<AttendanceReport>(`/api/attendance/reports?from=${from}&to=${to}`),
  });

  if (!hasPermission("reports.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (report.isLoading) return <PageSkeleton />;
  const data = report.data;
  return (
    <div className="grid gap-4">
      <BranchSwitcher />
      <PageHeader
        title={t("attendance.reportsTitle")}
        description={t("attendance.reportsDescription")}
        crumbs={[{ label: t("nav.attendance"), href: "/app/attendance" }, { label: t("nav.attendanceReports") }]}
      />
      <div className="grid gap-2 sm:grid-cols-2">
        <Input type="date" aria-label={t("attendance.from")} value={from} onChange={(event) => setFrom(event.target.value)} />
        <Input type="date" aria-label={t("attendance.to")} value={to} onChange={(event) => setTo(event.target.value)} />
      </div>
      <div className="grid gap-3 sm:grid-cols-3">
        <StatCard label={t("attendance.presentRate")} value={`${data?.presentRate ?? 0}%`} />
        <StatCard label={t("attendance.present")} value={String((data?.present ?? 0) + (data?.late ?? 0))} />
        <StatCard label={t("attendance.absent")} value={String(data?.absent ?? 0)} />
      </div>
      <div className="h-64 rounded-lg border border-border bg-card p-3">
        <ResponsiveContainer width="100%" height="100%">
          <BarChart data={data?.byDay ?? []}>
            <CartesianGrid stroke="var(--border)" vertical={false} />
            <XAxis dataKey="date" tick={{ fontSize: 12 }} />
            <YAxis allowDecimals={false} tick={{ fontSize: 12 }} />
            <Tooltip />
            <Bar dataKey="present" stackId="a" fill="var(--success)" name={t("attendance.present")} />
            <Bar dataKey="late" stackId="a" fill="var(--warning)" name={t("attendance.late")} />
            <Bar dataKey="absent" stackId="a" fill="var(--danger)" name={t("attendance.absent")} />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}
