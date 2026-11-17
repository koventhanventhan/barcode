import { Mail, MapPin, Phone } from "lucide-react";
import { useTranslation } from "react-i18next";
import { DemoRequestForm } from "../DemoRequestForm";
import { MarketingMeta } from "../MarketingMeta";

export function ContactPage() {
  const { t } = useTranslation();

  return (
    <>
      <MarketingMeta
        title={t("marketing.contactTitle")}
        description={t("marketing.contactLead")}
        path="/contact"
      />

      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6 lg:py-14">
        <div className="grid items-start gap-10 lg:grid-cols-[0.9fr_1.1fr]">
          <div>
            <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">01</p>
            <h1 className="mt-2 font-heading text-3xl font-extrabold tracking-[-0.02em] sm:text-[44px]">
              {t("marketing.contactTitle")}
            </h1>
            <p className="mt-4 max-w-[65ch] text-base leading-[1.6] text-muted">
              {t("marketing.contactLead")}
            </p>

            <dl className="mt-8 grid gap-5 border-t border-border pt-6 text-sm">
              <div className="flex items-start gap-3">
                <Mail className="mt-0.5 h-4 w-4 shrink-0 stroke-[1.5] text-primary" aria-hidden="true" />
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wider text-muted">{t("marketing.email")}</dt>
                  <dd className="mt-0.5 font-medium">
                    <a href="mailto:superadmin@eduflow.lk" className="text-primary underline-offset-4 hover:underline">
                      superadmin@eduflow.lk
                    </a>
                  </dd>
                </div>
              </div>
              <div className="flex items-start gap-3">
                <Phone className="mt-0.5 h-4 w-4 shrink-0 stroke-[1.5] text-primary" aria-hidden="true" />
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wider text-muted">{t("marketing.phone")}</dt>
                  <dd className="mt-0.5 font-medium tabular">+94 11 234 5678</dd>
                </div>
              </div>
              <div className="flex items-start gap-3">
                <MapPin className="mt-0.5 h-4 w-4 shrink-0 stroke-[1.5] text-primary" aria-hidden="true" />
                <div>
                  <dt className="text-xs font-semibold uppercase tracking-wider text-muted">{t("marketing.aboutOfficesLabel")}</dt>
                  <dd className="mt-0.5 text-muted">Colombo 03 · Jaffna</dd>
                </div>
              </div>
            </dl>
          </div>

          <DemoRequestForm title={t("marketing.contactFormTitle")} submitLabel={t("marketing.submitDemo")} />
        </div>
      </section>
    </>
  );
}

export default ContactPage;
