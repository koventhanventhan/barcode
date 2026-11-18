import { ArrowRight, ChevronRight } from "lucide-react";
import { t } from "@/config/strings";
import { Link, useParams } from "react-router-dom";
import { cn } from "@/lib/utils";
import { findFeaturePage, type FeatureSlug } from "../catalog";
import { Img } from "../Img";
import { MarketingMeta } from "../MarketingMeta";
import { FeatureMockup } from "../Mockups";
import { NotFoundPage } from "./NotFoundPage";

const SLUG_SECTION_MAP: Record<FeatureSlug, { sectionKey: string; points: string[] }> = {
  attendance: { sectionKey: "attendance", points: ["scan", "register", "board"] },
  fees: { sectionKey: "fees", points: ["invoices", "receipts", "payhere"] },
  exams: { sectionKey: "exams", points: ["marks", "ranks", "cards"] },
  messaging: { sectionKey: "messages", points: ["sms", "templates", "quiet"] },
  "parent-portal": { sectionKey: "portal", points: ["children", "fees", "results"] },
  backup: { sectionKey: "backups", points: ["export", "retention", "restore"] },
};

export function FeatureDetailPage({ fixedSlug }: { fixedSlug?: FeatureSlug }) {
  const { slug: routeSlug } = useParams();
  const feature = findFeaturePage(fixedSlug ?? routeSlug);

  if (!feature) {
    return <NotFoundPage />;
  }

  const info = SLUG_SECTION_MAP[feature.slug];
  const next = findFeaturePage(feature.nextSlug)!;
  const reverseHero = Number(feature.number) % 2 === 0;

  return (
    <>
      <MarketingMeta
        title={t(`marketing.pages.${feature.slug}.title`)}
        description={t(`marketing.pages.${feature.slug}.lead`)}
        path={`/features/${feature.slug}`}
      />

      <section className="mx-auto max-w-6xl px-4 py-10 sm:px-6 lg:py-12">
        <nav aria-label={t("marketing.breadcrumbs")} className="flex flex-wrap items-center gap-1.5 text-xs text-muted">
          <Link to="/" className="underline-offset-4 hover:text-primary hover:underline">
            {t("marketing.navHome")}
          </Link>
          <ChevronRight className="h-3.5 w-3.5 stroke-[1.5]" aria-hidden="true" />
          <Link to="/features" className="underline-offset-4 hover:text-primary hover:underline">
            {t("marketing.navFeatures")}
          </Link>
          <ChevronRight className="h-3.5 w-3.5 stroke-[1.5]" aria-hidden="true" />
          <span className="font-medium text-foreground">{t(`marketing.pages.${feature.slug}.navTitle`)}</span>
        </nav>

        {/* Section 01: Asymmetric split (alternating image side) */}
        <div className="mt-6 grid items-center gap-10 lg:grid-cols-2">
          <div className={cn(reverseHero && "lg:order-2")}>
            <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">
              {feature.number} · {t(`marketing.pages.${feature.slug}.navTitle`)}
            </p>
            <h1 className="mt-2 font-heading text-3xl font-extrabold leading-[1.1] tracking-[-0.02em] sm:text-[44px]">
              {t(`marketing.pages.${feature.slug}.title`)}
            </h1>
            <p className="mt-4 max-w-[65ch] text-base leading-[1.6] text-muted">
              {t(`marketing.pages.${feature.slug}.lead`)}
            </p>
            <div className="mt-6 flex flex-wrap gap-3">
              <Link
                to="/signup"
                className="inline-flex h-10 items-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground"
              >
                {t("marketing.startFreeTrial")}
              </Link>
              <Link
                to="/demo"
                className="inline-flex h-10 items-center rounded-md border border-border bg-card px-4 text-sm font-medium text-foreground hover:border-primary/40 hover:text-primary"
              >
                {t("marketing.openDemo")}
              </Link>
            </div>
          </div>

          <div className={cn(reverseHero && "lg:order-1")}>
            <Img
              src={feature.image}
              alt={feature.imageAlt}
              width={1200}
              height={800}
              priority
              position={feature.imagePosition}
              imgClassName="aspect-[3/2]"
            />
          </div>
        </div>
      </section>

      {/* Section 02: Numbered capability rules + matching live product screen */}
      <section className="border-t border-border bg-card">
        <div className="mx-auto grid max-w-6xl items-center gap-10 px-4 py-12 sm:px-6 lg:grid-cols-[0.9fr_1.1fr]">
          <div>
            <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">02</p>
            <h2 className="mt-1 font-heading text-2xl font-bold tracking-[-0.02em]">
              {t(`marketing.sections.${info.sectionKey}.title`)}
            </h2>
            <ol className="mt-6 grid gap-5">
              {info.points.map((point, idx) => (
                <li key={point} className="border-l-2 border-primary pl-4">
                  <p className="tabular text-xs font-semibold text-muted">0{idx + 1}</p>
                  <p className="mt-1 max-w-[65ch] text-sm leading-6 text-foreground">
                    {t(`marketing.sections.${info.sectionKey}.${point}`)}
                  </p>
                </li>
              ))}
            </ol>
          </div>

          <div>
            <FeatureMockup slug={feature.slug} />
          </div>
        </div>
      </section>

      {/* Section 03: Next feature link */}
      <section className="border-t border-border bg-background">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-8 sm:px-6">
          <div>
            <p className="text-xs font-medium uppercase tracking-wider text-muted">{t("marketing.nextFeature")}</p>
            <p className="mt-1 font-heading text-lg font-bold">
              {next.number} · {t(`marketing.pages.${next.slug}.navTitle`)}
            </p>
          </div>
          <Link
            to={`/features/${next.slug}`}
            className="inline-flex h-10 items-center gap-2 rounded-md border border-border bg-card px-4 text-sm font-semibold text-primary hover:border-primary/40"
          >
            {t(`marketing.pages.${next.slug}.navTitle`)}
            <ArrowRight className="h-4 w-4 stroke-[1.5]" aria-hidden="true" />
          </Link>
        </div>
      </section>
    </>
  );
}

export default FeatureDetailPage;
