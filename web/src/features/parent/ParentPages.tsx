import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { EmptyState } from "@/components/empty-state";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import { downloadApi } from "@/lib/download";
import {
  formatMinutes,
  formatMoney,
  weekdayName,
  type ParentAttendanceItem,
  type ParentBankAccount,
  type ParentBankSlip,
  type ParentCheckout,
  type ParentClassItem,
  type ParentDashboard,
  type ParentExamItem,
  type ParentInvoice,
  type ParentMessage,
  type ParentPayment,
} from "./model";
import { ParentFrame, useParentPortal } from "./ParentShell";

function childPath(studentId: string, suffix: string) {
  return `/api/parent/children/${studentId}/${suffix}`;
}

export function ParentDashboardPage() {
  const { t } = useTranslation();
  const { studentId, home } = useParentPortal();
  const child = home.children.find((item) => item.id === studentId);
  const dashboard = useQuery({
    queryKey: ["parent-dashboard", studentId],
    enabled: Boolean(studentId),
    queryFn: () => api<ParentDashboard>(childPath(studentId, "dashboard")),
  });
  const data = dashboard.data;
  const monthRate = data && data.monthMarked > 0 ? Math.round((data.monthPresent / data.monthMarked) * 100) : null;

  return (
    <ParentFrame title={child?.fullName || t("parent.title")}>
      <p className="text-sm text-muted">
        {[child?.studentCode, child?.levelName, child?.batchNames].filter(Boolean).join(" · ")}
      </p>
      <div className="grid grid-cols-2 gap-3">
        <article className="rounded-lg border border-border bg-card p-3">
          <p className="text-[12px] text-muted">{t("parent.today")}</p>
          <p className="mt-1 text-sm font-medium">
            {data?.today.length ? data.today.map((item) => item.status).join(", ") : t("parent.noAttendance")}
          </p>
        </article>
        <article className="rounded-lg border border-border bg-card p-3">
          <p className="text-[12px] text-muted">{t("parent.month")}</p>
          <p className="mt-1 text-sm font-medium tabular-nums">{monthRate === null ? "—" : `${monthRate}%`}</p>
        </article>
        <article className="rounded-lg border border-border bg-card p-3">
          <p className="text-[12px] text-muted">{t("parent.due")}</p>
          <p className="mt-1 text-sm font-medium tabular-nums">
            {data ? formatMoney(data.outstandingAmount, data.currency) : "—"}
          </p>
        </article>
        <article className="rounded-lg border border-border bg-card p-3">
          <p className="text-[12px] text-muted">{t("parent.openInvoices")}</p>
          <p className="mt-1 text-sm font-medium tabular-nums">{data?.openInvoiceCount ?? "—"}</p>
        </article>
      </div>
      <section className="rounded-lg border border-border bg-card p-4">
        <h2 className="text-sm font-semibold">{t("parent.upcomingClasses")}</h2>
        <ClassList items={data?.upcomingClasses.slice(0, 3) ?? []} empty={t("parent.noClasses")} />
      </section>
      <section className="rounded-lg border border-border bg-card p-4">
        <h2 className="text-sm font-semibold">{t("parent.upcomingExams")}</h2>
        {data?.upcomingExams.length ? (
          <ul className="mt-2 grid gap-2">
            {data.upcomingExams.slice(0, 3).map((exam) => (
              <li key={exam.id} className="text-sm">
                {exam.name}
                <span className="text-muted"> · {exam.status}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="mt-2 text-sm text-muted">{t("parent.noExams")}</p>
        )}
      </section>
      {data?.latestMessage ? (
        <section className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("parent.latestMessage")}</h2>
          <p className="mt-2 text-sm">{data.latestMessage.body}</p>
        </section>
      ) : null}
    </ParentFrame>
  );
}

export function ParentAttendancePage() {
  const { t } = useTranslation();
  const { studentId } = useParentPortal();
  const query = useQuery({
    queryKey: ["parent-attendance", studentId],
    enabled: Boolean(studentId),
    queryFn: () => api<ParentAttendanceItem[]>(childPath(studentId, "attendance")),
  });
  return (
    <ParentFrame title={t("parent.navAttendance")}>
      {query.data?.length ? (
        <ul className="grid gap-2">
          {query.data.map((item) => (
            <li key={item.id} className="rounded-lg border border-border bg-card px-3 py-3">
              <div className="flex items-center justify-between gap-3">
                <p className="text-sm font-medium">{item.className}</p>
                <p className="text-sm">{item.status}</p>
              </div>
              <p className="mt-1 text-[12px] text-muted">
                {item.sessionDate}
                {item.subjectName ? ` · ${item.subjectName}` : ""}
              </p>
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState title={t("parent.noAttendance")} />
      )}
    </ParentFrame>
  );
}

export function ParentClassesPage() {
  const { t } = useTranslation();
  const { studentId } = useParentPortal();
  const query = useQuery({
    queryKey: ["parent-classes", studentId],
    enabled: Boolean(studentId),
    queryFn: () => api<ParentClassItem[]>(childPath(studentId, "classes")),
  });
  return (
    <ParentFrame title={t("parent.navClasses")}>
      <ClassList items={query.data ?? []} empty={t("parent.noClasses")} />
    </ParentFrame>
  );
}

export function ParentResultsPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { studentId, home } = useParentPortal();
  const query = useQuery({
    queryKey: ["parent-exams", studentId],
    enabled: Boolean(studentId),
    queryFn: () => api<ParentExamItem[]>(childPath(studentId, "exams")),
  });

  return (
    <ParentFrame title={t("parent.navResults")}>
      {!home.canSeeMarks ? <p className="text-sm text-muted">{t("parent.marksHidden")}</p> : null}
      {query.data?.length ? (
        <ul className="grid gap-3">
          {query.data.map((exam) => (
            <li key={exam.id} className="rounded-lg border border-border bg-card p-4">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <h2 className="text-sm font-semibold">{exam.name}</h2>
                  <p className="text-[12px] text-muted">
                    {exam.kind} · {exam.status}
                    {exam.scheduledAt ? ` · ${new Date(exam.scheduledAt).toLocaleString()}` : ""}
                  </p>
                </div>
                {exam.grade ? <p className="text-sm font-semibold tabular-nums">{exam.grade}</p> : null}
              </div>
              {exam.percentage !== null ? (
                <p className="mt-2 text-sm tabular-nums">
                  {exam.percentage}%
                  {exam.batchRank ? ` · ${t("parent.rank", { rank: exam.batchRank })}` : ""}
                </p>
              ) : null}
              {exam.subjects?.length ? (
                <ul className="mt-2 grid gap-1">
                  {exam.subjects.map((subject) => (
                    <li key={subject.subject} className="flex justify-between text-sm">
                      <span>{subject.subject}</span>
                      <span className="tabular-nums">{subject.isAbsent ? t("parent.absent") : (subject.marks ?? "—")}</span>
                    </li>
                  ))}
                </ul>
              ) : null}
              {home.reportCardsAvailable && exam.status === "Published" ? (
                <Button
                  className="mt-3"
                  variant="outline"
                  onClick={() =>
                    void downloadApi(
                      `${childPath(studentId, `exams/${exam.id}/report-card`)}`,
                      `${exam.name}.pdf`,
                    ).catch((error: unknown) => toast.error(error instanceof Error ? error.message : t("errors.generic")))
                  }
                >
                  {t("parent.reportCard")}
                </Button>
              ) : null}
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState title={t("parent.noExams")} />
      )}
    </ParentFrame>
  );
}

export function ParentFeesPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { studentId, home } = useParentPortal();
  const [checkout, setCheckout] = useState<ParentCheckout | null>(null);
  const [slipTarget, setSlipTarget] = useState<ParentInvoice | null>(null);
  const [slipAmount, setSlipAmount] = useState("");
  const [slipRef, setSlipRef] = useState("");
  const [slipDate, setSlipDate] = useState("");
  const [slipNotes, setSlipNotes] = useState("");
  const [slipFile, setSlipFile] = useState<File | null>(null);

  const invoices = useQuery({
    queryKey: ["parent-invoices", studentId],
    enabled: Boolean(studentId),
    queryFn: () => api<ParentInvoice[]>(childPath(studentId, "invoices")),
  });
  const payments = useQuery({
    queryKey: ["parent-payments", studentId],
    enabled: Boolean(studentId),
    queryFn: () => api<ParentPayment[]>(childPath(studentId, "payments")),
  });
  const bankAccount = useQuery({
    queryKey: ["parent-bank-account"],
    queryFn: () => api<ParentBankAccount>("/api/parent/bank-account"),
  });
  const bankSlips = useQuery({
    queryKey: ["parent-bank-slips", studentId],
    enabled: Boolean(studentId),
    queryFn: () => api<ParentBankSlip[]>(childPath(studentId, "bank-slips")),
  });

  const pay = useMutation({
    mutationFn: (invoiceId: string) =>
      api<ParentCheckout>(`${childPath(studentId, `invoices/${invoiceId}/pay`)}`, {
        method: "POST",
        body: JSON.stringify({}),
      }),
    onSuccess: (session) => setCheckout(session),
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  const uploadSlip = useMutation({
    mutationFn: async () => {
      if (!slipTarget || !slipFile) throw new Error(t("fees.slipFileRequired"));
      const fd = new FormData();
      fd.append("amount", slipAmount);
      if (slipRef.trim()) fd.append("referenceNumber", slipRef.trim());
      if (slipDate) fd.append("transferDate", slipDate);
      if (slipNotes.trim()) fd.append("notes", slipNotes.trim());
      fd.append("file", slipFile);
      return api<ParentBankSlip>(childPath(studentId, `invoices/${slipTarget.id}/bank-slips`), {
        method: "POST",
        body: fd,
      });
    },
    onSuccess: async () => {
      toast.success(t("fees.bankSlipSubmitted"));
      setSlipTarget(null);
      setSlipFile(null);
      setSlipRef("");
      setSlipDate("");
      setSlipNotes("");
      await queryClient.invalidateQueries({ queryKey: ["parent-bank-slips", studentId] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  const bank = bankAccount.data;

  return (
    <ParentFrame title={t("parent.navFees")}>
      {!home.canPayOnline ? <p className="text-sm text-muted">{t("parent.payHidden")}</p> : null}

      {bank && bank.isConfigured && bank.isEnabled ? (
        <section className="rounded-lg border border-border bg-card p-3">
          <h2 className="text-sm font-semibold">{t("fees.bankDetailsForTransfer")}</h2>
          <dl className="mt-2 grid gap-1 text-xs sm:grid-cols-2">
            <div>
              <dt className="text-muted">{t("fees.bankName")}</dt>
              <dd className="font-medium">
                {bank.bankName}
                {bank.branchName ? ` · ${bank.branchName}` : ""}
              </dd>
            </div>
            <div>
              <dt className="text-muted">{t("fees.accountNumber")}</dt>
              <dd className="font-mono font-semibold">{bank.accountNumber}</dd>
            </div>
            <div className="sm:col-span-2">
              <dt className="text-muted">{t("fees.accountHolder")}</dt>
              <dd className="font-medium">{bank.accountName}</dd>
            </div>
            {bank.instructions ? (
              <div className="sm:col-span-2">
                <dt className="text-muted">{t("fees.bankInstructions")}</dt>
                <dd>{bank.instructions}</dd>
              </div>
            ) : null}
          </dl>
        </section>
      ) : null}

      <h2 className="text-sm font-semibold">{t("parent.invoices")}</h2>
      {invoices.data?.length ? (
        <ul className="grid gap-2">
          {invoices.data.map((invoice) => (
            <li key={invoice.id} className="rounded-lg border border-border bg-card p-3">
              <div className="flex items-start justify-between gap-3">
                <div>
                  <p className="text-sm font-medium">{invoice.title}</p>
                  <p className="text-[12px] text-muted">
                    {invoice.invoiceNumber} · {t("parent.dueDate", { date: invoice.dueDate })} · {invoice.status}
                  </p>
                  {invoice.creditAppliedAmount && invoice.creditAppliedAmount > 0 ? (
                    <p className="text-[11px] text-muted">
                      {t("fees.creditApplied")}: {formatMoney(invoice.creditAppliedAmount, invoice.currency)}
                    </p>
                  ) : null}
                </div>
                <p className="text-sm font-medium tabular-nums">{formatMoney(invoice.balanceAmount, invoice.currency)}</p>
              </div>
              {invoice.balanceAmount > 0 ? (
                <div className="mt-3 flex flex-wrap gap-2">
                  {home.canPayOnline ? (
                    <Button disabled={pay.isPending} onClick={() => pay.mutate(invoice.id)}>
                      {t("parent.pay")}
                    </Button>
                  ) : null}
                  <Button
                    variant="outline"
                    onClick={() => {
                      setSlipTarget(invoice);
                      setSlipAmount(String(invoice.balanceAmount));
                      setSlipRef("");
                      setSlipDate("");
                      setSlipNotes("");
                      setSlipFile(null);
                    }}
                  >
                    {t("fees.uploadBankSlip")}
                  </Button>
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState title={t("parent.noInvoices")} />
      )}
      {checkout ? (
        <section className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("parent.checkoutTitle")}</h2>
          <p className="mt-1 text-sm text-muted">
            {checkout.orderId} · {formatMoney(checkout.amount, checkout.currency)}
          </p>
          <form className="mt-3" action={checkout.checkoutUrl} method="POST">
            {Object.entries(checkout.formFields).map(([key, value]) => (
              <input key={key} type="hidden" name={key} value={value} />
            ))}
            <Button type="submit">{t("parent.continuePay")}</Button>
          </form>
          <Link className="mt-3 inline-block text-[13px] text-primary" to={`/pay/${checkout.orderId}`}>
            {t("parent.paymentStatus")}
          </Link>
        </section>
      ) : null}

      {bankSlips.data?.length ? (
        <>
          <h2 className="text-sm font-semibold">{t("fees.submittedBankSlips")}</h2>
          <ul className="grid gap-2">
            {bankSlips.data.map((slip) => (
              <li key={slip.id} className="rounded-lg border border-border bg-card px-3 py-3 text-sm">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-medium">
                      {slip.invoiceNumber} · {formatMoney(slip.amount, slip.currency)}
                    </p>
                    <p className="text-[12px] text-muted">
                      {slip.status}
                      {slip.referenceNumber ? ` · ${slip.referenceNumber}` : ""}
                      {slip.receiptNumber ? ` · ${slip.receiptNumber}` : ""}
                    </p>
                    {slip.rejectionReason ? (
                      <p className="mt-1 text-xs text-red-600">{slip.rejectionReason}</p>
                    ) : null}
                  </div>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() =>
                      void downloadApi(
                        childPath(studentId, `bank-slips/${slip.id}/file`),
                        slip.slipFileName,
                      ).catch((e: unknown) => toast.error(e instanceof Error ? e.message : t("errors.generic")))
                    }
                  >
                    {t("fees.viewSlip")}
                  </Button>
                </div>
              </li>
            ))}
          </ul>
        </>
      ) : null}

      <h2 className="text-sm font-semibold">{t("parent.payments")}</h2>
      {payments.data?.length ? (
        <ul className="grid gap-2">
          {payments.data.map((payment) => (
            <li key={payment.id} className="rounded-lg border border-border bg-card px-3 py-3 text-sm">
              <div className="flex items-center justify-between gap-3">
                <div>
                  <span>{payment.receiptNumber}</span>
                  <span className="ml-2 tabular-nums">{formatMoney(payment.amount, payment.currency)}</span>
                  <p className="mt-1 text-[12px] text-muted">
                    {payment.method} · {new Date(payment.paidAt).toLocaleString()} · {payment.status}
                  </p>
                </div>
                <Button
                  size="sm"
                  variant="outline"
                  onClick={() =>
                    void downloadApi(
                      childPath(studentId, `payments/${payment.id}/receipt`),
                      `${payment.receiptNumber.toLowerCase()}.pdf`,
                    ).catch((e: unknown) => toast.error(e instanceof Error ? e.message : t("errors.generic")))
                  }
                >
                  PDF
                </Button>
              </div>
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState title={t("parent.noPayments")} />
      )}

      <Dialog
        open={Boolean(slipTarget)}
        onOpenChange={(open) => !open && setSlipTarget(null)}
        title={t("fees.uploadBankSlip")}
        description={slipTarget ? `${slipTarget.invoiceNumber} · ${formatMoney(slipTarget.balanceAmount, slipTarget.currency)}` : ""}
      >
        <form
          className="grid gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            uploadSlip.mutate();
          }}
        >
          <div>
            <label className="text-xs font-medium" htmlFor="p-slip-amt">
              {t("fees.amount")}
            </label>
            <Input
              id="p-slip-amt"
              type="number"
              step="0.01"
              min="0.01"
              value={slipAmount}
              onChange={(e) => setSlipAmount(e.target.value)}
              required
            />
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="p-slip-ref">
              {t("fees.reference")}
            </label>
            <Input
              id="p-slip-ref"
              value={slipRef}
              onChange={(e) => setSlipRef(e.target.value)}
              placeholder="BANK-REF-001"
            />
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="p-slip-date">
              {t("fees.transferDate")}
            </label>
            <Input
              id="p-slip-date"
              type="date"
              value={slipDate}
              onChange={(e) => setSlipDate(e.target.value)}
            />
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="p-slip-file">
              {t("fees.bankSlipAttachment")}
            </label>
            <Input
              id="p-slip-file"
              type="file"
              accept=".pdf,.png,.jpg,.jpeg,.webp"
              onChange={(e) => setSlipFile(e.target.files?.[0] ?? null)}
              required
            />
          </div>
          <div>
            <label className="text-xs font-medium" htmlFor="p-slip-notes">
              {t("students.notes")}
            </label>
            <Input
              id="p-slip-notes"
              value={slipNotes}
              onChange={(e) => setSlipNotes(e.target.value)}
            />
          </div>
          <div className="flex justify-end gap-2">
            <Button type="button" variant="outline" onClick={() => setSlipTarget(null)}>
              {t("common.cancel")}
            </Button>
            <Button type="submit" disabled={uploadSlip.isPending}>
              {t("fees.submitBankSlip")}
            </Button>
          </div>
        </form>
      </Dialog>
    </ParentFrame>
  );
}

export function ParentMessagesPage() {
  const { t } = useTranslation();
  const query = useQuery({
    queryKey: ["parent-messages"],
    queryFn: () => api<ParentMessage[]>("/api/parent/messages"),
  });
  return (
    <ParentFrame title={t("parent.navMessages")}>
      {query.data?.length ? (
        <ul className="grid gap-2">
          {query.data.map((message) => (
            <li key={message.id} className="rounded-lg border border-border bg-card p-3">
              <p className="text-[12px] text-muted">
                {message.messageType} · {message.channel} · {message.status}
                {message.studentName ? ` · ${message.studentName}` : ""}
              </p>
              <p className="mt-1 text-sm">{message.body}</p>
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState title={t("parent.noMessages")} />
      )}
    </ParentFrame>
  );
}

function ClassList({ items, empty }: { items: ParentClassItem[]; empty: string }) {
  if (!items.length) return <p className="mt-2 text-sm text-muted">{empty}</p>;
  return (
    <ul className="mt-2 grid gap-2">
      {items.map((item) => (
        <li key={`${item.id}-${item.nextDate}`} className="text-sm">
          <p className="font-medium">{item.subjectName || item.name}</p>
          <p className="text-[12px] text-muted">
            {weekdayName(item.weekday)} {formatMinutes(item.startMinutes)}–{formatMinutes(item.endMinutes)}
            {item.room ? ` · ${item.room}` : ""}
            {item.teacherName ? ` · ${item.teacherName}` : ""}
          </p>
        </li>
      ))}
    </ul>
  );
}
