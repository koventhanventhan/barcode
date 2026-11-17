import { zodResolver } from "@hookform/resolvers/zod";
import { useState, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { useTranslation } from "react-i18next";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { FormField, fieldError } from "@/components/form-field";
import { ProductName } from "@/config/brand";
import { useBranding } from "@/features/branding/BrandingProvider";
import { useToast } from "@/components/toast";
import { ApiError, api } from "@/lib/api";
import { homeFor, useAuth } from "./AuthProvider";
import { passwordSchema } from "./password";

const loginSchema = z.object({
  institute: z.string().optional(),
  email: z.string().email(),
  password: z.string().min(1),
});

export function LoginPage() {
  const { t } = useTranslation();
  const auth = useAuth();
  const branding = useBranding();
  const navigate = useNavigate();
  const [parentOff, setParentOff] = useState(false);
  const [formError, setFormError] = useState("");
  const form = useForm<z.infer<typeof loginSchema>>({
    resolver: zodResolver(loginSchema),
    defaultValues: { institute: branding.institute ?? "", email: "", password: "" },
  });

  if (parentOff) {
    return (
      <AuthFrame>
        <h1 className="text-xl font-semibold">{t("auth.parentOffTitle")}</h1>
        <p className="mt-2 text-sm text-muted">{t("auth.parentOffBody")}</p>
        <Button className="mt-4" variant="outline" onClick={() => setParentOff(false)}>
          {t("auth.backToLogin")}
        </Button>
      </AuthFrame>
    );
  }

  return (
    <AuthFrame>
      <h1 className="text-xl font-semibold">{t("auth.loginTitle")}</h1>
      <p className="mt-1 text-[13px] text-muted">{t("auth.loginDescription")}</p>
      <form
        className="mt-5 grid gap-3"
        onSubmit={form.handleSubmit(async (values) => {
          setFormError("");
          const institute = values.institute?.trim() ?? "";
          branding.setInstitute(institute);
          try {
            const role = await auth.login(values.email, values.password, institute);
            navigate(homeFor(role));
          } catch (error) {
            if (error instanceof ApiError && error.feature === "ParentPortal") {
              setParentOff(true);
              return;
            }
            setFormError(error instanceof Error ? error.message : t("errors.generic"));
          }
        })}
      >
        <FormField label={t("auth.institute")} hint={t("auth.instituteHint")} htmlFor="institute" error={fieldError(form, "institute")}>
          <Input
            id="institute"
            autoComplete="organization"
            {...form.register("institute", {
              onBlur: (event) => branding.setInstitute(event.target.value),
            })}
          />
        </FormField>
        <FormField label={t("auth.email")} htmlFor="email" error={fieldError(form, "email")}>
          <Input id="email" type="email" autoComplete="username" {...form.register("email")} />
        </FormField>
        <FormField label={t("auth.password")} htmlFor="password" error={fieldError(form, "password")}>
          <Input id="password" type="password" autoComplete="current-password" {...form.register("password")} />
        </FormField>
        {formError ? (
          <p className="text-sm text-danger" role="alert">
            {formError}
          </p>
        ) : null}
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting ? t("auth.signingIn") : t("auth.signIn")}
        </Button>
        <Link className="text-[13px] text-primary" to="/forgot-password">
          {t("auth.forgot")}
        </Link>
      </form>
    </AuthFrame>
  );
}

export function ForgotPasswordPage() {
  const { t } = useTranslation();
  const [sent, setSent] = useState(false);
  const [formError, setFormError] = useState("");
  const schema = z.object({ email: z.string().email() });
  const form = useForm<z.infer<typeof schema>>({ resolver: zodResolver(schema), defaultValues: { email: "" } });

  return (
    <AuthFrame>
      <h1 className="text-xl font-semibold">{t("auth.forgotTitle")}</h1>
      <p className="mt-1 text-[13px] text-muted">{t("auth.forgotDescription")}</p>
      {sent ? <p className="mt-4 text-sm">{t("auth.sent")}</p> : null}
      <form
        className="mt-5 grid gap-3"
        onSubmit={form.handleSubmit(async (values) => {
          setFormError("");
          try {
            await api("/api/auth/forgot-password", { method: "POST", body: JSON.stringify(values) }, false);
            setSent(true);
          } catch (error) {
            setFormError(error instanceof Error ? error.message : t("errors.generic"));
          }
        })}
      >
        <FormField label={t("auth.email")} htmlFor="email" error={fieldError(form, "email")}>
          <Input id="email" type="email" {...form.register("email")} />
        </FormField>
        {formError ? <p className="text-sm text-danger">{formError}</p> : null}
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {t("auth.sendReset")}
        </Button>
        <Link className="text-[13px] text-primary" to="/reset-password">
          {t("auth.resetTitle")}
        </Link>
        <Link className="text-[13px] text-muted" to="/login">
          {t("auth.backToLogin")}
        </Link>
      </form>
    </AuthFrame>
  );
}

