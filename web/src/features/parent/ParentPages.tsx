import { useMutation, useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { EmptyState } from "@/components/empty-state";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
import { downloadApi } from "@/lib/download";
import {
  formatMinutes,
  formatMoney,
  weekdayName,
  type ParentAttendanceItem,
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
  const { studentId, home } = useParentPortal();
  const [checkout, setCheckout] = useState<ParentCheckout | null>(null);
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
  const pay = useMutation({
    mutationFn: (invoiceId: string) =>
      api<ParentCheckout>(`${childPath(studentId, `invoices/${invoiceId}/pay`)}`, {
        method: "POST",
        body: JSON.stringify({}),
      }),
    onSuccess: (session) => setCheckout(session),
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  return (
    <ParentFrame title={t("parent.navFees")}>
      {!home.canPayOnline ? <p className="text-sm text-muted">{t("parent.payHidden")}</p> : null}
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
                </div>
                <p className="text-sm font-medium tabular-nums">{formatMoney(invoice.balanceAmount, invoice.currency)}</p>
              </div>
              {home.canPayOnline && invoice.balanceAmount > 0 ? (
                <Button className="mt-3" disabled={pay.isPending} onClick={() => pay.mutate(invoice.id)}>
                  {t("parent.pay")}
                </Button>
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
      <h2 className="text-sm font-semibold">{t("parent.payments")}</h2>
      {payments.data?.length ? (
        <ul className="grid gap-2">
          {payments.data.map((payment) => (
            <li key={payment.id} className="rounded-lg border border-border bg-card px-3 py-3 text-sm">
              <div className="flex justify-between gap-3">
                <span>{payment.receiptNumber}</span>
                <span className="tabular-nums">{formatMoney(payment.amount, payment.currency)}</span>
              </div>
              <p className="mt-1 text-[12px] text-muted">
                {payment.method} · {new Date(payment.paidAt).toLocaleString()} · {payment.status}
              </p>
            </li>
          ))}
        </ul>
      ) : (
        <EmptyState title={t("parent.noPayments")} />
      )}
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
