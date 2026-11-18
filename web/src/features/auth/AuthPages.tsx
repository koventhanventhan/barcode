import { zodResolver } from "@hookform/resolvers/zod";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { useForm } from "react-hook-form";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { FormField, fieldError } from "@/components/form-field";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ProductName } from "@/config/brand";
import { t } from "@/config/strings";
import { BrandLogo } from "@/features/branding/BrandLogo";
import { useBranding } from "@/features/branding/BrandingProvider";
import { ApiError, api } from "@/lib/api";
import { cn } from "@/lib/utils";
import { homeFor, useAuth } from "./AuthProvider";
import { passwordSchema } from "./password";

const BRAND_DEBOUNCE_MS = 400;

function useDebouncedBrandPreview(instituteCode: string | undefined) {
  const branding = useBranding();
  const initialRef = useRef(true);
  const previewInstitute = branding.previewInstitute;
  const hostSubdomain = branding.hostSubdomain;

  useEffect(() => {
    const normalized = (instituteCode ?? "").trim().toLowerCase();
    if (initialRef.current) {
      initialRef.current = false;
      if (normalized && normalized === hostSubdomain && previewInstitute) {
        previewInstitute(normalized);
      }
      return;
    }

    if (!previewInstitute) return;
    const timer = window.setTimeout(() => {
      previewInstitute(normalized);
    }, BRAND_DEBOUNCE_MS);
    return () => window.clearTimeout(timer);
  }, [instituteCode, hostSubdomain, previewInstitute]);
}

const loginSchema = z.object({
  institute: z.string().optional(),
  email: z.string().optional(),
  phone: z.string().optional(),
  password: z.string().min(1),
});

