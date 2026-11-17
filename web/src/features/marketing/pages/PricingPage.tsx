import { useQuery } from "@tanstack/react-query";
import { Check, ChevronDown, Minus } from "lucide-react";
import { Fragment, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { api } from "@/lib/api";
import type { Plan } from "@/lib/types";
import { cn } from "@/lib/utils";
import { COMPARISON_GROUPS } from "../comparison";
import { MarketingMeta } from "../MarketingMeta";
import { addonPrice, formatMonthlyPrice, monthsFree, monthsFreeBadge, type AddonPrice } from "../price";

const faqs = ["payment", "trial", "export", "cancel", "sms", "domain", "languages"] as const;

const cardLimits = [
  ["Students", ""],
  ["Batches", ""],
  ["StaffAccounts", ""],
  ["SmsSegmentsPerMonth", ""],
  ["CloudStorageGb", " GB"],
] as const;

function formatLimit(value: number | null | undefined, unlimited: string, suffix: string) {
  if (value == null) return unlimited;
  return `${new Intl.NumberFormat("en-LK", { maximumFractionDigits: 0 }).format(value)}${suffix}`;
}

function priceLabel(plan: Plan, cycle: "monthly" | "yearly") {
  const amount = cycle === "yearly" ? plan.priceYearlyLkr : plan.priceMonthlyLkr;
  return formatMonthlyPrice(amount, plan.currency || "LKR");
}

function FeatureMark({ included, yes, no }: { included: boolean; yes: string; no: string }) {
  const Icon = included ? Check : Minus;
  return (
    <span className={cn("inline-flex items-center", included ? "text-primary" : "text-muted")} data-included={included ? "true" : "false"}>
      <Icon className="h-4 w-4 stroke-[1.5]" aria-hidden="true" />
      <span className="sr-only">{included ? yes : no}</span>
    </span>
  );
}

export function PricingPage() {
  const { t } = useTranslation();
  const plans = useQuery({ queryKey: ["public-plans"], queryFn: () => api<Plan[]>("/api/plans") });
  const [cycle, setCycle] = useState<"monthly" | "yearly">("monthly");
  const [openFaq, setOpenFaq] = useState<string | null>("trial");
  const [mobilePlan, setMobilePlan] = useState<string | null>(null);
  const rows = plans.data ?? [];
  const active = rows.find((plan) => plan.code === mobilePlan) ?? rows[0];
  const badge = monthsFreeBadge(rows);
  const currency = rows[0]?.currency || "LKR";
  const smsAddon = addonPrice(rows.map((plan) => plan.extraSmsPerSegmentLkr));
  const storageAddon = addonPrice(rows.map((plan) => plan.extraStoragePerGbLkr));
  const setupAddon = addonPrice(rows.map((plan) => plan.setupFeeLkr));
  const whatsappPlans = rows.filter((plan) => plan.features.includes("WhatsAppMessaging")).map((plan) => plan.name);
  const enterprise = rows.find((plan) => plan.code === "Enterprise");
  const yes = t("marketing.includedMark");
  const no = t("marketing.notIncludedMark");

  function addonText(price: AddonPrice) {
    if (price.kind === "none") return t("marketing.contactUs");
    const formatted = formatMonthlyPrice(price.amount, currency) ?? t("marketing.contactUs");
    return price.kind === "from" ? t("marketing.addonFrom", { price: formatted }) : formatted;
  }

  return (
    <>
      <MarketingMeta title={t("marketing.pricingTitle")} description={t("marketing.pricingBody")} path="/pricing" />

      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
        <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">01</p>
        <h1 className="mt-2 font-heading text-3xl font-extrabold tracking-[-0.02em] sm:text-[44px]">
          {t("marketing.pricingTitle")}
        </h1>
        <p className="mt-3 max-w-[65ch] text-base leading-[1.6] text-muted">{t("marketing.pricingBody")}</p>

        <div className="mt-6 flex flex-wrap items-center gap-3">
          <p className="text-xs font-semibold uppercase tracking-[0.12em] text-muted">{t("marketing.currencyLabel")}</p>
          <div className="inline-flex rounded-md border border-border bg-card p-0.5" role="group" aria-label={t("marketing.billingLabel")}>
            <button
              type="button"
              aria-pressed={cycle === "monthly"}
              onClick={() => setCycle("monthly")}
              className={cn("h-8 rounded px-3 text-xs font-semibold", cycle === "monthly" ? "bg-primary text-primary-foreground" : "text-foreground")}
            >
              {t("marketing.billingMonthly")}
            </button>
            <button
              type="button"
              aria-pressed={cycle === "yearly"}
              onClick={() => setCycle("yearly")}
              className={cn("h-8 rounded px-3 text-xs font-semibold", cycle === "yearly" ? "bg-primary text-primary-foreground" : "text-foreground")}
            >
              {t("marketing.billingYearly")}
            </button>
          </div>
          {cycle === "yearly" && badge ? (
            <span className="rounded bg-brand-50 px-2 py-0.5 text-[11px] font-semibold text-primary">
              {badge.varied ? t("marketing.monthsFreeUpTo", { count: badge.count }) : t("marketing.monthsFree", { count: badge.count })}
            </span>
          ) : null}
        </div>

        {plans.isError ? (
          <p className="mt-6 rounded-md border border-border bg-card p-4 text-sm" role="alert">
            {t("marketing.pricingError")}{" "}
            <button type="button" className="font-semibold text-primary underline" onClick={() => void plans.refetch()}>
              {t("common.retry")}
            </button>
          </p>
        ) : plans.isLoading ? (
          <p className="mt-8 text-sm text-muted">{t("common.loading")}</p>
        ) : (
          <>
            <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
              {rows.map((plan) => {
                const shown = priceLabel(plan, cycle);
                const free = monthsFree(plan.priceMonthlyLkr, plan.priceYearlyLkr);
                const enterprisePlan = plan.code === "Enterprise";
                return (
                  <article
                    key={plan.code}
                    aria-label={plan.name}
                    className={cn("flex flex-col justify-between rounded-lg border bg-card p-5", plan.code === "Pro" ? "border-primary" : "border-border")}
                  >
                    <div>
                      <div className="flex items-start justify-between gap-2">
                        <h2 className="font-heading text-lg font-bold">{plan.name}</h2>
                        {plan.code === "Pro" ? (
                          <span className="rounded bg-brand-50 px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wide text-primary">
                            {t("marketing.mostPopular")}
                          </span>
                        ) : null}
                      </div>
                      {plan.description ? <p className="mt-2 text-xs leading-5 text-muted">{plan.description}</p> : null}
                      <p className="mt-3 font-heading text-2xl font-extrabold tracking-[-0.02em] tabular">
                        {shown ?? t("marketing.contactUs")}
                        {shown ? (
                          <span className="ml-1 font-sans text-xs font-medium text-muted">
                            {cycle === "yearly" ? t("marketing.perYear") : t("marketing.perMonth")}
                          </span>
                        ) : null}
                      </p>
                      {cycle === "yearly" && free ? (
                        <p className="mt-1 text-xs font-semibold text-primary">{t("marketing.monthsFree", { count: free })}</p>
                      ) : null}
                      <ul className="mt-4 grid gap-1.5 border-t border-border pt-3 text-xs tabular text-foreground">
                        {cardLimits.map(([key, suffix]) => (
                          <li key={key}>
                            {t(`limit.${key}`)}: {formatLimit(plan.limits[key], t("common.unlimited"), suffix)}
                          </li>
                        ))}
                      </ul>
                    </div>
                    <Link
                      to={enterprisePlan ? "/contact" : `/signup?plan=${encodeURIComponent(plan.code)}`}
                      className="mt-5 inline-flex h-9 items-center justify-center rounded-md bg-primary px-3 text-center text-xs font-semibold text-primary-foreground"
                    >
                      {enterprisePlan ? t("marketing.talkToUs") : t("marketing.startTrialCta")}
                    </Link>
                  </article>
                );
              })}
            </div>

            <div className="mt-12">
              <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">02</p>
              <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em]">{t("marketing.compareTitle")}</h2>
            </div>

            <div className="mt-4 hidden md:block">
              <table className="w-full border-collapse text-left text-[13px]">
                <thead>
                  <tr className="border-b border-border">
                    <th className="sticky top-16 z-10 bg-card px-3 py-3 font-semibold">{t("marketing.compare")}</th>
                    {rows.map((plan) => (
                      <th key={plan.code} className="sticky top-16 z-10 bg-card px-3 py-3 font-heading font-bold">
                        {plan.name}
                        <div className="mt-0.5 font-sans text-xs font-normal tabular text-muted">
                          {priceLabel(plan, cycle) ?? t("marketing.contactUs")}
                        </div>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {COMPARISON_GROUPS.map((group) => (
                    <Fragment key={group.id}>
                      <tr>
                        <th colSpan={rows.length + 1} className="bg-secondary/40 px-3 py-2 text-xs font-semibold uppercase tracking-[0.08em] text-muted">
                          {t(`marketing.group.${group.id}`)}
                        </th>
                      </tr>
                      {group.features.map((key) => (
                        <tr key={key} className="border-b border-border">
                          <th className="px-3 py-2.5 font-normal text-foreground">{t(`feature.${key}`)}</th>
                          {rows.map((plan) => (
                            <td key={plan.code} className="px-3 py-2.5" data-plan={plan.code} data-feature={key}>
                              <FeatureMark included={plan.features.includes(key)} yes={yes} no={no} />
                            </td>
                          ))}
                        </tr>
                      ))}
                    </Fragment>
                  ))}
                </tbody>
              </table>
            </div>

            {active ? (
              <div className="mt-4 md:hidden">
                <div role="tablist" aria-label={t("marketing.compare")} className="flex flex-wrap gap-2">
                  {rows.map((plan) => (
                    <button
                      key={plan.code}
                      type="button"
                      role="tab"
                      aria-selected={active.code === plan.code}
                      onClick={() => setMobilePlan(plan.code)}
                      className={cn(
                        "h-8 rounded-md border px-3 text-xs font-semibold",
                        active.code === plan.code ? "border-primary bg-brand-50 text-primary" : "border-border bg-card text-foreground",
                      )}
                    >
                      {plan.name}
                    </button>
                  ))}
                </div>
                <p className="mt-3 font-heading text-lg font-bold">
                  {active.name}
                  <span className="ml-2 font-sans text-sm font-medium tabular text-muted">
                    {priceLabel(active, cycle) ?? t("marketing.contactUs")}
                  </span>
                </p>
                <div className="mt-3 rounded-lg border border-border bg-card">
                  {COMPARISON_GROUPS.map((group) => (
                    <div key={group.id}>
                      <h3 className="border-b border-border bg-secondary/40 px-3 py-2 text-xs font-semibold uppercase tracking-[0.08em] text-muted">
                        {t(`marketing.group.${group.id}`)}
                      </h3>
                      <ul>
                        {group.features.map((key) => (
                          <li key={key} className="flex items-center justify-between gap-3 border-b border-border px-3 py-2.5 text-[13px] last:border-b-0">
                            <span>{t(`feature.${key}`)}</span>
                            <span data-plan={active.code} data-feature={key}>
                              <FeatureMark included={active.features.includes(key)} yes={yes} no={no} />
                            </span>
                          </li>
                        ))}
                      </ul>
                    </div>
                  ))}
                </div>
              </div>
            ) : null}
          </>
        )}
      </section>

      <section className="border-t border-border bg-card">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
          <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">03</p>
          <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em]">{t("marketing.addonsTitle")}</h2>
          <p className="mt-2 max-w-[65ch] text-sm leading-6 text-muted">{t("marketing.addonsBody")}</p>
          <dl className="mt-6 grid gap-px overflow-hidden rounded-lg border border-border bg-border sm:grid-cols-2">
            <div className="bg-background px-4 py-3">
              <dt className="text-sm font-semibold">{t("marketing.addonSms")}</dt>
              <dd className="mt-1 text-sm tabular text-muted">
                {addonText(smsAddon)}
                {smsAddon.kind === "none" ? null : ` ${t("marketing.addonSmsUnit")}`}
              </dd>
            </div>
            <div className="bg-background px-4 py-3">
              <dt className="text-sm font-semibold">{t("marketing.addonStorage")}</dt>
              <dd className="mt-1 text-sm tabular text-muted">
                {addonText(storageAddon)}
                {storageAddon.kind === "none" ? null : ` ${t("marketing.addonStorageUnit")}`}
              </dd>
            </div>
            <div className="bg-background px-4 py-3">
              <dt className="text-sm font-semibold">{t("marketing.addonStudents")}</dt>
              <dd className="mt-1 text-sm text-muted">
                {enterprise ? t("marketing.addonStudentsOn", { plan: enterprise.name }) : t("marketing.contactUs")}
              </dd>
            </div>
            <div className="bg-background px-4 py-3">
              <dt className="text-sm font-semibold">{t("marketing.addonWhatsapp")}</dt>
              <dd className="mt-1 text-sm text-muted">
                {whatsappPlans.length > 0 ? t("marketing.addonWhatsappOn", { plans: whatsappPlans.join(", ") }) : t("marketing.contactUs")}
              </dd>
            </div>
            <div className="bg-background px-4 py-3 sm:col-span-2">
              <dt className="text-sm font-semibold">{t("marketing.addonPayhere")}</dt>
              <dd className="mt-1 text-sm tabular text-muted">{addonText(setupAddon)}</dd>
            </div>
          </dl>
        </div>
      </section>

      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
        <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">04</p>
        <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em]">{t("marketing.goodToKnowTitle")}</h2>
        <ul className="mt-6 grid max-w-3xl gap-3">
          {(["goodTrial", "goodSoftware", "goodSms"] as const).map((key) => (
            <li key={key} className="rounded-lg border border-border bg-card px-4 py-3 text-sm leading-6">
              {t(`marketing.${key}`)}
            </li>
          ))}
        </ul>
      </section>

      <section className="border-t border-border bg-card">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
          <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">05</p>
          <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em]">{t("marketing.faqTitle")}</h2>
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

      <section className="border-t border-border">
        <div className="mx-auto flex max-w-6xl flex-col gap-4 px-4 py-12 sm:flex-row sm:items-end sm:justify-between sm:px-6">
          <div>
            <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">06</p>
            <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em]">{t("marketing.finalCtaTitle")}</h2>
            <p className="mt-2 max-w-[65ch] text-sm leading-6 text-muted">{t("marketing.finalCtaBody")}</p>
          </div>
          <div className="flex flex-wrap gap-3">
            <Link to="/signup" className="inline-flex h-9 items-center rounded-md bg-primary px-3 text-sm font-semibold text-primary-foreground">
              {t("marketing.startTrialCta")}
            </Link>
            <Link to="/contact" className="inline-flex h-9 items-center rounded-md border border-border bg-card px-3 text-sm font-semibold text-foreground">
              {t("marketing.contactUs")}
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}

export default PricingPage;