export function ResetPasswordPage() {
  const { t } = useTranslation();
  const [params] = useSearchParams();
  const [done, setDone] = useState(false);
  const [formError, setFormError] = useState("");
  const schema = z.object({
    email: z.string().email(),
    token: z.string().min(1),
    newPassword: passwordSchema(t("auth.passwordHint")),
  });
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { email: params.get("email") ?? "", token: params.get("token") ?? "", newPassword: "" },
  });

  return (
    <AuthFrame>
      <h1 className="text-xl font-semibold">{t("auth.resetTitle")}</h1>
      <p className="mt-1 text-[13px] text-muted">{t("auth.resetDescription")}</p>
      {done ? <p className="mt-4 text-sm">{t("auth.resetDone")}</p> : null}
      <form
        className="mt-5 grid gap-3"
        onSubmit={form.handleSubmit(async (values) => {
          setFormError("");
          try {
            await api("/api/auth/reset-password", { method: "POST", body: JSON.stringify(values) }, false);
            setDone(true);
          } catch (error) {
            setFormError(error instanceof Error ? error.message : t("errors.generic"));
          }
        })}
      >
        <FormField label={t("auth.email")} htmlFor="email" error={fieldError(form, "email")}>
          <Input id="email" type="email" {...form.register("email")} />
        </FormField>
        <FormField label={t("auth.token")} htmlFor="token" error={fieldError(form, "token")}>
          <Input id="token" {...form.register("token")} />
        </FormField>
        <FormField label={t("auth.newPassword")} hint={t("auth.passwordHint")} htmlFor="newPassword" error={fieldError(form, "newPassword")}>
          <Input id="newPassword" type="password" {...form.register("newPassword")} />
        </FormField>
        {formError ? <p className="text-sm text-danger">{formError}</p> : null}
        <Button type="submit">{t("auth.resetAction")}</Button>
        <Link className="text-[13px] text-muted" to="/login">
          {t("auth.backToLogin")}
        </Link>
      </form>
    </AuthFrame>
  );
}

export function AcceptInvitePage() {
  const { t } = useTranslation();
  const auth = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [formError, setFormError] = useState("");
  const schema = z.object({
    email: z.string().email(),
    token: z.string().min(1),
    fullName: z.string().min(2),
    password: passwordSchema(t("auth.passwordHint")),
  });
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: params.get("email") ?? "",
      token: params.get("token") ?? "",
      fullName: "",
      password: "",
    },
  });

  return (
    <AuthFrame>
      <h1 className="text-xl font-semibold">{t("auth.inviteTitle")}</h1>
      <p className="mt-1 text-[13px] text-muted">{t("auth.inviteDescription")}</p>
      <form
        className="mt-5 grid gap-3"
        onSubmit={form.handleSubmit(async (values) => {
          setFormError("");
          try {
            const response = await api<{ accessToken: string; user: { role: string } }>(
              "/api/auth/accept-invite",
              { method: "POST", body: JSON.stringify(values) },
              false,
            );
            await auth.login(values.email, values.password, "");
            navigate(homeFor(response.user.role));
          } catch (error) {
            setFormError(error instanceof Error ? error.message : t("errors.generic"));
          }
        })}
      >
        <FormField label={t("auth.email")} htmlFor="email" error={fieldError(form, "email")}>
          <Input id="email" type="email" {...form.register("email")} />
        </FormField>
        <FormField label={t("auth.fullName")} htmlFor="fullName" error={fieldError(form, "fullName")}>
          <Input id="fullName" {...form.register("fullName")} />
        </FormField>
        <FormField label={t("auth.token")} htmlFor="token" error={fieldError(form, "token")}>
          <Input id="token" {...form.register("token")} />
        </FormField>
        <FormField label={t("auth.password")} hint={t("auth.passwordHint")} htmlFor="password" error={fieldError(form, "password")}>
          <Input id="password" type="password" {...form.register("password")} />
        </FormField>
        {formError ? <p className="text-sm text-danger">{formError}</p> : null}
        <Button type="submit">{t("auth.accept")}</Button>
      </form>
    </AuthFrame>
  );
}

