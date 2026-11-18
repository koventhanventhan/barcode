import { ArrowRight } from "lucide-react";
import { t } from "@/config/strings";
import { Link } from "react-router-dom";
import { ProductName } from "@/config/brand";
import { Img } from "../Img";
import { MarketingMeta } from "../MarketingMeta";

export function AboutPage() {

  return (
    <>
      <MarketingMeta
        title={t("marketing.aboutTitle")}
        description={t("marketing.aboutLead")}
        path="/about"
      />

      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6 lg:py-14">
        <div className="grid items-center gap-10 lg:grid-cols-2">
          <div>
            <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">01 · {ProductName}</p>
            <h1 className="mt-2 font-heading text-3xl font-extrabold tracking-[-0.02em] sm:text-[44px]">
              {t("marketing.aboutTitle")}
            </h1>
            <p className="mt-4 max-w-[65ch] text-base leading-[1.6] text-muted">
              {t("marketing.aboutLead")}
            </p>
            <p className="mt-4 max-w-[65ch] text-sm leading-6 text-foreground">
              {t("marketing.aboutBody")}
            </p>
          </div>

          <Img
            src="/images/about-team.jpg"
            alt="EduFlow support and engineering team in Colombo"
            width={1200}
            height={800}
            priority
            position="center 30%"
            imgClassName="aspect-[3/2]"
          />
        </div>
      </section>

      <section className="border-t border-border bg-card">
        <div className="mx-auto grid max-w-6xl gap-6 px-4 py-10 sm:px-6 md:grid-cols-3">
          <div className="border-l-2 border-primary pl-4">
            <p className="text-xs font-semibold uppercase tracking-wider text-muted">{t("marketing.aboutOfficesLabel")}</p>
            <p className="mt-1 font-heading text-lg font-bold">Colombo 03 · Jaffna</p>
            <p className="mt-1 text-sm text-muted">{t("marketing.aboutOfficesNote")}</p>
          </div>
          <div className="border-l-2 border-primary pl-4">
            <p className="text-xs font-semibold uppercase tracking-wider text-muted">{t("marketing.contactTitle")}</p>
            <p className="mt-1 font-heading text-lg font-bold">
              <a href="mailto:superadmin@eduflow.lk" className="text-primary underline-offset-4 hover:underline">
                superadmin@eduflow.lk
              </a>
            </p>
            <p className="mt-1 text-sm text-muted">{t("marketing.contactBody")}</p>
          </div>
          <div className="flex items-center md:justify-end">
            <Link
              to="/contact"
              className="inline-flex h-10 items-center gap-2 rounded-md bg-primary px-4 text-sm font-semibold text-primary-foreground"
            >
              {t("marketing.contactTitle")}
              <ArrowRight className="h-4 w-4 stroke-[1.5]" aria-hidden="true" />
            </Link>
          </div>
        </div>
      </section>
    </>
  );
}

export default AboutPage;
