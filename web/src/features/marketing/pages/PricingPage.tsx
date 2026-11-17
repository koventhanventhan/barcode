import { useQuery } from "@tanstack/react-query";
import { ChevronDown } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { MarketingMeta } from "../MarketingMeta";
import { formatMonthlyPrice } from "../price";

type PublicPlan = {
  code: string;
  name: string;
  description: string | null;
  priceMonthly: number | null;
  currency: string;
  features: string[];
  limits: Record<string, number | null>;
};

const limitKeys = [
  "Students",
  "EducationLevels",
  "Batches",
  "Subjects",
  "StaffAccounts",
  "AdminAccounts",
  "SmsSegmentsPerMonth",
  "CloudStorageGb",
];

const featureKeys = [
  "DataExport",
  "BarcodeGenerator",
  "PerformanceReports",
  "OnlinePayments",
  "WhatsAppMessaging",
  "Timetable",
  "AutoBackup",
  "ParentPortal",
];

const faqs = ["trial", "price", "sms", "portal", "data"] as const;

export function PricingPage() {
  const { t } = useTranslation();
  const plans = useQuery({ queryKey: ["public-plans"], queryFn: () => api<PublicPlan[]>("/api/plans") });
  const [openFaq, setOpenFaq] = useState<string | null>("trial");

  return (
    <>
      <MarketingMeta
        title={t("marketing.pricingTitle")}
        description={t("marketing.pricingBody")}
        path="/pricing"
      />

      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
        <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">01</p>
        <h1 className="mt-2 font-heading text-3xl font-extrabold tracking-[-0.02em] sm:text-[44px]">
          {t("marketing.pricingTitle")}
        </h1>
        <p className="mt-3 max-w-[65ch] text-base leading-[1.6] text-muted">
          {t("marketing.pricingBody")}
        </p>

        {plans.isError ? (
          <p className="mt-6 rounded-md border border-border bg-card p-4 text-sm" role="alert">
            {t("marketing.pricingError")}{" "}
            <button type="button" className="font-semibold text-primary underline" onClick={() => void plans.refetch()}>
              {t("common.retry")}
            </button>
          </p>
        ) : (
          <>
            <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {(plans.data ?? []).map((plan) => (
                <article
                  key={plan.code}
                  className={cn(
                    "flex flex-col justify-between rounded-lg border bg-card p-5",
                    plan.code === "Pro" ? "border-primary" : "border-border",
                  )}
                >
                  <div>
                    <div className="flex items-center justify-between">
                      <h2 className="font-heading text-lg font-bold">{plan.name}</h2>
                      {plan.code === "Pro" ? (
                        <span className="rounded bg-brand-50 px-2 py-0.5 text-[11px] font-semibold text-primary">
                          Pro
                        </span>
                      ) : null}
                    </div>
                    <p className="mt-2 font-heading text-2xl font-extrabold tracking-[-0.02em] tabular">
                      {formatMonthlyPrice(plan.priceMonthly, plan.currency) ?? t("marketing.contactUs")}
                    </p>
                    {plan.description ? (
                      <p className="mt-2 text-xs leading-5 text-muted">{plan.description}</p>
                    ) : null}
                    <ul className="mt-4 grid gap-1.5 border-t border-border pt-3 text-xs tabular text-foreground">
                      <li>{t("limit.Students")}: {plan.limits.Students ?? t("common.unlimited")}</li>
                      <li>{t("limit.SmsSegmentsPerMonth")}: {plan.limits.SmsSegmentsPerMonth ?? t("common.unlimited")}</li>
                      <li>{t("limit.CloudStorageGb")}: {plan.limits.CloudStorageGb ?? t("common.unlimited")} GB</li>
                    </ul>
                  </div>
                  <Link
                    to="/signup"
                    className="mt-5 inline-flex h-9 items-center justify-center rounded-md bg-primary px-3 text-xs font-semibold text-primary-foreground"
                  >
                    {t("marketing.startFreeTrial")}
                  </Link>
                </article>
              ))}
            </div>

            <div className="mt-10 overflow-x-auto rounded-lg border border-border bg-card">
              <table className="w-full min-w-[40rem] border-collapse text-left text-[13px]">
                <thead>
                  <tr className="border-b border-border bg-secondary/40">
                    <th className="px-4 py-3 font-semibold">{t("marketing.compare")}</th>
                    {(plans.data ?? []).map((plan) => (
                      <th key={plan.code} className="px-4 py-3 font-heading font-bold">
                        {plan.name}
                        <div className="mt-0.5 font-sans text-xs font-normal tabular text-muted">
                          {formatMonthlyPrice(plan.priceMonthly, plan.currency) ?? t("marketing.contactUs")}
                        </div>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {limitKeys.map((key) => (
                    <tr key={key} className="border-b border-border">
                      <th className="px-4 py-2.5 font-normal text-foreground">{t(`limit.${key}`, { defaultValue: key })}</th>
                      {(plans.data ?? []).map((plan) => (
                        <td key={plan.code} className="px-4 py-2.5 tabular">
                          {plan.limits[key] == null ? t("common.unlimited") : plan.limits[key]}
                        </td>
                      ))}
                    </tr>
                  ))}
                  {featureKeys.map((key) => (
                    <tr key={key} className="border-b border-border last:border-b-0">
                      <th className="px-4 py-2.5 font-normal text-foreground">{t(`feature.${key}`, { defaultValue: key })}</th>
                      {(plans.data ?? []).map((plan) => (
                        <td key={plan.code} className="px-4 py-2.5">
                          {plan.features.includes(key) ? (
                            <span className="font-medium text-primary">{t("common.yes")}</span>
                          ) : (
                            <span className="text-muted">{t("common.no")}</span>
                          )}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </section>

      {/* Keyboard-accessible FAQ Accordion */}
      <section className="border-t border-border bg-card">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
          <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">02</p>
          <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em]">
            {t("marketing.faqTitle")}
          </h2>
          <div className="mt-6 grid max-w-3xl divide-y divide-border rounded-lg border border-border bg-background">
            {faqs.map((id) => {
              const expanded = openFaq === id;
              const panelId = `faq-panel-${id}`;
              return (
                <div key={id}>
                  <button
                    type="button"
                    aria-expanded={expanded}
                    aria-controls={panelId}
                    onClick={() => setOpenFaq(expanded ? null : id)}
                    className="flex w-full items-center justify-between gap-4 px-5 py-4 text-left text-sm font-semibold text-foreground hover:text-primary"
                  >
                    <span>{t(`marketing.faq.${id}.q`)}</span>
                    <ChevronDown
                      className={cn("h-4 w-4 shrink-0 stroke-[1.5] text-muted transition-transform", expanded && "rotate-180 text-primary")}
                      aria-hidden="true"
                    />
                  </button>
                  <div id={panelId} hidden={!expanded} className="px-5 pb-4 text-sm leading-6 text-muted">
                    <p className="max-w-[65ch]">{t(`marketing.faq.${id}.a`)}</p>
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </section>
    </>
  );
}

export default PricingPage;
