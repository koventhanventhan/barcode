import { ArrowRight, CheckCircle2, CreditCard, MessageSquare, QrCode } from "lucide-react";
import { t } from "@/config/strings";
import { Link } from "react-router-dom";
import { Img } from "../Img";
import { MarketingMeta } from "../MarketingMeta";
import { HeroProductCard } from "../Mockups";

export function HomePage() {

  const highlights = [
    {
      num: "01",
      slug: "attendance",
      icon: QrCode,
      title: t("marketing.pages.attendance.navTitle"),
      summary: t("marketing.homeHighlight1"),
    },
    {
      num: "02",
      slug: "fees",
      icon: CreditCard,
      title: t("marketing.pages.fees.navTitle"),
      summary: t("marketing.homeHighlight2"),
    },
    {
      num: "03",
      slug: "messaging",
      icon: MessageSquare,
      title: t("marketing.pages.messaging.navTitle"),
      summary: t("marketing.homeHighlight3"),
    },
  ];

  const stats = [
    { value: "4", label: t("marketing.statPlans") },
    { value: "EN · தமிழ் · සිංහල", label: t("marketing.statLangs") },
    { value: "< 300ms", label: t("marketing.statScan") },
    { value: "14 Days", label: t("marketing.statTrial") },
  ];

  return (
    <>
      <MarketingMeta
        title={t("marketing.metaTitle", { product: "Admino" })}
        description={t("marketing.subhead")}
        path="/"
      />

      {/* Block 1: Asymmetric split hero */}
      <section className="mx-auto grid max-w-6xl items-center gap-10 px-4 py-12 sm:px-6 lg:grid-cols-[1.05fr_0.95fr] lg:py-16">
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-primary">
            {t("marketing.eyebrow")}
          </p>
          <h1 className="mt-3 font-heading text-[34px] font-extrabold leading-[1.08] tracking-[-0.02em] text-foreground sm:text-[44px] lg:text-[56px]">
            {t("marketing.headline")}
          </h1>
          <p className="mt-4 max-w-[65ch] text-base leading-[1.6] text-muted sm:text-lg">
            {t("marketing.subhead")}
          </p>
          <div className="mt-7 flex flex-wrap items-center gap-3">
            <Link
              to="/signup"
              className="inline-flex h-11 items-center rounded-md bg-primary px-5 text-sm font-semibold text-primary-foreground transition-opacity hover:opacity-95"
            >
              {t("marketing.startFreeTrial")}
            </Link>
            <Link
              to="/demo"
              className="inline-flex h-11 items-center rounded-md border border-border bg-card px-5 text-sm font-medium text-foreground transition-colors hover:border-primary/40 hover:text-primary"
            >
              {t("marketing.requestDemo")}
            </Link>
          </div>
          <ul className="mt-7 grid gap-2 text-sm text-muted sm:grid-cols-3">
            {[t("marketing.trust1"), t("marketing.trust2"), t("marketing.trust3")].map((item) => (
              <li key={item} className="flex items-start gap-2">
                <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 stroke-[1.5] text-primary" aria-hidden="true" />
                <span>{item}</span>
              </li>
            ))}
          </ul>
        </div>

        <div className="relative pb-10 sm:pb-12">
          <Img
            src="/images/hero-classroom.jpg"
            alt="Colombo tuition institute lecture hall in warm morning light"
            width={1600}
            height={1067}
            priority
            position="center 35%"
          />
          <div className="mt-3 sm:absolute sm:bottom-0 sm:left-4 sm:right-8 sm:mt-0">
            <HeroProductCard />
          </div>
        </div>
      </section>

      {/* Block 2: 3 Highlights linking to feature pages */}
      <section className="border-t border-border bg-card">
        <div className="mx-auto max-w-6xl px-4 py-14 sm:px-6">
          <div className="flex flex-wrap items-end justify-between gap-4 border-b border-border pb-5">
            <div>
              <p className="text-xs font-semibold uppercase tracking-[0.12em] text-primary tabular">01</p>
              <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em] sm:text-[32px]">
                {t("marketing.productTitle")}
              </h2>
            </div>
            <Link
              to="/features"
              className="inline-flex items-center gap-1.5 text-sm font-semibold text-primary underline-offset-4 hover:underline"
            >
              {t("marketing.allFeatures")}
              <ArrowRight className="h-4 w-4 stroke-[1.5]" aria-hidden="true" />
            </Link>
          </div>

          <div className="mt-8 grid gap-6 md:grid-cols-3">
            {highlights.map((item) => (
              <article
                key={item.slug}
                className="flex flex-col justify-between rounded-lg border border-border bg-background p-6"
              >
                <div>
                  <div className="flex items-center justify-between">
                    <span className="inline-flex h-9 w-9 items-center justify-center rounded-md bg-brand-50 text-primary">
                      <item.icon className="h-4 w-4 stroke-[1.5]" aria-hidden="true" />
                    </span>
                    <span className="tabular text-xs font-semibold text-muted">{item.num}</span>
                  </div>
                  <h3 className="mt-4 font-heading text-xl font-bold tracking-[-0.01em]">{item.title}</h3>
                  <p className="mt-2 max-w-[65ch] text-sm leading-6 text-muted">{item.summary}</p>
                </div>
                <Link
                  to={`/features/${item.slug}`}
                  className="mt-5 inline-flex items-center gap-1.5 text-sm font-semibold text-primary underline-offset-4 hover:underline"
                >
                  {t("marketing.learnMore")}
                  <ArrowRight className="h-4 w-4 stroke-[1.5]" aria-hidden="true" />
                </Link>
              </article>
            ))}
          </div>
        </div>
      </section>

      {/* Block 3: Editorial stats band */}
      <section className="border-t border-border bg-background">
        <div className="mx-auto max-w-6xl px-4 py-12 sm:px-6">
          <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
            {stats.map((stat) => (
              <div key={stat.label} className="border-l-2 border-primary pl-4">
                <p className="font-heading text-2xl font-extrabold tracking-[-0.02em] text-foreground tabular">
                  {stat.value}
                </p>
                <p className="mt-1 text-sm text-muted">{stat.label}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* Block 4: Final CTA */}
      <section className="border-t border-border bg-card">
        <div className="mx-auto flex max-w-6xl flex-col items-start justify-between gap-6 px-4 py-12 sm:px-6 md:flex-row md:items-center">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.12em] text-primary tabular">02</p>
            <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em] sm:text-[32px]">
              {t("marketing.ctaTitle")}
            </h2>
            <p className="mt-2 max-w-[65ch] text-sm text-muted">{t("marketing.ctaBody")}</p>
          </div>
          <div className="flex flex-wrap gap-3">
            <Link
              to="/signup"
              className="inline-flex h-11 items-center rounded-md bg-primary px-5 text-sm font-semibold text-primary-foreground transition-opacity hover:opacity-95"
            >
              {t("marketing.startFreeTrial")}
            </Link>
            <Link
              to="/demo"
              className="inline-flex h-11 items-center rounded-md border border-border bg-background px-5 text-sm font-medium text-foreground hover:border-primary/40 hover:text-primary"
            >
              {t("marketing.openDemo")}
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}

export default HomePage;