export function ChangePasswordPage() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const [formError, setFormError] = useState("");
  const schema = z
    .object({
      currentPassword: z.string().min(1),
      newPassword: passwordSchema(t("auth.passwordHint")),
      confirmPassword: z.string(),
    })
    .refine((values) => values.newPassword === values.confirmPassword, {
      message: t("auth.passwordMismatch"),
      path: ["confirmPassword"],
    });
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { currentPassword: "", newPassword: "", confirmPassword: "" },
  });

  return (
    <section className="mx-auto max-w-lg">
      <h1 className="text-xl font-semibold">{t("auth.changeTitle")}</h1>
      <p className="mt-1 text-[13px] text-muted">{t("auth.changeDescription")}</p>
      <form
        className="mt-5 grid gap-3"
        onSubmit={form.handleSubmit(async (values) => {
          setFormError("");
          try {
            await api("/api/auth/change-password", {
              method: "POST",
              body: JSON.stringify({ currentPassword: values.currentPassword, newPassword: values.newPassword }),
            });
            toast.success(t("auth.changed"));
            if (user) navigate(homeFor(user.role));
          } catch (error) {
            setFormError(error instanceof Error ? error.message : t("errors.generic"));
          }
        })}
      >
        <FormField label={t("auth.currentPassword")} htmlFor="currentPassword" error={fieldError(form, "currentPassword")}>
          <Input id="currentPassword" type="password" {...form.register("currentPassword")} />
        </FormField>
        <FormField label={t("auth.newPassword")} hint={t("auth.passwordHint")} htmlFor="newPassword" error={fieldError(form, "newPassword")}>
          <Input id="newPassword" type="password" {...form.register("newPassword")} />
        </FormField>
        <FormField label={t("auth.confirmPassword")} htmlFor="confirmPassword" error={fieldError(form, "confirmPassword")}>
          <Input id="confirmPassword" type="password" {...form.register("confirmPassword")} />
        </FormField>
        {formError ? <p className="text-sm text-danger">{formError}</p> : null}
        <Button type="submit">{t("auth.changeAction")}</Button>
      </form>
    </section>
  );
}

function LanguageSelect() {
  const { t, i18n } = useTranslation();
  return (
    <label className="mb-4 flex items-center justify-end gap-2 text-xs text-muted">
      <span>{t("shell.language")}</span>
      <select
        aria-label={t("shell.language")}
        className="h-8 rounded-md border border-border bg-card px-2"
        value={i18n.resolvedLanguage ?? i18n.language}
        onChange={(event) => void i18n.changeLanguage(event.target.value)}
      >
        <option value="en">{t("lang.en")}</option>
        <option value="ta">{t("lang.ta")}</option>
        <option value="si">{t("lang.si")}</option>
      </select>
    </label>
  );
}

function AuthFrame({ children }: { children: ReactNode }) {
  const branding = useBranding();
  const name = branding.branding?.instituteDisplayName ?? ProductName;
  const hideProduct = branding.branding?.removeProductBranding;
  return (
    <div className="grid min-h-screen md:grid-cols-[minmax(240px,38%)_1fr]">
      <aside
        className="flex flex-col justify-between bg-primary px-6 py-8 text-primary-foreground md:min-h-screen"
        style={
          branding.branding?.loginBackgroundUrl
            ? {
                backgroundImage: `linear-gradient(to bottom, color-mix(in srgb, var(--primary) 78%, transparent), var(--primary)), url(${branding.branding.loginBackgroundUrl})`,
                backgroundSize: "cover",
                backgroundPosition: "center",
              }
            : undefined
        }
      >
        <div>
          {branding.branding?.logoUrl ? (
            <img src={branding.branding.logoUrl} alt="" className="mb-4 h-10 w-auto" />
          ) : null}
          <p className="text-lg font-semibold tracking-tight">{name}</p>
          {branding.branding?.tagline ? <p className="mt-2 max-w-xs text-sm opacity-90">{branding.branding.tagline}</p> : null}
        </div>
        {hideProduct || name === ProductName ? null : <p className="mt-8 text-xs opacity-80">{ProductName}</p>}
      </aside>
      <main className="flex items-center justify-center px-4 py-10">
        <div className="w-full max-w-sm">
          <LanguageSelect />
          {children}
        </div>
      </main>
    </div>
  );
}