export function LoginPage() {
  const auth = useAuth();
  const branding = useBranding();
  const navigate = useNavigate();
  const [parentOff, setParentOff] = useState(false);
  const [mode, setMode] = useState<"staff" | "parent">("staff");
  const [formError, setFormError] = useState("");
  const form = useForm<z.infer<typeof loginSchema>>({
    resolver: zodResolver(loginSchema),
    defaultValues: { institute: branding.hostSubdomain || branding.institute || "", email: "", phone: "", password: "" },
  });
  const watchedInstitute = form.watch("institute");
  useDebouncedBrandPreview(watchedInstitute);

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
      <p className="mt-1 text-[13px] text-muted">{mode === "parent" ? t("auth.parentLoginDescription") : t("auth.loginDescription")}</p>
      <div className="mt-4 grid grid-cols-2 gap-2" role="group" aria-label={t("auth.signInAs")}>
        <Button type="button" variant={mode === "staff" ? "default" : "outline"} onClick={() => setMode("staff")}>
          {t("auth.staffMode")}
        </Button>
        <Button type="button" variant={mode === "parent" ? "default" : "outline"} onClick={() => setMode("parent")}>
          {t("auth.parentMode")}
        </Button>
      </div>
      <form
        className="mt-5 grid gap-3"
        onSubmit={form.handleSubmit(async (values) => {
          setFormError("");
          const institute = values.institute?.trim() ?? "";
          if (mode === "staff" && !z.string().email().safeParse(values.email).success) {
            form.setError("email", { message: t("auth.emailInvalid") });
            return;
          }
          if (mode === "parent") {
            if (!institute) {
              form.setError("institute", { message: t("auth.instituteRequired") });
              return;
            }
            if ((values.phone ?? "").replace(/\D/g, "").length < 9) {
              form.setError("phone", { message: t("auth.phoneInvalid") });
              return;
            }
          }
          branding.setInstitute(institute);
          try {
            const role =
              mode === "parent"
                ? await auth.login(values.email ?? "", values.password, institute, values.phone)
                : await auth.login(values.email ?? "", values.password, institute);
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
          <Input id="institute" autoComplete="organization" {...form.register("institute")} />
        </FormField>
        {mode === "parent" ? (
          <FormField label={t("auth.phone")} htmlFor="phone" error={fieldError(form, "phone")}>
            <Input id="phone" type="tel" autoComplete="tel" inputMode="tel" {...form.register("phone")} />
          </FormField>
        ) : (
          <FormField label={t("auth.email")} htmlFor="email" error={fieldError(form, "email")}>
            <Input id="email" type="email" autoComplete="username" {...form.register("email")} />
          </FormField>
        )}
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
        <Link className="text-[13px] text-primary" to="/signup">
          {t("marketing.startTrial")}
        </Link>
      </form>
    </AuthFrame>
  );
}

export function ForgotPasswordPage() {
  const [sent, setSent] = useState(false);
  const [formError, setFormError] = useState("");
  const branding = useBranding();
  const schema = z.object({
    institute: z.string().optional(),
    email: z.string().optional(),
    phone: z.string().optional(),
  });
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { institute: branding.hostSubdomain || branding.institute || "", email: "", phone: "" },
  });
  const watchedInstitute = form.watch("institute");
  useDebouncedBrandPreview(watchedInstitute);

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
            const phone = values.phone?.trim() ?? "";
            const email = values.email?.trim() ?? "";
            if (!email && phone.replace(/\D/g, "").length < 9) {
              setFormError(t("auth.phoneOrEmail"));
              return;
            }
            if (phone) branding.setInstitute(values.institute?.trim() ?? "");
            await api(
              "/api/auth/forgot-password",
              { method: "POST", body: JSON.stringify({ email: email || null, phone: phone || null }) },
              false,
            );
            setSent(true);
          } catch (error) {
            setFormError(error instanceof Error ? error.message : t("errors.generic"));
          }
        })}
      >
        <FormField label={t("auth.institute")} htmlFor="forgot-institute" error={fieldError(form, "institute")}>
          <Input id="forgot-institute" {...form.register("institute")} />
        </FormField>
        <FormField label={t("auth.email")} htmlFor="email" error={fieldError(form, "email")}>
          <Input id="email" type="email" {...form.register("email")} />
        </FormField>
        <FormField label={t("auth.phone")} hint={t("auth.phoneOrEmail")} htmlFor="forgot-phone" error={fieldError(form, "phone")}>
          <Input id="forgot-phone" type="tel" inputMode="tel" {...form.register("phone")} />
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
  const [params] = useSearchParams();
  const [done, setDone] = useState(false);
  const [formError, setFormError] = useState("");
  const phoneFromLink = params.get("phone") ?? "";
  const schema = z.object({
    email: z.string().optional(),
    phone: z.string().optional(),
    token: z.string().min(1),
    newPassword: passwordSchema(t("auth.passwordHint")),
  });
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      email: params.get("email") ?? "",
      phone: phoneFromLink,
      token: params.get("token") ?? "",
      newPassword: "",
    },
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
            await api(
              "/api/auth/reset-password",
              {
                method: "POST",
                body: JSON.stringify({
                  email: values.email || null,
                  phone: values.phone || null,
                  token: values.token,
                  newPassword: values.newPassword,
                }),
              },
              false,
            );
            setDone(true);
          } catch (error) {
            setFormError(error instanceof Error ? error.message : t("errors.generic"));
          }
        })}
      >
        {phoneFromLink ? (
          <FormField label={t("auth.phone")} htmlFor="reset-phone" error={fieldError(form, "phone")}>
            <Input id="reset-phone" type="tel" {...form.register("phone")} />
          </FormField>
        ) : (
          <FormField label={t("auth.email")} htmlFor="email" error={fieldError(form, "email")}>
            <Input id="email" type="email" {...form.register("email")} />
          </FormField>
        )}
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
  const auth = useAuth();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [formError, setFormError] = useState("");
  const phoneFromLink = params.get("phone") ?? "";
  const codeFromLink = params.get("code") ?? params.get("institute") ?? "";
  const branding = useBranding();
  const schema = z.object({
    institute: z.string().optional(),
    email: z.string().optional(),
    phone: z.string().optional(),
    token: z.string().min(1),
    fullName: z.string().min(2),
    password: passwordSchema(t("auth.passwordHint")),
  });
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      institute: codeFromLink || branding.hostSubdomain || branding.institute || "",
      email: params.get("email") ?? "",
      phone: phoneFromLink,
      token: params.get("token") ?? "",
      fullName: "",
      password: "",
    },
  });
  const watchedInstitute = form.watch("institute");
  useDebouncedBrandPreview(watchedInstitute);

  return (
    <AuthFrame>
      <h1 className="text-xl font-semibold">{t("auth.inviteTitle")}</h1>
      <p className="mt-1 text-[13px] text-muted">{t("auth.inviteDescription")}</p>
      <form
        className="mt-5 grid gap-3"
        onSubmit={form.handleSubmit(async (values) => {
          setFormError("");
          try {
            const institute = values.institute?.trim() ?? "";
            if (institute) branding.setInstitute(institute);
            const response = await api<{ accessToken: string; user: { role: string } }>(
              "/api/auth/accept-invite",
              {
                method: "POST",
                body: JSON.stringify({
                  email: values.email || null,
                  phone: values.phone || null,
                  token: values.token,
                  fullName: values.fullName,
                  password: values.password,
                }),
              },
              false,
            );
            await auth.login(values.email ?? "", values.password, institute, values.phone || undefined);
            navigate(homeFor(response.user.role));
          } catch (error) {
            setFormError(error instanceof Error ? error.message : t("errors.generic"));
          }
        })}
      >
        <FormField label={t("auth.institute")} hint={t("auth.instituteHint")} htmlFor="invite-institute" error={fieldError(form, "institute")}>
          <Input id="invite-institute" {...form.register("institute")} />
        </FormField>
        {phoneFromLink ? (
          <FormField label={t("auth.phone")} htmlFor="invite-phone" error={fieldError(form, "phone")}>
            <Input id="invite-phone" type="tel" {...form.register("phone")} />
          </FormField>
        ) : (
          <FormField label={t("auth.email")} htmlFor="email" error={fieldError(form, "email")}>
            <Input id="email" type="email" {...form.register("email")} />
          </FormField>
        )}
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

function AuthFrame({ children }: { children: ReactNode }) {
  const branding = useBranding();
  const name = branding.publicBranding?.displayName ?? branding.branding?.instituteDisplayName ?? ProductName;
  const tagline = branding.publicBranding?.tagline ?? branding.branding?.tagline ?? null;
  const logoUrl = branding.publicBranding?.logoUrl ?? branding.branding?.logoUrl ?? null;
  const hideProduct = branding.branding?.removeProductBranding ?? false;
  const brandSignature = `${name}|${logoUrl ?? ""}|${tagline ?? ""}`;
  const [fading, setFading] = useState(false);
  const prevSignature = useRef(brandSignature);

  useEffect(() => {
    if (prevSignature.current === brandSignature) return;
    prevSignature.current = brandSignature;
    const reduced = window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches;
    if (reduced) return;
    setFading(true);
    const timer = window.setTimeout(() => setFading(false), 150);
    return () => window.clearTimeout(timer);
  }, [brandSignature]);

  return (
    <div className="flex min-h-screen flex-col md:grid md:grid-cols-[minmax(260px,38%)_1fr]">
      <header className="flex items-center gap-3 bg-primary px-4 py-3 text-primary-foreground transition-colors duration-150 motion-reduce:transition-none md:hidden">
        <div className={cn("flex min-w-0 items-center gap-3 transition-opacity duration-150 motion-reduce:transition-none", fading ? "opacity-50" : "opacity-100")}>
          <BrandLogo logoUrl={logoUrl} displayName={name} size="md" inverted />
          <div className="min-w-0">
            <p className="truncate text-sm font-semibold tracking-tight">{name}</p>
            {tagline ? <p className="truncate text-xs opacity-85">{tagline}</p> : null}
          </div>
        </div>
      </header>
      <aside className="hidden flex-col justify-between bg-primary px-8 py-10 text-primary-foreground transition-colors duration-150 motion-reduce:transition-none md:flex md:min-h-screen">
        <div className={cn("transition-opacity duration-150 motion-reduce:transition-none", fading ? "opacity-50" : "opacity-100")}>
          <div className="mb-5">
            <BrandLogo logoUrl={logoUrl} displayName={name} size="lg" inverted />
          </div>
          <p className="text-xl font-semibold tracking-tight">{name}</p>
          {tagline ? <p className="mt-2 max-w-xs text-sm opacity-90">{tagline}</p> : null}
        </div>
        {hideProduct || name === ProductName ? null : <p className="mt-8 text-xs opacity-80">{ProductName}</p>}
      </aside>
      <main className="flex flex-1 items-center justify-center px-4 py-10">
        <div className="w-full max-w-sm">{children}</div>
      </main>
    </div>
  );
}
