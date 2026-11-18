import { Play } from "lucide-react";
import { useState } from "react";
import { t } from "@/config/strings";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import type { AuthResponse } from "@/lib/types";
import { DemoRequestForm } from "../DemoRequestForm";
import { Img } from "../Img";
import { MarketingMeta } from "../MarketingMeta";

export function DemoPage() {
  const auth = useAuth();
  const [opening, setOpening] = useState(false);
  const [error, setError] = useState("");

  async function openDemo() {
    setOpening(true);
    setError("");
    try {
      const result = await api<AuthResponse>("/api/public/demo/login", { method: "POST", body: "{}" });
      await auth.adoptSession(result, "demo");
      window.location.assign("/app");
    } catch (err) {
      setError(err instanceof Error ? err.message : t("errors.generic"));
      setOpening(false);
    }
  }

  return (
    <>
      <MarketingMeta
        title={t("marketing.demoTitle")}
        description={t("marketing.demoBody")}
        path="/demo"
      />

      <section className="mx-auto max-w-6xl px-4 py-12 sm:px-6 lg:py-14">
        <div className="grid items-start gap-10 lg:grid-cols-[0.95fr_1.05fr]">
          <div>
            <p className="tabular text-xs font-semibold uppercase tracking-[0.12em] text-primary">01</p>
            <h1 className="mt-2 font-heading text-3xl font-extrabold tracking-[-0.02em] sm:text-[44px]">
              {t("marketing.demoTitle")}
            </h1>
            <p className="mt-4 max-w-[65ch] text-base leading-[1.6] text-muted">
              {t("marketing.demoBody")}
            </p>
            <div className="mt-6 flex flex-wrap items-center gap-3">
              <Button
                type="button"
                disabled={auth.isReady === false || opening}
                onClick={() => void openDemo()}
              >
                <Play className="mr-1.5 h-4 w-4 stroke-[1.5]" aria-hidden="true" />
                {t("marketing.openDemo")}
              </Button>
            </div>
            {error ? (
              <p className="mt-3 text-sm text-danger" role="alert">
                {error}
              </p>
            ) : null}

            <div className="mt-8">
              <Img
                src="/images/hero-tuition.jpg"
                alt="Sri Lankan mathematics batch at Royal Vidyalaya tuition class"
                width={1200}
                height={800}
                position="center 30%"
                imgClassName="aspect-[3/2]"
              />
            </div>
          </div>

          <DemoRequestForm />
        </div>
      </section>
    </>
  );
}

export default DemoPage;
