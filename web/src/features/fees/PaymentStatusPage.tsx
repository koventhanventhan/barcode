import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Clock, CreditCard, Download, ShieldCheck, XCircle } from "lucide-react";
import { useRef } from "react";
import { t } from "@/config/strings";
import { Link, useParams } from "react-router-dom";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { useBranding } from "@/features/branding/BrandingProvider";
import { api } from "@/lib/api";
import { downloadApi } from "@/lib/download";
import { formatMoney, type PayHereCheckoutSessionDto, type PayHereWebhookResultDto } from "./api";

export function PaymentStatusPage() {
  const { orderId = "" } = useParams();
  const toast = useToast();
  const branding = useBranding();
  const queryClient = useQueryClient();
  const formRef = useRef<HTMLFormElement>(null);

  const sessionQuery = useQuery({
    queryKey: ["payhere-order", orderId],
    enabled: Boolean(orderId),
    queryFn: () => api<PayHereCheckoutSessionDto>(`/api/fees/payhere/orders/${encodeURIComponent(orderId)}`),
  });

  const simulateMutation = useMutation({
    mutationFn: (statusCode: number) =>
      api<PayHereWebhookResultDto>("/api/fees/payhere/sandbox-complete", {
        method: "POST",
        body: JSON.stringify({ orderId, statusCode }),
      }),
    onSuccess: async (res) => {
      if (res.status === "Success") {
        toast.success(t("fees.payherePaidToast", { receipt: res.receiptNumber ?? "" }));
      } else {
        toast.success(t("fees.payhereStatusUpdated"));
      }
      await queryClient.invalidateQueries({ queryKey: ["payhere-order", orderId] });
      await queryClient.invalidateQueries({ queryKey: ["fee-invoices"] });
      await queryClient.invalidateQueries({ queryKey: ["fee-dashboard"] });
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  if (sessionQuery.isLoading) return <PageSkeleton />;
  if (sessionQuery.isError || !sessionQuery.data) {
    return (
      <div className="mx-auto max-w-lg py-10">
        <section className="rounded-lg border border-border bg-card p-6">
          <h1 className="text-base font-semibold">{t("fees.paymentStatusTitle")}</h1>
          <p className="mt-2 text-sm text-muted">{t("errors.load")}</p>
        </section>
      </div>
    );
  }

  const session = sessionQuery.data;
  const isSuccess = session.status === "Success";
  const isInitiated = session.status === "Initiated" || session.status === "Pending";

  return (
    <div className="mx-auto max-w-xl py-4">
      <PageHeader
        title={t("fees.paymentStatusTitle")}
        description={branding.branding?.instituteDisplayName || t("fees.paymentStatusDescription")}
      />

      <section className="rounded-lg border border-border bg-card p-5">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-border pb-4">
          <div className="flex items-center gap-2.5">
            {isSuccess ? (
              <CheckCircle2 className="h-6 w-6 text-emerald-600" aria-hidden="true" />
            ) : isInitiated ? (
              <Clock className="h-6 w-6 text-amber-600" aria-hidden="true" />
            ) : (
              <XCircle className="h-6 w-6 text-red-600" aria-hidden="true" />
            )}
            <div>
              <h2 className="text-sm font-semibold">{session.invoiceTitle}</h2>
              <p className="text-xs text-muted">
                {t("fees.orderId")}: <span className="font-mono">{session.orderId}</span>
              </p>
            </div>
          </div>
          <span className="rounded-md border border-border bg-secondary px-2.5 py-1 text-xs font-medium">
            {session.status}
          </span>
        </div>

        <dl className="mt-4 grid gap-3 text-sm sm:grid-cols-2">
          <div>
            <dt className="text-xs text-muted">{t("students.name")}</dt>
            <dd className="font-medium">
              {session.studentName} ({session.studentCode})
            </dd>
          </div>
          <div>
            <dt className="text-xs text-muted">{t("fees.invoiceNumber")}</dt>
            <dd className="font-mono font-medium">{session.invoiceNumber}</dd>
          </div>
          <div>
            <dt className="text-xs text-muted">{t("fees.amountToPay")}</dt>
            <dd className="text-base font-semibold tabular-nums">
              {formatMoney(session.amount, session.currency)}
            </dd>
          </div>
          <div>
            <dt className="text-xs text-muted">{t("fees.remainingBalance")}</dt>
            <dd className="font-medium tabular-nums">
              {formatMoney(session.invoiceBalanceAmount, session.currency)}
            </dd>
          </div>
          {session.receiptNumber ? (
            <div>
              <dt className="text-xs text-muted">{t("fees.receiptNumber")}</dt>
              <dd className="font-mono font-semibold">{session.receiptNumber}</dd>
            </div>
          ) : null}
          {session.payHerePaymentId ? (
            <div>
              <dt className="text-xs text-muted">{t("fees.reference")}</dt>
              <dd className="font-mono">{session.payHerePaymentId}</dd>
            </div>
          ) : null}
        </dl>

        {isSuccess ? (
          <div className="mt-5 flex flex-wrap items-center gap-2 border-t border-border pt-4">
            <Button
              type="button"
              onClick={() =>
                void downloadApi(
                  `/api/fees/payhere/orders/${encodeURIComponent(session.orderId)}/receipt`,
                  `${(session.receiptNumber || session.orderId).toLowerCase()}.pdf`,
                ).catch((e) => toast.error(e instanceof Error ? e.message : t("errors.generic")))
              }
            >
              <Download className="mr-1.5 h-4 w-4" />
              {t("fees.downloadReceipt")}
            </Button>
            <Link to="/app/fees" className="inline-flex h-9 items-center rounded-md border border-border px-3 text-sm hover:bg-secondary">
              {t("common.back")}
            </Link>
          </div>
        ) : null}

        {isInitiated ? (
          <div className="mt-5 grid gap-3 border-t border-border pt-4">
            {session.isSandbox ? (
              <div className="rounded-md border border-border bg-secondary/50 p-3">
                <div className="flex items-center gap-2 text-xs font-semibold">
                  <ShieldCheck className="h-4 w-4 text-primary" />
                  <span>{t("fees.sandboxModeBadge")}</span>
                </div>
                <p className="mt-1 text-xs text-muted">{t("fees.sandboxModeHint")}</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Button
                    type="button"
                    disabled={simulateMutation.isPending}
                    onClick={() => simulateMutation.mutate(2)}
                  >
                    <CreditCard className="mr-1.5 h-4 w-4" />
                    {t("fees.completeSandboxPayment")}
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    disabled={simulateMutation.isPending}
                    onClick={() => simulateMutation.mutate(-1)}
                  >
                    {t("fees.simulateCancel")}
                  </Button>
                </div>
              </div>
            ) : null}

            <form ref={formRef} action={session.checkoutUrl} method="POST">
              {Object.entries(session.formFields).map(([key, value]) => (
                <input key={key} type="hidden" name={key} value={value} />
              ))}
              <Button type="submit" variant={session.isSandbox ? "outline" : "default"}>
                {t("fees.proceedToPayHere")}
              </Button>
            </form>
          </div>
        ) : null}
      </section>
    </div>
  );
}
