import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bell, CreditCard, Download, FileSpreadsheet, FileText, Lock, Plus, Receipt, RefreshCw, ShieldCheck } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { BranchSwitcher } from "@/components/layout/BranchSwitcher";
import { useFeature } from "@/components/feature-gate";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { type Batch, type EducationLevel } from "@/features/academics/shared";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import { downloadApi } from "@/lib/download";
import {
  type DiscountType,
  type FeeDashboardDto,
  type FeeFrequency,
  type FeeInvoiceDto,
  type FeeInvoicePageDto,
  type FeeStructureDto,
  formatMoney,
  type GenerateInvoicesResultDto,
  type PayHereCheckoutSessionDto,
  type PayHereGatewaySettingsDto,
  type PaymentMethod,
  type RunFeeRemindersResultDto,
  type StudentConcessionDto,
} from "./api";

type TabKey = "overview" | "invoices" | "structures" | "payhere";

export function FeesPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const navigate = useNavigate();
  const auth = useAuth();
  const queryClient = useQueryClient();

  const canCollect = auth.hasPermission("fees.collect");
  const canSettings = auth.hasPermission("settings.manage");
  const exportFeature = useFeature("DataExport");
  const onlinePayFeature = useFeature("OnlinePayments");

  const [tab, setTab] = useState<TabKey>("overview");
  const [periodFilter, setPeriodFilter] = useState("");
  const [batchFilter, setBatchFilter] = useState("");
  const [statusFilter, setStatusFilter] = useState("");
  const [searchFilter, setSearchFilter] = useState("");
  const [overdueOnly, setOverdueOnly] = useState(false);

  // Modals state
  const [generatePeriod, setGeneratePeriod] = useState(() => new Date().toISOString().slice(0, 7));
  const [singleOpen, setSingleOpen] = useState(false);
  const [singleStudentId, setSingleStudentId] = useState("");
  const [singleStructureId, setSingleStructureId] = useState("");
  const [singlePeriod, setSinglePeriod] = useState(() => new Date().toISOString().slice(0, 7));

  const [paymentInvoice, setPaymentInvoice] = useState<FeeInvoiceDto | null>(null);
  const [paymentAmount, setPaymentAmount] = useState("");
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>("Cash");
  const [paymentReference, setPaymentReference] = useState("");
  const [paymentNotes, setPaymentNotes] = useState("");
  const [paymentFile, setPaymentFile] = useState<File | null>(null);

  const [historyInvoice, setHistoryInvoice] = useState<FeeInvoiceDto | null>(null);
  const [refundPaymentId, setRefundPaymentId] = useState<string | null>(null);
  const [refundReason, setRefundReason] = useState("");
  const [refundAmount, setRefundAmount] = useState("");

  const [voidTarget, setVoidTarget] = useState<FeeInvoiceDto | null>(null);
  const [voidReason, setVoidReason] = useState("");

  // Structure form state
  const [structureName, setStructureName] = useState("");
  const [structureFrequency, setStructureFrequency] = useState<FeeFrequency>("Monthly");
  const [structureAmount, setStructureAmount] = useState("3500");
  const [structureLevelId, setStructureLevelId] = useState("");
  const [structureBatchId, setStructureBatchId] = useState("");
  const [structureDueDay, setStructureDueDay] = useState("10");

  // Concession form state
  const [concessionStudentId, setConcessionStudentId] = useState("");
  const [concessionStructureId, setConcessionStructureId] = useState("");
  const [concessionName, setConcessionName] = useState("");
  const [concessionType, setConcessionType] = useState<DiscountType>("Percentage");
  const [concessionValue, setConcessionValue] = useState("10");
  const [concessionReason, setConcessionReason] = useState("");

  // PayHere form state
  const [merchantId, setMerchantId] = useState("");
  const [merchantSecret, setMerchantSecret] = useState("");
  const [isSandbox, setIsSandbox] = useState(true);
  const [isGatewayEnabled, setIsGatewayEnabled] = useState(true);

  const dashboardQuery = useQuery({
    queryKey: ["fee-dashboard", periodFilter, batchFilter],
    queryFn: () => {
      const params = new URLSearchParams();
      if (periodFilter) params.set("billingPeriod", periodFilter);
      if (batchFilter) params.set("batchId", batchFilter);
      const qs = params.toString();
      return api<FeeDashboardDto>(`/api/fees/dashboard${qs ? `?${qs}` : ""}`);
    },
  });

  const invoicesQuery = useQuery({
    queryKey: ["fee-invoices", searchFilter, statusFilter, batchFilter, periodFilter, overdueOnly],
    queryFn: () => {
      const params = new URLSearchParams();
      if (searchFilter.trim()) params.set("search", searchFilter.trim());
      if (statusFilter) params.set("status", statusFilter);
      if (batchFilter) params.set("batchId", batchFilter);
      if (periodFilter) params.set("billingPeriod", periodFilter);
      if (overdueOnly) params.set("overdueOnly", "true");
      params.set("pageSize", "100");
      return api<FeeInvoicePageDto>(`/api/fees/invoices?${params.toString()}`);
    },
  });

  const structuresQuery = useQuery({
    queryKey: ["fee-structures"],
    queryFn: () => api<FeeStructureDto[]>("/api/fees/structures"),
  });

  const concessionsQuery = useQuery({
    queryKey: ["fee-concessions"],
    queryFn: () => api<StudentConcessionDto[]>("/api/fees/concessions"),
  });

  const batchesQuery = useQuery({
    queryKey: ["batches"],
    queryFn: () => api<Batch[]>("/api/batches"),
  });

  const levelsQuery = useQuery({
    queryKey: ["levels"],
    queryFn: () => api<EducationLevel[]>("/api/education-levels"),
  });

  const studentsQuery = useQuery({
    queryKey: ["students-Select"],
    queryFn: () =>
      api<{ items: Array<{ id: string; studentCode: string; fullName: string }> }>("/api/students?pageSize=100"),
  });

  const payHereSettingsQuery = useQuery({
    queryKey: ["payhere-settings"],
    enabled: canSettings && tab === "payhere",
    queryFn: async () => {
      const res = await api<PayHereGatewaySettingsDto>("/api/fees/payhere/settings");
      setMerchantId(res.merchantId);
      setIsSandbox(res.isSandbox);
      setIsGatewayEnabled(res.isEnabled);
      return res;
    },
  });

  async function refreshAll() {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ["fee-dashboard"] }),
      queryClient.invalidateQueries({ queryKey: ["fee-invoices"] }),
      queryClient.invalidateQueries({ queryKey: ["fee-structures"] }),
      queryClient.invalidateQueries({ queryKey: ["fee-concessions"] }),
    ]);
  }

  const generateMutation = useMutation({
    mutationFn: () =>
      api<GenerateInvoicesResultDto>("/api/fees/invoices/generate", {
        method: "POST",
        body: JSON.stringify({
          billingPeriod: generatePeriod || undefined,
          batchId: batchFilter || undefined,
        }),
      }),
    onSuccess: async (res) => {
      toast.success(
        t("fees.generatedToast", {
          created: res.createdCount,
          skipped: res.skippedExistingCount,
        }),
      );
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const remindersMutation = useMutation({
    mutationFn: () =>
      api<RunFeeRemindersResultDto>("/api/fees/reminders/run", {
        method: "POST",
        body: JSON.stringify({}),
      }),
    onSuccess: (res) => {
      const sent = res.beforeDueSent + res.onDueSent + res.overdueSent;
      toast.success(t("fees.remindersSentToast", { count: sent }));
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const createSingleInvoiceMutation = useMutation({
    mutationFn: () =>
      api<FeeInvoiceDto>("/api/fees/invoices", {
        method: "POST",
        body: JSON.stringify({
          studentId: singleStudentId,
          feeStructureId: singleStructureId,
          billingPeriod: singlePeriod || undefined,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("fees.invoiceCreated"));
      setSingleOpen(false);
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const recordPaymentMutation = useMutation({
    mutationFn: async () => {
      if (!paymentInvoice) throw new Error("No invoice selected");
      const created = await api<{ id: string; receiptNumber: string }>("/api/fees/payments", {
        method: "POST",
        body: JSON.stringify({
          invoiceId: paymentInvoice.id,
          amount: Number(paymentAmount),
          method: paymentMethod,
          referenceNumber: paymentReference.trim() || undefined,
          notes: paymentNotes.trim() || undefined,
        }),
      });
      if (paymentFile) {
        const fd = new FormData();
        fd.append("file", paymentFile);
        await api(`/api/fees/payments/${created.id}/attachment`, {
          method: "POST",
          body: fd,
        });
      }
      return created;
    },
    onSuccess: async (res) => {
      toast.success(t("fees.paymentRecordedToast", { receipt: res.receiptNumber }));
      setPaymentInvoice(null);
      setPaymentFile(null);
      setPaymentReference("");
      setPaymentNotes("");
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const refundOrVoidMutation = useMutation({
    mutationFn: () =>
      api(`/api/fees/payments/${refundPaymentId}/refund-or-void`, {
        method: "POST",
        body: JSON.stringify({
          reason: refundReason,
          refundAmount: refundAmount.trim() ? Number(refundAmount) : undefined,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("fees.paymentRefundedToast"));
      setRefundPaymentId(null);
      setRefundReason("");
      setRefundAmount("");
      setHistoryInvoice(null);
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const voidInvoiceMutation = useMutation({
    mutationFn: () =>
      api(`/api/fees/invoices/${voidTarget?.id}/void`, {
        method: "POST",
        body: JSON.stringify({ reason: voidReason }),
      }),
    onSuccess: async () => {
      toast.success(t("fees.invoiceVoidedToast"));
      setVoidTarget(null);
      setVoidReason("");
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const payOnlineMutation = useMutation({
    mutationFn: (invoiceId: string) =>
      api<PayHereCheckoutSessionDto>("/api/fees/payhere/checkout", {
        method: "POST",
        body: JSON.stringify({ invoiceId }),
      }),
    onSuccess: (session) => {
      navigate(`/app/fees/pay/${encodeURIComponent(session.orderId)}`);
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const createStructureMutation = useMutation({
    mutationFn: () =>
      api<FeeStructureDto>("/api/fees/structures", {
        method: "POST",
        body: JSON.stringify({
          name: structureName,
          frequency: structureFrequency,
          amount: Number(structureAmount),
          educationLevelId: structureLevelId || undefined,
          batchId: structureBatchId || undefined,
          dueDayOfMonth: Number(structureDueDay) || 10,
          isActive: true,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("fees.structureSaved"));
      setStructureName("");
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const deleteStructureMutation = useMutation({
    mutationFn: (id: string) => api(`/api/fees/structures/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      toast.success(t("academic.deleted"));
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const createConcessionMutation = useMutation({
    mutationFn: () =>
      api<StudentConcessionDto>("/api/fees/concessions", {
        method: "POST",
        body: JSON.stringify({
          studentId: concessionStudentId,
          feeStructureId: concessionStructureId || undefined,
          name: concessionName,
          discountType: concessionType,
          value: Number(concessionValue),
          reason: concessionReason.trim() || undefined,
          isActive: true,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("fees.concessionSaved"));
      setConcessionName("");
      setConcessionReason("");
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const deleteConcessionMutation = useMutation({
    mutationFn: (id: string) => api(`/api/fees/concessions/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      toast.success(t("academic.deleted"));
      await refreshAll();
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  const savePayHereMutation = useMutation({
    mutationFn: () =>
      api<PayHereGatewaySettingsDto>("/api/fees/payhere/settings", {
        method: "PUT",
        body: JSON.stringify({
          merchantId,
          merchantSecret: merchantSecret.trim() || undefined,
          isSandbox,
          isEnabled: isGatewayEnabled,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("fees.payhereSaved"));
      setMerchantSecret("");
      await queryClient.invalidateQueries({ queryKey: ["payhere-settings"] });
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  function exportReport(format: "csv" | "xlsx" | "pdf") {
    const params = new URLSearchParams();
    params.set("format", format);
    if (periodFilter) params.set("billingPeriod", periodFilter);
    if (batchFilter) params.set("batchId", batchFilter);
    if (statusFilter) params.set("status", statusFilter);
    void downloadApi(`/api/fees/export?${params.toString()}`, `fee-invoices.${format}`).catch((e) =>
      toast.error(e instanceof Error ? e.message : t("errors.generic")),
    );
  }

  if (dashboardQuery.isLoading && invoicesQuery.isLoading) return <PageSkeleton />;
  const dash = dashboardQuery.data;
  const invoices = invoicesQuery.data?.items ?? [];
  const batches = batchesQuery.data ?? [];
  const levels = (levelsQuery.data ?? []).filter((l) => l.isEnabled);
  const students = studentsQuery.data?.items ?? [];
  const structures = structuresQuery.data ?? [];
  const concessions = concessionsQuery.data ?? [];

  return (
    <div>
      <BranchSwitcher />
      <PageHeader
        title={t("fees.title")}
        description={t("fees.description")}
        crumbs={[{ label: t("nav.academic") }, { label: t("nav.fees") }]}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {canCollect ? (
              <>
                <div className="flex items-center gap-1">
                  <Input
                    type="month"
                    aria-label={t("fees.billingPeriod")}
                    value={generatePeriod}
                    onChange={(e) => setGeneratePeriod(e.target.value)}
                    className="h-9 w-36"
                  />
                  <Button
                    size="sm"
                    disabled={generateMutation.isPending}
                    onClick={() => generateMutation.mutate()}
                  >
                    <RefreshCw className="mr-1.5 h-3.5 w-3.5" />
                    {t("fees.generateMonthly")}
                  </Button>
                </div>
                <Button
                  size="sm"
                  variant="outline"
                  disabled={remindersMutation.isPending}
                  onClick={() => remindersMutation.mutate()}
                >
                  <Bell className="mr-1.5 h-3.5 w-3.5" />
                  {t("fees.sendReminders")}
                </Button>
                <Button size="sm" variant="outline" onClick={() => setSingleOpen(true)}>
                  <Plus className="mr-1 h-3.5 w-3.5" />
                  {t("fees.newInvoice")}
                </Button>
              </>
            ) : null}
          </div>
        }
      />

      <div className="mb-4 flex flex-wrap items-center justify-between gap-2 border-b border-border pb-2">
        <div className="flex flex-wrap gap-1" role="tablist">
          {(["overview", "invoices", "structures", "payhere"] as const).map((key) => (
            <button
              key={key}
              type="button"
              role="tab"
              aria-selected={tab === key}
              onClick={() => setTab(key)}
              className={`rounded-md px-3 py-1.5 text-sm font-medium ${
                tab === key ? "bg-primary text-primary-foreground" : "hover:bg-secondary"
              }`}
            >
              {t(`fees.tab.${key}`)}
            </button>
          ))}
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <Input
            type="month"
            value={periodFilter}
            onChange={(e) => setPeriodFilter(e.target.value)}
            aria-label={t("fees.billingPeriod")}
            className="h-8 w-36 text-xs"
          />
          <select
            value={batchFilter}
            onChange={(e) => setBatchFilter(e.target.value)}
            aria-label={t("students.allBatches")}
            className="h-8 rounded-md border border-border bg-card px-2 text-xs"
          >
            <option value="">{t("students.allBatches")}</option>
            {batches.map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
              </option>
            ))}
          </select>
          {exportFeature.isEnabled ? (
            <div className="flex items-center gap-1">
              <Button size="sm" variant="outline" onClick={() => exportReport("csv")}>
                <Download className="mr-1 h-3.5 w-3.5" />
                {t("students.exportCsv")}
              </Button>
              <Button size="sm" variant="outline" onClick={() => exportReport("xlsx")}>
                <FileSpreadsheet className="mr-1 h-3.5 w-3.5" />
                {t("students.exportExcel")}
              </Button>
              <Button size="sm" variant="outline" onClick={() => exportReport("pdf")}>
                <FileText className="mr-1 h-3.5 w-3.5" />
                {t("students.exportPdf")}
              </Button>
            </div>
          ) : null}
        </div>
      </div>

      {tab === "overview" && dash ? (
        <div className="grid gap-4">
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard
              label={t("fees.expectedTotal")}
              value={formatMoney(dash.expectedTotal)}
              hint={`${dash.paidInvoiceCount + dash.partiallyPaidInvoiceCount + dash.pendingInvoiceCount} ${t("fees.invoicesCount")}`}
            />
            <StatCard
              label={t("fees.collectedTotal")}
              value={formatMoney(dash.collectedTotal)}
              hint={`${dash.collectionRatePercent}% ${t("fees.collectionRate")}`}
            />
            <StatCard
              label={t("fees.outstandingTotal")}
              value={formatMoney(dash.outstandingTotal)}
              hint={`${dash.pendingInvoiceCount + dash.partiallyPaidInvoiceCount} ${t("fees.unpaidCount")}`}
            />
            <StatCard
              label={t("fees.overdueTotal")}
              value={formatMoney(dash.overdueTotal)}
              hint={`${dash.overdueInvoiceCount} ${t("fees.overdueInvoices")}`}
            />
          </div>

          <div className="grid gap-3 xl:grid-cols-2">
            <section className="rounded-lg border border-border bg-card p-4">
              <h2 className="text-sm font-semibold">{t("fees.byBatchTitle")}</h2>
              <p className="mb-3 text-xs text-muted">{t("fees.byBatchDescription")}</p>
              <div className="h-60">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={dash.byBatch}>
                    <CartesianGrid stroke="var(--border)" vertical={false} />
                    <XAxis dataKey="batchName" tick={{ fontSize: 11 }} />
                    <YAxis tick={{ fontSize: 11 }} />
                    <Tooltip />
                    <Bar dataKey="expectedAmount" fill="var(--accent)" name={t("fees.expectedTotal")} />
                    <Bar dataKey="collectedAmount" fill="var(--primary)" name={t("fees.collectedTotal")} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </section>

            <section className="rounded-lg border border-border bg-card p-4">
              <h2 className="text-sm font-semibold">{t("fees.agingTitle")}</h2>
              <p className="mb-3 text-xs text-muted">{t("fees.agingDescription")}</p>
              <div className="h-60">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={dash.overdueAging}>
                    <CartesianGrid stroke="var(--border)" vertical={false} />
                    <XAxis dataKey="bucket" tick={{ fontSize: 11 }} />
                    <YAxis tick={{ fontSize: 11 }} />
                    <Tooltip />
                    <Bar dataKey="balanceAmount" fill="var(--primary)" name={t("fees.outstandingTotal")} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </section>
          </div>

          <div className="grid gap-3 xl:grid-cols-2">
            <section className="rounded-lg border border-border bg-card">
              <div className="border-b border-border px-4 py-3">
                <h2 className="text-sm font-semibold">{t("fees.topDefaultersTitle")}</h2>
                <p className="text-xs text-muted">{t("fees.topDefaultersDescription")}</p>
              </div>
              {dash.topDefaulters.length === 0 ? (
                <p className="p-4 text-sm text-muted">{t("fees.noDefaulters")}</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-border bg-secondary/40 text-xs text-muted">
                      <tr>
                        <th className="px-3 py-2">{t("students.name")}</th>
                        <th className="px-3 py-2">{t("students.phone")}</th>
                        <th className="px-3 py-2">{t("fees.daysOverdue")}</th>
                        <th className="px-3 py-2 text-right">{t("fees.overdueTotal")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {dash.topDefaulters.map((d) => (
                        <tr key={d.studentId} className="border-b border-border last:border-0">
                          <td className="px-3 py-2">
                            <span className="font-medium">{d.studentName}</span>
                            <span className="ml-1.5 text-xs text-muted">({d.studentCode})</span>
                          </td>
                          <td className="px-3 py-2 text-xs">{d.guardianPhone}</td>
                          <td className="px-3 py-2 text-xs">{d.maxDaysOverdue}d</td>
                          <td className="px-3 py-2 text-right font-medium tabular-nums">
                            {formatMoney(d.totalOverdueBalance)}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>

            <section className="rounded-lg border border-border bg-card">
              <div className="border-b border-border px-4 py-3">
                <h2 className="text-sm font-semibold">{t("fees.recentPaymentsTitle")}</h2>
                <p className="text-xs text-muted">{t("fees.recentPaymentsDescription")}</p>
              </div>
              {dash.recentPayments.length === 0 ? (
                <p className="p-4 text-sm text-muted">{t("fees.noPaymentsYet")}</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-sm">
                    <thead className="border-b border-border bg-secondary/40 text-xs text-muted">
                      <tr>
                        <th className="px-3 py-2">{t("fees.receiptNumber")}</th>
                        <th className="px-3 py-2">{t("students.name")}</th>
                        <th className="px-3 py-2">{t("fees.method")}</th>
                        <th className="px-3 py-2 text-right">{t("fees.amount")}</th>
                        <th className="px-3 py-2 text-right">{t("notifications.colActions")}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {dash.recentPayments.map((p) => (
                        <tr key={p.id} className="border-b border-border last:border-0">
                          <td className="px-3 py-2 font-mono text-xs font-medium">{p.receiptNumber}</td>
                          <td className="px-3 py-2 text-xs">
                            {p.studentName} ({p.studentCode})
                          </td>
                          <td className="px-3 py-2 text-xs">{p.method}</td>
                          <td className="px-3 py-2 text-right font-medium tabular-nums">
                            {formatMoney(p.effectiveAmount, p.currency)}
                          </td>
                          <td className="px-3 py-2 text-right">
                            <Button
                              size="sm"
                              variant="outline"
                              onClick={() =>
                                void downloadApi(
                                  `/api/fees/payments/${p.id}/receipt`,
                                  `${p.receiptNumber.toLowerCase()}.pdf`,
                                ).catch((e) => toast.error(e instanceof Error ? e.message : t("errors.generic")))
                              }
                            >
                              <Receipt className="mr-1 h-3.5 w-3.5" />
                              PDF
                            </Button>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </section>
          </div>
        </div>
      ) : null}

      {tab === "invoices" ? (
        <section className="rounded-lg border border-border bg-card">
          <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border p-3">
            <div className="flex flex-wrap items-center gap-2">
              <Input
                value={searchFilter}
                onChange={(e) => setSearchFilter(e.target.value)}
                placeholder={t("fees.searchInvoices")}
                className="h-8 w-56 text-xs"
              />
              <select
                value={statusFilter}
                onChange={(e) => setStatusFilter(e.target.value)}
                className="h-8 rounded-md border border-border bg-card px-2 text-xs"
              >
                <option value="">{t("students.allStatuses")}</option>
                <option value="Pending">Pending</option>
                <option value="PartiallyPaid">PartiallyPaid</option>
                <option value="Paid">Paid</option>
                <option value="Voided">Voided</option>
              </select>
              <label className="flex items-center gap-1.5 text-xs">
                <input
                  type="checkbox"
                  checked={overdueOnly}
                  onChange={(e) => setOverdueOnly(e.target.checked)}
                />
                <span>{t("fees.overdueOnly")}</span>
              </label>
            </div>
            <span className="text-xs text-muted">
              {t("table.rows", { count: invoices.length })}
            </span>
          </div>

          {invoices.length === 0 ? (
            <p className="p-6 text-sm text-muted">{t("table.empty")}</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-border bg-secondary/40 text-xs text-muted">
                  <tr>
                    <th className="px-3 py-2">{t("fees.invoiceNumber")}</th>
                    <th className="px-3 py-2">{t("students.name")}</th>
                    <th className="px-3 py-2">{t("fees.billingPeriod")}</th>
                    <th className="px-3 py-2 text-right">{t("fees.netAmount")}</th>
                    <th className="px-3 py-2 text-right">{t("fees.paidAmount")}</th>
                    <th className="px-3 py-2 text-right">{t("fees.remainingBalance")}</th>
                    <th className="px-3 py-2">{t("fees.dueDate")}</th>
                    <th className="px-3 py-2">{t("students.status")}</th>
                    <th className="px-3 py-2 text-right">{t("notifications.colActions")}</th>
                  </tr>
                </thead>
                <tbody>
                  {invoices.map((inv) => {
                    const canPay = inv.status === "Pending" || inv.status === "PartiallyPaid";
                    return (
                      <tr key={inv.id} className="border-b border-border last:border-0">
                        <td className="px-3 py-2 font-mono text-xs font-medium">{inv.invoiceNumber}</td>
                        <td className="px-3 py-2">
                          <div className="font-medium">{inv.studentName}</div>
                          <div className="text-xs text-muted">
                            {inv.studentCode} · {inv.batchName || inv.feeStructureName}
                          </div>
                        </td>
                        <td className="px-3 py-2 text-xs">{inv.billingPeriod}</td>
                        <td className="px-3 py-2 text-right tabular-nums">
                          <div>{formatMoney(inv.netAmount, inv.currency)}</div>
                          {inv.discountAmount > 0 ? (
                            <div className="text-[11px] text-muted">
                              -{formatMoney(inv.discountAmount, inv.currency)}
                            </div>
                          ) : null}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">
                          {formatMoney(inv.paidAmount, inv.currency)}
                        </td>
                        <td className="px-3 py-2 text-right font-semibold tabular-nums">
                          {formatMoney(inv.balanceAmount, inv.currency)}
                        </td>
                        <td className="px-3 py-2 text-xs">
                          <div>{inv.dueDate}</div>
                          {inv.isOverdue ? (
                            <span className="text-[11px] font-medium text-red-600">
                              {inv.daysOverdue}d {t("fees.overdueShort")}
                            </span>
                          ) : null}
                        </td>
                        <td className="px-3 py-2 text-xs">
                          <span className="rounded border border-border bg-secondary px-2 py-0.5 font-medium">
                            {inv.status}
                          </span>
                        </td>
                        <td className="px-3 py-2 text-right">
                          <div className="flex flex-wrap justify-end gap-1">
                            {canCollect && canPay ? (
                              <Button
                                size="sm"
                                onClick={() => {
                                  setPaymentInvoice(inv);
                                  setPaymentAmount(String(inv.balanceAmount));
                                  setPaymentMethod("Cash");
                                }}
                              >
                                {t("fees.recordPayment")}
                              </Button>
                            ) : null}
                            {canPay && onlinePayFeature.isEnabled ? (
                              <Button
                                size="sm"
                                variant="outline"
                                disabled={payOnlineMutation.isPending}
                                onClick={() => payOnlineMutation.mutate(inv.id)}
                              >
                                <CreditCard className="mr-1 h-3.5 w-3.5" />
                                PayHere
                              </Button>
                            ) : null}
                            {inv.payments.length > 0 ? (
                              <Button
                                size="sm"
                                variant="outline"
                                onClick={() => setHistoryInvoice(inv)}
                              >
                                <Receipt className="mr-1 h-3.5 w-3.5" />
                                {t("fees.receiptsCount", { count: inv.payments.length })}
                              </Button>
                            ) : null}
                            {canCollect && inv.status === "Pending" && inv.paidAmount === 0 ? (
                              <Button
                                size="sm"
                                variant="danger"
                                onClick={() => {
                                  setVoidTarget(inv);
                                  setVoidReason("");
                                }}
                              >
                                {t("fees.void")}
                              </Button>
                            ) : null}
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </section>
      ) : null}

      {tab === "structures" ? (
        <div className="grid gap-4 xl:grid-cols-2">
          <section className="rounded-lg border border-border bg-card p-4">
            <h2 className="text-sm font-semibold">{t("fees.structuresTitle")}</h2>
            <p className="mb-3 text-xs text-muted">{t("fees.structuresDescription")}</p>
            {canCollect ? (
              <form
                className="mb-4 grid gap-2 rounded-md border border-border bg-secondary/30 p-3 sm:grid-cols-2"
                onSubmit={(e) => {
                  e.preventDefault();
                  createStructureMutation.mutate();
                }}
              >
                <div className="sm:col-span-2">
                  <label className="text-xs font-medium" htmlFor="fs-name">
                    {t("plans.name")}
                  </label>
                  <Input
                    id="fs-name"
                    value={structureName}
                    onChange={(e) => setStructureName(e.target.value)}
                    placeholder="2026 O/L Monthly Tuition"
                    required
                  />
                </div>
                <div>
                  <label className="text-xs font-medium" htmlFor="fs-freq">
                    {t("fees.frequency")}
                  </label>
                  <select
                    id="fs-freq"
                    value={structureFrequency}
                    onChange={(e) => setStructureFrequency(e.target.value as FeeFrequency)}
                    className="h-9 w-full rounded-md border border-border bg-card px-2 text-sm"
                  >
                    <option value="Monthly">Monthly</option>
                    <option value="OneTime">OneTime</option>
                    <option value="Admission">Admission</option>
                  </select>
                </div>
                <div>
                  <label className="text-xs font-medium" htmlFor="fs-amount">
                    {t("fees.amount")} (LKR)
                  </label>
                  <Input
                    id="fs-amount"
                    type="number"
                    min="1"
                    step="0.01"
                    value={structureAmount}
                    onChange={(e) => setStructureAmount(e.target.value)}
                    required
                  />
                </div>
                <div>
                  <label className="text-xs font-medium" htmlFor="fs-level">
                    {t("academic.level")}
                  </label>
                  <select
                    id="fs-level"
                    value={structureLevelId}
                    onChange={(e) => setStructureLevelId(e.target.value)}
                    className="h-9 w-full rounded-md border border-border bg-card px-2 text-sm"
                  >
                    <option value="">{t("students.allLevels")}</option>
                    {levels.map((l) => (
                      <option key={l.id} value={l.id}>
                        {l.name}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="text-xs font-medium" htmlFor="fs-batch">
                    {t("academic.batchesTitle")}
                  </label>
                  <select
                    id="fs-batch"
                    value={structureBatchId}
                    onChange={(e) => setStructureBatchId(e.target.value)}
                    className="h-9 w-full rounded-md border border-border bg-card px-2 text-sm"
                  >
                    <option value="">{t("students.allBatches")}</option>
                    {batches.map((b) => (
                      <option key={b.id} value={b.id}>
                        {b.name}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="text-xs font-medium" htmlFor="fs-due">
                    {t("fees.dueDayOfMonth")}
                  </label>
                  <Input
                    id="fs-due"
                    type="number"
                    min="1"
                    max="28"
                    value={structureDueDay}
                    onChange={(e) => setStructureDueDay(e.target.value)}
                    required
                  />
                </div>
                <div className="flex items-end">
                  <Button type="submit" size="sm" disabled={createStructureMutation.isPending}>
                    {t("fees.addStructure")}
                  </Button>
                </div>
              </form>
            ) : null}

            <ul className="divide-y divide-border">
              {structures.map((s) => (
                <li key={s.id} className="flex items-center justify-between gap-2 py-2.5 text-sm">
                  <div>
                    <div className="font-medium">{s.name}</div>
                    <div className="text-xs text-muted">
                      {s.frequency} · {formatMoney(s.amount, s.currency)} ·{" "}
                      {s.batchName || s.educationLevelName || "-"} · {t("fees.dueDayShort", { day: s.dueDayOfMonth })}
                    </div>
                  </div>
                  {canCollect ? (
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => deleteStructureMutation.mutate(s.id)}
                    >
                      {t("common.delete")}
                    </Button>
                  ) : null}
                </li>
              ))}
            </ul>
          </section>

          <section className="rounded-lg border border-border bg-card p-4">
            <h2 className="text-sm font-semibold">{t("fees.concessionsTitle")}</h2>
            <p className="mb-3 text-xs text-muted">{t("fees.concessionsDescription")}</p>
            {canCollect ? (
              <form
                className="mb-4 grid gap-2 rounded-md border border-border bg-secondary/30 p-3 sm:grid-cols-2"
                onSubmit={(e) => {
                  e.preventDefault();
                  createConcessionMutation.mutate();
                }}
              >
                <div>
                  <label className="text-xs font-medium" htmlFor="conc-student">
                    {t("students.name")}
                  </label>
                  <select
                    id="conc-student"
                    value={concessionStudentId}
                    onChange={(e) => setConcessionStudentId(e.target.value)}
                    className="h-9 w-full rounded-md border border-border bg-card px-2 text-sm"
                    required
                  >
                    <option value="">{t("fees.selectStudent")}</option>
                    {students.map((s) => (
                      <option key={s.id} value={s.id}>
                        {s.fullName} ({s.studentCode})
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="text-xs font-medium" htmlFor="conc-fs">
                    {t("fees.structureOptional")}
                  </label>
                  <select
                    id="conc-fs"
                    value={concessionStructureId}
                    onChange={(e) => setConcessionStructureId(e.target.value)}
                    className="h-9 w-full rounded-md border border-border bg-card px-2 text-sm"
                  >
                    <option value="">{t("fees.allStructures")}</option>
                    {structures.map((fs) => (
                      <option key={fs.id} value={fs.id}>
                        {fs.name}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="text-xs font-medium" htmlFor="conc-name">
                    {t("plans.name")}
                  </label>
                  <Input
                    id="conc-name"
                    value={concessionName}
                    onChange={(e) => setConcessionName(e.target.value)}
                    placeholder="Merit Scholarship"
                    required
                  />
                </div>
                <div className="grid grid-cols-2 gap-1">
                  <div>
                    <label className="text-xs font-medium" htmlFor="conc-type">
                      {t("notifications.colType")}
                    </label>
                    <select
                      id="conc-type"
                      value={concessionType}
                      onChange={(e) => setConcessionType(e.target.value as DiscountType)}
                      className="h-9 w-full rounded-md border border-border bg-card px-2 text-xs"
                    >
                      <option value="Percentage">%</option>
                      <option value="FixedAmount">LKR</option>
                    </select>
                  </div>
                  <div>
                    <label className="text-xs font-medium" htmlFor="conc-val">
                      {t("fees.discountValue")}
                    </label>
                    <Input
                      id="conc-val"
                      type="number"
                      min="1"
                      step="0.01"
                      value={concessionValue}
                      onChange={(e) => setConcessionValue(e.target.value)}
                      required
                    />
                  </div>
                </div>
                <div className="sm:col-span-2 flex items-center justify-between gap-2">
                  <Input
                    value={concessionReason}
                    onChange={(e) => setConcessionReason(e.target.value)}
                    placeholder={t("attendance.reason")}
                  />
                  <Button type="submit" size="sm" disabled={createConcessionMutation.isPending}>
                    {t("fees.addConcession")}
                  </Button>
                </div>
              </form>
            ) : null}

            <ul className="divide-y divide-border">
              {concessions.map((c) => (
                <li key={c.id} className="flex items-center justify-between gap-2 py-2.5 text-sm">
                  <div>
                    <div className="font-medium">
                      {c.studentName} ({c.studentCode}) · {c.name}
                    </div>
                    <div className="text-xs text-muted">
                      {c.discountType === "Percentage" ? `${c.value}%` : formatMoney(c.value)} ·{" "}
                      {c.feeStructureName || t("fees.allStructures")}
                    </div>
                  </div>
                  {canCollect ? (
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => deleteConcessionMutation.mutate(c.id)}
                    >
                      {t("common.delete")}
                    </Button>
                  ) : null}
                </li>
              ))}
            </ul>
          </section>
        </div>
      ) : null}

      {tab === "payhere" ? (
        <section className="max-w-xl rounded-lg border border-border bg-card p-5">
          <div className="flex items-center gap-2">
            <ShieldCheck className="h-5 w-5 text-primary" />
            <h2 className="text-sm font-semibold">{t("fees.payhereTitle")}</h2>
          </div>
          <p className="mt-1 text-xs text-muted">{t("fees.payhereDescription")}</p>

          {!onlinePayFeature.isEnabled ? (
            <div className="mt-4 flex items-center gap-2 rounded-md border border-border bg-secondary/50 p-3 text-sm">
              <Lock className="h-4 w-4 text-muted" />
              <span>{t("fees.onlinePaymentsLocked")}</span>
            </div>
          ) : (
            <form
              className="mt-4 grid gap-3"
              onSubmit={(e) => {
                e.preventDefault();
                savePayHereMutation.mutate();
              }}
            >
              <div>
                <label className="text-xs font-medium" htmlFor="ph-mid">
                  {t("fees.merchantId")}
                </label>
                <Input
                  id="ph-mid"
                  value={merchantId}
                  onChange={(e) => setMerchantId(e.target.value)}
                  placeholder="1211149"
                  required
                />
              </div>
              <div>
                <label className="text-xs font-medium" htmlFor="ph-secret">
                  {t("fees.merchantSecret")}
                </label>
                <Input
                  id="ph-secret"
                  type="password"
                  value={merchantSecret}
                  onChange={(e) => setMerchantSecret(e.target.value)}
                  placeholder={
                    payHereSettingsQuery.data?.maskedMerchantSecret || t("fees.merchantSecretPlaceholder")
                  }
                />
                {payHereSettingsQuery.data?.hasMerchantSecret ? (
                  <p className="mt-1 text-[11px] text-muted">
                    {t("fees.secretEncryptedHint", {
                      masked: payHereSettingsQuery.data.maskedMerchantSecret,
                    })}
                  </p>
                ) : null}
              </div>
              <div className="flex items-center justify-between rounded-md border border-border p-3">
                <div>
                  <div className="text-sm font-medium">{t("fees.sandboxMode")}</div>
                  <div className="text-xs text-muted">{t("fees.sandboxModeHint")}</div>
                </div>
                <Switch checked={isSandbox} onCheckedChange={setIsSandbox} />
              </div>
              <div className="flex items-center justify-between rounded-md border border-border p-3">
                <div>
                  <div className="text-sm font-medium">{t("fees.gatewayEnabled")}</div>
                  <div className="text-xs text-muted">{t("fees.gatewayEnabledHint")}</div>
                </div>
                <Switch checked={isGatewayEnabled} onCheckedChange={setIsGatewayEnabled} />
              </div>
              <div>
                <Button type="submit" disabled={savePayHereMutation.isPending}>
                  {t("common.save")}
                </Button>
              </div>
            </form>
          )}
        </section>
      ) : null}

      {/* Create Single Invoice Dialog */}
      <Dialog
        open={singleOpen}
        onOpenChange={setSingleOpen}
        title={t("fees.newInvoice")}
        description={t("fees.newInvoiceDescription")}
      >
        <form
          className="grid gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            createSingleInvoiceMutation.mutate();
          }}
        >
          <div>
            <label className="text-xs font-medium" htmlFor="single-student">
              {t("students.name")}
            </label>
            <select
              id="single-student"
              value={singleStudentId}
              onChange={(e) => setSingleStudentId(e.target.value)}
              className="h-9 w-full rounded-md border border-border bg-card px-2 text-sm"
              required
            >
              <option value="">{t("fees.selectStudent")}</option>
              {students.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.fullName} ({s.studentCode})
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="single-structure">
              {t("fees.structuresTitle")}
            </label>
            <select
              id="single-structure"
              value={singleStructureId}
              onChange={(e) => setSingleStructureId(e.target.value)}
              className="h-9 w-full rounded-md border border-border bg-card px-2 text-sm"
              required
            >
              <option value="">{t("fees.selectStructure")}</option>
              {structures.map((fs) => (
                <option key={fs.id} value={fs.id}>
                  {fs.name} ({formatMoney(fs.amount, fs.currency)})
                </option>
              ))}
            </select>
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="single-period">
              {t("fees.billingPeriod")}
            </label>
            <Input
              id="single-period"
              type="month"
              value={singlePeriod}
              onChange={(e) => setSinglePeriod(e.target.value)}
            />
          </div>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setSingleOpen(false)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={createSingleInvoiceMutation.isPending}>
              {t("common.create")}
            </Button>
          </div>
        </form>
      </Dialog>

      {/* Record Manual Payment Dialog */}
      <Dialog
        open={Boolean(paymentInvoice)}
        onOpenChange={(open) => !open && setPaymentInvoice(null)}
        title={t("fees.recordPayment")}
        description={
          paymentInvoice
            ? `${paymentInvoice.invoiceNumber} · ${paymentInvoice.studentName} · ${t("fees.remainingBalance")}: ${formatMoney(paymentInvoice.balanceAmount, paymentInvoice.currency)}`
            : ""
        }
      >
        <form
          className="grid gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            recordPaymentMutation.mutate();
          }}
        >
          <div className="grid grid-cols-2 gap-2">
            <div>
              <label className="text-xs font-medium" htmlFor="pay-amount">
                {t("fees.amount")}
              </label>
              <Input
                id="pay-amount"
                type="number"
                step="0.01"
                min="0.01"
                max={paymentInvoice?.balanceAmount}
                value={paymentAmount}
                onChange={(e) => setPaymentAmount(e.target.value)}
                required
              />
            </div>
            <div>
              <label className="text-xs font-medium" htmlFor="pay-method">
                {t("fees.method")}
              </label>
              <select
                id="pay-method"
                value={paymentMethod}
                onChange={(e) => setPaymentMethod(e.target.value as PaymentMethod)}
                className="h-9 w-full rounded-md border border-border bg-card px-2 text-sm"
              >
                <option value="Cash">Cash</option>
                <option value="BankTransfer">BankTransfer</option>
              </select>
            </div>
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="pay-ref">
              {t("fees.reference")}
            </label>
            <Input
              id="pay-ref"
              value={paymentReference}
              onChange={(e) => setPaymentReference(e.target.value)}
              placeholder="SLIP-2026-001"
            />
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="pay-slip">
              {t("fees.bankSlipAttachment")}
            </label>
            <Input
              id="pay-slip"
              type="file"
              accept=".pdf,.png,.jpg,.jpeg,.webp"
              onChange={(e) => setPaymentFile(e.target.files?.[0] ?? null)}
            />
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="pay-notes">
              {t("students.notes")}
            </label>
            <Input
              id="pay-notes"
              value={paymentNotes}
              onChange={(e) => setPaymentNotes(e.target.value)}
            />
          </div>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setPaymentInvoice(null)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={recordPaymentMutation.isPending}>
              {t("fees.issueReceipt")}
            </Button>
          </div>
        </form>
      </Dialog>

      {/* Payment History & Refund/Void Dialog */}
      <Dialog
        open={Boolean(historyInvoice)}
        onOpenChange={(open) => {
          if (!open) {
            setHistoryInvoice(null);
            setRefundPaymentId(null);
          }
        }}
        title={t("fees.paymentHistoryTitle")}
        description={historyInvoice ? `${historyInvoice.invoiceNumber} · ${historyInvoice.studentName}` : ""}
      >
        <div className="grid gap-3">
          <ul className="divide-y divide-border">
            {(historyInvoice?.payments ?? []).map((p) => (
              <li key={p.id} className="py-2.5 text-sm">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div>
                    <span className="font-mono font-semibold">{p.receiptNumber}</span>
                    <span className="ml-2 text-xs text-muted">
                      {p.method} · {p.status} · {formatMoney(p.effectiveAmount, p.currency)}
                    </span>
                    {p.attachmentUrl ? (
                      <a
                        href={p.attachmentUrl}
                        target="_blank"
                        rel="noreferrer"
                        className="ml-2 text-xs text-primary underline"
                      >
                        {t("fees.viewSlip")}
                      </a>
                    ) : null}
                  </div>
                  <div className="flex items-center gap-1">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() =>
                        void downloadApi(
                          `/api/fees/payments/${p.id}/receipt`,
                          `${p.receiptNumber.toLowerCase()}.pdf`,
                        )
                      }
                    >
                      PDF
                    </Button>
                    {canCollect && p.status !== "Voided" && p.effectiveAmount > 0 ? (
                      <Button
                        size="sm"
                        variant="danger"
                        onClick={() => {
                          setRefundPaymentId(p.id);
                          setRefundAmount("");
                          setRefundReason("");
                        }}
                      >
                        {t("fees.refundOrVoid")}
                      </Button>
                    ) : null}
                  </div>
                </div>
              </li>
            ))}
          </ul>

          {refundPaymentId ? (
            <form
              className="grid gap-2 rounded-md border border-border bg-secondary/40 p-3"
              onSubmit={(e) => {
                e.preventDefault();
                refundOrVoidMutation.mutate();
              }}
            >
              <div className="text-xs font-semibold">{t("fees.refundOrVoid")}</div>
              <Input
                type="number"
                step="0.01"
                min="0.01"
                value={refundAmount}
                onChange={(e) => setRefundAmount(e.target.value)}
                placeholder={t("fees.refundAmountPlaceholder")}
              />
              <Input
                value={refundReason}
                onChange={(e) => setRefundReason(e.target.value)}
                placeholder={t("attendance.reason")}
                required
              />
              <div className="flex justify-end gap-2">
                <Button type="button" size="sm" variant="outline" onClick={() => setRefundPaymentId(null)}>
                  {t("common.cancel")}
                </Button>
                <Button type="submit" size="sm" variant="danger" disabled={refundOrVoidMutation.isPending}>
                  {t("common.confirm")}
                </Button>
              </div>
            </form>
          ) : null}
        </div>
      </Dialog>

      {/* Void Invoice Dialog */}
      <Dialog
        open={Boolean(voidTarget)}
        onOpenChange={(open) => !open && setVoidTarget(null)}
        title={t("fees.voidInvoiceTitle")}
        description={t("fees.voidInvoiceBody", { number: voidTarget?.invoiceNumber ?? "" })}
      >
        <form
          className="grid gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            voidInvoiceMutation.mutate();
          }}
        >
          <Input
            value={voidReason}
            onChange={(e) => setVoidReason(e.target.value)}
            placeholder={t("attendance.reason")}
            required
          />
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setVoidTarget(null)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" variant="danger" disabled={voidInvoiceMutation.isPending}>
              {t("fees.void")}
            </Button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}
