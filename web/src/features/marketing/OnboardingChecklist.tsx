import { useQuery } from "@tanstack/react-query";
import { t } from "@/config/strings";
import { Link } from "react-router-dom";
import { api } from "@/lib/api";

type OnboardingStep = { key: string; title: string; done: boolean; href: string };
type OnboardingChecklistDto = { steps: OnboardingStep[]; completed: number; total: number };

export function OnboardingChecklist() {
  const checklist = useQuery({
    queryKey: ["onboarding"],
    queryFn: () => api<OnboardingChecklistDto>("/api/onboarding"),
  });
  const data = checklist.data;
  if (!data || data.completed >= data.total) return null;

  return (
    <section className="mb-4 rounded-lg border border-border bg-card p-4">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="text-sm font-semibold">{t("onboarding.title")}</h2>
        <p className="text-[13px] tabular-nums text-muted">
          {t("onboarding.progress", { completed: data.completed, total: data.total })}
        </p>
      </div>
      <ol className="mt-3 grid gap-2">
        {data.steps.map((step, index) => (
          <li key={step.key} className="flex items-center justify-between gap-3 text-[13px]">
            <span>
              <span className="mr-2 tabular-nums text-muted">{index + 1}.</span>
              <span className={step.done ? "text-muted line-through" : ""}>{t(`onboarding.steps.${step.key}`, { defaultValue: step.title })}</span>
            </span>
            {step.done ? (
              <span className="text-success">{t("onboarding.done")}</span>
            ) : (
              <Link className="font-medium text-primary" to={step.href}>
                {t("onboarding.open")}
              </Link>
            )}
          </li>
        ))}
      </ol>
    </section>
  );
}
