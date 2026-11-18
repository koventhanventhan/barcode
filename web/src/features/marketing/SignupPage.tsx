import { zodResolver } from "@hookform/resolvers/zod";
import { useQuery } from "@tanstack/react-query";
import { useEffect, useMemo } from "react";
import { useForm } from "react-hook-form";
import { t } from "@/config/strings";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { FormField, fieldError } from "@/components/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ProductName } from "@/config/brand";
import { homeFor, useAuth } from "@/features/auth/AuthProvider";
import { passwordSchema } from "@/features/auth/password";
import { ApiError, api } from "@/lib/api";
import { formatMonthlyPrice } from "./price";

type PublicPlan = {
  code: string;
  name: string;
  priceMonthlyLkr: number | null;
  currency: string;
};

type SignupValues = {
  instituteName: string;
  subdomain: string;
  adminFullName: string;
  adminEmail: string;
  adminPassword: string;
  planCode: string;
  phone?: string;
};

export function SignupPage() {
  const auth = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const plans = useQuery({ queryKey: ["public-plans"], queryFn: () => api<PublicPlan[]>("/api/plans") });
  const schema = useMemo(
    () =>
      z.object({
        instituteName: z.string().trim().min(2).max(120),
        subdomain: z.string().trim().toLowerCase().regex(/^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/),
        adminFullName: z.string().trim().min(2),
        adminEmail: z.string().trim().email(),
        adminPassword: passwordSchema(t("auth.passwordHint")),
        planCode: z.string().min(1),
        phone: z.string().optional(),
      }),
    [],
  );
  const form = useForm<SignupValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      instituteName: "",
      subdomain: "",
      adminFullName: "",
      adminEmail: "",
      adminPassword: "",
      planCode: params.get("plan") || "Starter",
      phone: "",
    },
  });
  const subdomain = form.watch("subdomain");
  const availability = useQuery({
    queryKey: ["subdomain", subdomain],
    enabled: /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/.test(subdomain),
    queryFn: () => api<{ available: boolean; reason: string | null }>(`/api/public/subdomains/${encodeURIComponent(subdomain)}/availability`),
  });

  useEffect(() => {
    document.title = t("signup.metaTitle", { product: ProductName });
  }, []);

  return (
    <main className="mx-auto grid min-h-screen max-w-lg content-center gap-4 px-4 py-10">
      <div className="rounded-lg border border-border bg-card p-6 sm:p-8">
        <div>
          <Link className="font-heading text-sm font-bold text-primary underline-offset-4 hover:underline" to="/">
            &larr; {ProductName}
          </Link>
          <h1 className="mt-3 font-heading text-2xl font-extrabold tracking-[-0.02em]">{t("signup.title")}</h1>
          <p className="mt-1 text-sm leading-6 text-muted">{t("signup.description")}</p>
        </div>
        <form
          className="mt-5 grid gap-3"
          onSubmit={form.handleSubmit(async (values) => {
            try {
              if (availability.data && !availability.data.available) {
                form.setError("subdomain", { message: availability.data.reason ?? t("signup.taken") });
                return;
              }
              await api("/api/public/signup", { method: "POST", body: JSON.stringify(values) });
              const role = await auth.login(values.adminEmail, values.adminPassword, values.subdomain);
              navigate(homeFor(role));
            } catch (error) {
              form.setError("root", { message: error instanceof ApiError ? error.message : t("errors.generic") });
            }
          })}
        >
          <FormField label={t("marketing.institute")} htmlFor="instituteName" error={fieldError(form, "instituteName")}>
            <Input id="instituteName" {...form.register("instituteName")} />
          </FormField>
          <FormField
            label={t("signup.subdomain")}
            htmlFor="subdomain"
            hint={availability.data ? (availability.data.available ? t("signup.available") : availability.data.reason ?? t("signup.taken")) : t("signup.subdomainHint")}
            error={fieldError(form, "subdomain")}
          >
            <Input id="subdomain" autoCapitalize="none" {...form.register("subdomain")} />
          </FormField>
          <FormField label={t("signup.adminName")} htmlFor="adminFullName" error={fieldError(form, "adminFullName")}>
            <Input id="adminFullName" {...form.register("adminFullName")} />
          </FormField>
          <FormField label={t("marketing.email")} htmlFor="adminEmail" error={fieldError(form, "adminEmail")}>
            <Input id="adminEmail" type="email" autoComplete="email" {...form.register("adminEmail")} />
          </FormField>
          <FormField label={t("marketing.phone")} htmlFor="phone" error={fieldError(form, "phone")}>
            <Input id="phone" {...form.register("phone")} />
          </FormField>
          <FormField label={t("auth.password")} htmlFor="adminPassword" error={fieldError(form, "adminPassword")}>
            <Input id="adminPassword" type="password" autoComplete="new-password" {...form.register("adminPassword")} />
          </FormField>
          <FormField label={t("signup.plan")} htmlFor="planCode" error={fieldError(form, "planCode")}>
            <select id="planCode" className="h-9 rounded-md border border-border bg-card px-2 text-sm" {...form.register("planCode")}>
              {(plans.data ?? [{ code: "Starter", name: "Starter", priceMonthlyLkr: null, currency: "LKR" }]).map((plan) => (
                <option key={plan.code} value={plan.code}>
                  {plan.name} — {formatMonthlyPrice(plan.priceMonthlyLkr, plan.currency) ?? t("marketing.contactUs")}
                </option>
              ))}
            </select>
          </FormField>
          {form.formState.errors.root ? (
            <p className="text-sm text-danger" role="alert">
              {form.formState.errors.root.message}
            </p>
          ) : null}
          <Button type="submit" disabled={form.formState.isSubmitting}>
            {form.formState.isSubmitting ? t("common.saving") : t("marketing.startTrial")}
          </Button>
        </form>
        <p className="mt-4 text-[13px]">
          <Link className="text-primary underline-offset-4 hover:underline" to="/login">
            {t("signup.haveAccount")}
          </Link>
        </p>
      </div>
    </main>
  );
}
