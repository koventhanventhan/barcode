import { useMutation, useQuery } from "@tanstack/react-query";
import { useEffect, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ProductName } from "@/config/brand";
import { useAuth } from "@/features/auth/AuthProvider";
import { ApiError, api } from "@/lib/api";
import { applyDocumentLanguage, LOCALES, type AppLocale } from "@/i18n/language";
import { formatMonthlyPrice } from "./price";

type PublicPlan = {
  code: string;
  name: string;
  description: string | null;
  priceMonthly: number | null;
  currency: string;
  features: string[];
  limits: Record<string, number | null>;
};

const limitKeys = ["Students", "EducationLevels", "Batches", "Subjects", "StaffAccounts", "AdminAccounts", "SmsSegmentsPerMonth", "CloudStorageGb"];
const featureKeys = ["DataExport", "BarcodeGenerator", "PerformanceReports", "OnlinePayments", "WhatsAppMessaging", "Timetable", "AutoBackup", "ParentPortal"];

const sections = [
  { id: "attendance", points: ["scan", "register", "board"] },
  { id: "fees", points: ["invoices", "receipts", "payhere"] },
  { id: "exams", points: ["marks", "ranks", "cards"] },
  { id: "messages", points: ["sms", "templates", "quiet"] },
  { id: "portal", points: ["children", "fees", "results"] },
  { id: "backups", points: ["export", "retention", "restore"] },
] as const;

const faqs = ["trial", "price", "sms", "portal", "data"] as const;

