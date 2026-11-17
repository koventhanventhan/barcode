import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { MarketingMeta } from "../MarketingMeta";

export function NotFoundPage() {
  const { t } = useTranslation();

  return (
    <>
      <MarketingMeta
        title={t("marketing.notFoundTitle")}
        description={t("marketing.notFoundBody")}
        path="/404"
      />
      <section className="mx-auto max-w-3xl px-4 py-20 sm:px-6">
        <p className="tabular text-xs font-semibold uppercase tracking-[0.14em] text-primary">404</p>
        <h1 className="mt-2 font-heading text-3xl font-extrabold tracking-[-0.02em] sm:text-[44px]">
          {t("marketing.notFoundTitle")}
        </h1>
        <p className="mt-3 max-w-[65ch] text-base leading-[1.6] text-muted">
          {t("marketing.notFoundBody")}
        </p>
        <div className="mt-6 flex flex-wrap gap-3">
          <Link
            to="/"
            className="inline-flex h-10 items-center rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground"
          >
            {t("marketing.navHome")}
          </Link>
          <Link
            to="/features"
            className="inline-flex h-10 items-center rounded-md border border-border bg-card px-4 text-sm font-medium text-foreground hover:border-primary/40"
          >
            {t("marketing.navFeatures")}
          </Link>
        </div>
      </section>
    </>
  );
}

export default NotFoundPage;
