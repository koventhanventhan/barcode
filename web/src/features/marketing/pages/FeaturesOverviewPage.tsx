import { ArrowRight } from "lucide-react";
import { t } from "@/config/strings";
import { Link } from "react-router-dom";
import { FEATURE_PAGES } from "../catalog";
import { Img } from "../Img";
import { MarketingMeta } from "../MarketingMeta";

export function FeaturesOverviewPage() {

  return (
    <>
      <MarketingMeta
        title={t("marketing.featuresOverviewTitle")}
        description={t("marketing.featuresOverviewBody")}
        path="/features"
      />

      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6 lg:py-14">
        <p className="text-xs font-semibold uppercase tracking-[0.12em] text-primary">
          {t("marketing.modulesEyebrow")}
        </p>
        <h1 className="mt-2 font-heading text-3xl font-extrabold tracking-[-0.02em] sm:text-[44px] sm:leading-[1.12]">
          {t("marketing.featuresOverviewTitle")}
        </h1>
        <p className="mt-3 max-w-[65ch] text-base leading-[1.6] text-muted">
          {t("marketing.featuresOverviewBody")}
        </p>

        <div className="mt-10 grid gap-6 md:grid-cols-2 lg:grid-cols-3">
          {FEATURE_PAGES.map((feature) => (
            <article
              key={feature.slug}
              className="flex flex-col justify-between overflow-hidden rounded-lg border border-border bg-card"
            >
              <div>
                <Img
                  src={feature.image}
                  alt={feature.imageAlt}
                  width={1200}
                  height={800}
                  position={feature.imagePosition}
                  className="rounded-none border-0 border-b border-border"
                  imgClassName="aspect-[3/2]"
                />
                <div className="p-5">
                  <p className="tabular text-xs font-semibold text-primary">{feature.number}</p>
                  <h2 className="mt-1 font-heading text-xl font-bold tracking-[-0.01em]">
                    {t(`marketing.pages.${feature.slug}.navTitle`)}
                  </h2>
                  <p className="mt-2 text-sm leading-6 text-muted">
                    {t(`marketing.pages.${feature.slug}.navSummary`)}
                  </p>
                </div>
              </div>
              <div className="border-t border-border px-5 py-3.5">
                <Link
                  to={`/features/${feature.slug}`}
                  className="inline-flex items-center gap-1.5 text-sm font-semibold text-primary underline-offset-4 hover:underline"
                >
                  {t("marketing.learnMore")}
                  <ArrowRight className="h-4 w-4 stroke-[1.5]" aria-hidden="true" />
                </Link>
              </div>
            </article>
          ))}
        </div>
      </section>
    </>
  );
}

export default FeaturesOverviewPage;