export function LandingPage() {
  const { t, i18n } = useTranslation();
  const auth = useAuth();
  const plans = useQuery({ queryKey: ["public-plans"], queryFn: () => api<PublicPlan[]>("/api/plans") });

  useEffect(() => {
    document.title = t("marketing.metaTitle", { product: ProductName });
  }, [t]);

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="border-b border-border bg-card">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-3 px-4 py-3">
          <a href="#top" className="text-sm font-semibold">
            {ProductName}
          </a>
          <nav aria-label={t("marketing.nav")} className="flex flex-wrap gap-3 text-[13px]">
            <a href="#product">{t("marketing.navProduct")}</a>
            <a href="#pricing">{t("marketing.navPricing")}</a>
            <a href="#demo">{t("marketing.navDemo")}</a>
            <a href="#faq">{t("marketing.navFaq")}</a>
          </nav>
          <div className="ml-auto flex items-center gap-2">
            <label className="sr-only" htmlFor="landing-lang">
              {t("shell.language")}
            </label>
            <select
              id="landing-lang"
              className="h-8 rounded-md border border-border bg-card px-2 text-[13px]"
              value={i18n.language}
              onChange={(event) => {
                const locale = event.target.value as AppLocale;
                void i18n.changeLanguage(locale);
                applyDocumentLanguage(locale);
              }}
            >
              {LOCALES.map((locale) => (
                <option key={locale} value={locale}>
                  {locale.toUpperCase()}
                </option>
              ))}
            </select>
            <Link className="text-[13px]" to="/login">
              {t("marketing.signIn")}
            </Link>
            <Link className="rounded-md bg-primary px-3 py-1.5 text-[13px] font-medium text-primary-foreground" to="/signup">
              {t("marketing.startTrial")}
            </Link>
          </div>
        </div>
      </header>
      <main id="top">
        <section className="mx-auto grid max-w-6xl items-center gap-8 px-4 py-10 lg:grid-cols-[1.1fr_0.9fr] lg:py-16">
          <div>
            <p className="text-[13px] font-medium text-primary">{t("marketing.eyebrow")}</p>
            <h1 className="mt-2 max-w-xl text-[32px] leading-tight font-semibold">{t("marketing.headline")}</h1>
            <p className="mt-3 max-w-xl text-base leading-6 text-muted">{t("marketing.subhead")}</p>
            <div className="mt-5 flex flex-wrap gap-2">
              <Link className="rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-foreground" to="/signup">
                {t("marketing.startTrial")}
              </Link>
              <a className="rounded-md border border-border px-3 py-2 text-sm" href="#demo">
                {t("marketing.requestDemo")}
              </a>
            </div>
          </div>
          <ProductShot />
        </section>
        <section id="product" className="border-t border-border">
          <div className="mx-auto grid max-w-6xl gap-8 px-4 py-10">
            <h2 className="text-2xl font-semibold">{t("marketing.productTitle")}</h2>
            {sections.map((section) => (
              <article key={section.id} className="grid gap-3 border-t border-border pt-6 md:grid-cols-[16rem_1fr]">
                <h3 className="text-base font-semibold">{t(`marketing.sections.${section.id}.title`)}</h3>
                <ul className="grid gap-2 text-sm">
                  {section.points.map((point) => (
                    <li key={point} className="border-l-2 border-primary pl-3">
                      {t(`marketing.sections.${section.id}.${point}`)}
                    </li>
                  ))}
                </ul>
              </article>
            ))}
          </div>
        </section>
        <section id="pricing" className="border-t border-border bg-card">
          <div className="mx-auto max-w-6xl px-4 py-10">
            <h2 className="text-2xl font-semibold">{t("marketing.pricingTitle")}</h2>
            <p className="mt-2 max-w-2xl text-sm text-muted">{t("marketing.pricingBody")}</p>
            {plans.isError ? (
              <p className="mt-4 text-sm" role="alert">
                {t("marketing.pricingError")}{" "}
                <button type="button" className="text-primary" onClick={() => void plans.refetch()}>
                  {t("common.retry")}
                </button>
              </p>
            ) : (
              <div className="mt-4 overflow-x-auto">
                <table className="w-full min-w-[40rem] border-collapse text-left text-[13px]">
                  <thead>
                    <tr className="border-b border-border">
                      <th className="px-2 py-2 font-medium">{t("marketing.compare")}</th>
                      {(plans.data ?? []).map((plan) => (
                        <th key={plan.code} className="px-2 py-2 font-semibold">
                          {plan.name}
                          <div className="mt-1 font-normal tabular-nums text-muted">
                            {formatMonthlyPrice(plan.priceMonthly, plan.currency) ?? t("marketing.contactUs")}
                          </div>
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {limitKeys.map((key) => (
                      <tr key={key} className="border-b border-border">
                        <th className="px-2 py-2 font-normal">{t(`limit.${key}`, { defaultValue: key })}</th>
                        {(plans.data ?? []).map((plan) => (
                          <td key={plan.code} className="px-2 py-2 tabular-nums">
                            {plan.limits[key] == null ? t("common.unlimited") : plan.limits[key]}
                          </td>
                        ))}
                      </tr>
                    ))}
                    {featureKeys.map((key) => (
                      <tr key={key} className="border-b border-border">
                        <th className="px-2 py-2 font-normal">{t(`feature.${key}`, { defaultValue: key })}</th>
                        {(plans.data ?? []).map((plan) => (
                          <td key={plan.code} className="px-2 py-2">
                            {plan.features.includes(key) ? t("common.yes") : t("common.no")}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
            <Link className="mt-4 inline-flex rounded-md bg-primary px-3 py-2 text-sm font-medium text-primary-foreground" to="/signup">
              {t("marketing.startTrial")}
            </Link>
          </div>
        </section>
        <section id="demo" className="border-t border-border">
          <div className="mx-auto grid max-w-6xl gap-8 px-4 py-10 md:grid-cols-2">
            <div>
              <h2 className="text-2xl font-semibold">{t("marketing.demoTitle")}</h2>
              <p className="mt-2 text-sm text-muted">{t("marketing.demoBody")}</p>
              <Button type="button" className="mt-4" disabled={auth.isReady === false} onClick={() => void openDemo(auth)}>
                {t("marketing.openDemo")}
              </Button>
            </div>
            <DemoForm />
          </div>
        </section>
        <section id="faq" className="border-t border-border bg-card">
          <div className="mx-auto max-w-6xl px-4 py-10">
            <h2 className="text-2xl font-semibold">{t("marketing.faqTitle")}</h2>
            <div className="mt-4 grid gap-2">
              {faqs.map((id) => (
                <details key={id} className="rounded-lg border border-border px-3 py-2">
                  <summary className="cursor-pointer text-sm font-medium">{t(`marketing.faq.${id}.q`)}</summary>
                  <p className="mt-2 text-[13px] leading-5 text-muted">{t(`marketing.faq.${id}.a`)}</p>
                </details>
              ))}
            </div>
          </div>
        </section>
        <section id="contact" className="border-t border-border">
          <div className="mx-auto max-w-6xl px-4 py-10">
            <h2 className="text-2xl font-semibold">{t("marketing.contactTitle")}</h2>
            <p className="mt-2 text-sm">
              <a className="text-primary" href="mailto:superadmin@eduflow.lk">
                superadmin@eduflow.lk
              </a>
            </p>
            <p className="mt-1 text-[13px] text-muted">{t("marketing.contactBody")}</p>
          </div>
        </section>
      </main>
      <footer className="border-t border-border">
        <div className="mx-auto flex max-w-6xl flex-wrap gap-3 px-4 py-4 text-[13px] text-muted">
          <span>{ProductName}</span>
          <a href="#pricing">{t("marketing.navPricing")}</a>
          <Link to="/signup">{t("marketing.startTrial")}</Link>
          <Link to="/login">{t("marketing.signIn")}</Link>
        </div>
      </footer>
    </div>
  );
}

function ProductShot() {
  const { t } = useTranslation();
  return (
    <figure className="rounded-lg border border-border bg-card p-3">
      <figcaption className="sr-only">{t("marketing.shotCaption")}</figcaption>
      <div className="grid grid-cols-[7rem_1fr] overflow-hidden rounded-md border border-border">
        <div className="border-r border-border bg-secondary p-2 text-[11px]">
          <p className="font-semibold">{ProductName}</p>
          <p className="mt-3">{t("nav.dashboard")}</p>
          <p className="mt-2">{t("nav.students")}</p>
          <p className="mt-2">{t("nav.attendance")}</p>
          <p className="mt-2">{t("nav.fees")}</p>
        </div>
        <div className="p-3">
          <p className="text-[11px] text-muted">{t("marketing.shotInstitute")}</p>
          <p className="text-sm font-semibold">{t("marketing.shotToday")}</p>
          <div className="mt-3 grid grid-cols-3 gap-2">
            <ShotStat label={t("marketing.shotAttendance")} value="92%" />
            <ShotStat label={t("marketing.shotFees")} value="LKR 128,500" />
            <ShotStat label={t("marketing.shotSms")} value="184 / 1,500" />
          </div>
          <table className="mt-3 w-full text-left text-[11px]">
            <thead>
              <tr className="border-b border-border text-muted">
                <th className="py-1 font-medium">{t("marketing.shotStudent")}</th>
                <th className="py-1 font-medium">{t("marketing.shotStatus")}</th>
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-border">
                <td className="py-1">Nimal Perera</td>
                <td className="py-1">Present</td>
              </tr>
              <tr>
                <td className="py-1">மீனா ராஜேந்திரன்</td>
                <td className="py-1">Late</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </figure>
  );
}

function ShotStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-border px-2 py-1">
      <p className="text-[10px] text-muted">{label}</p>
      <p className="text-[12px] font-semibold tabular-nums">{value}</p>
    </div>
  );
}

function DemoForm() {
  const { t } = useTranslation();
  const [sent, setSent] = useState("");
  const [error, setError] = useState("");
  const mutation = useMutation({
    mutationFn: (body: {
      instituteName: string;
      contactName: string;
      email: string;
      phone: string;
      city: string;
      approximateStudents: number | null;
      message: string;
    }) => api<{ message: string }>("/api/public/demo-requests", { method: "POST", body: JSON.stringify(body) }),
    onSuccess: (result) => {
      setError("");
      setSent(result.message);
    },
    onError: (reason) => {
      setSent("");
      setError(reason instanceof ApiError ? reason.message : t("errors.generic"));
    },
  });

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const students = String(data.get("approximateStudents") ?? "");
    mutation.mutate({
      instituteName: String(data.get("instituteName") ?? ""),
      contactName: String(data.get("contactName") ?? ""),
      email: String(data.get("email") ?? ""),
      phone: String(data.get("phone") ?? ""),
      city: String(data.get("city") ?? ""),
      approximateStudents: students ? Number(students) : null,
      message: String(data.get("message") ?? ""),
    });
  }

  return (
    <form className="grid gap-3 rounded-lg border border-border bg-card p-4" onSubmit={onSubmit}>
      <h3 className="text-sm font-semibold">{t("marketing.formTitle")}</h3>
      <Field name="instituteName" label={t("marketing.institute")} required />
      <Field name="contactName" label={t("marketing.contactName")} required />
      <Field name="email" label={t("marketing.email")} type="email" required />
      <Field name="phone" label={t("marketing.phone")} />
      <Field name="city" label={t("marketing.city")} />
      <Field name="approximateStudents" label={t("marketing.students")} type="number" />
      <label className="grid gap-1 text-[13px]">
        {t("marketing.message")}
        <textarea name="message" className="min-h-20 rounded-md border border-border px-2 py-1 text-sm" />
      </label>
      {error ? (
        <p className="text-sm text-danger" role="alert">
          {error}
        </p>
      ) : null}
      {sent ? <p className="text-sm text-success">{sent}</p> : null}
      <Button type="submit" disabled={mutation.isPending}>
        {mutation.isPending ? t("common.saving") : t("marketing.submitDemo")}
      </Button>
    </form>
  );
}

function Field({ name, label, type = "text", required = false }: { name: string; label: string; type?: string; required?: boolean }) {
  return (
    <label className="grid gap-1 text-[13px]">
      {label}
      <Input name={name} type={type} required={required} />
    </label>
  );
}

async function openDemo(auth: ReturnType<typeof useAuth>) {
  const result = await api<import("@/lib/types").AuthResponse>("/api/public/demo/login", { method: "POST", body: "{}" });
  await auth.adoptSession(result, "demo");
  window.location.assign("/app");
}
