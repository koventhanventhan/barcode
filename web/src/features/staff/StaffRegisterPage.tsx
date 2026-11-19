import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery } from "@tanstack/react-query";
import { CheckCircle2 } from "lucide-react";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { Link, useSearchParams } from "react-router-dom";
import { z } from "zod";
import { fieldError, FormField } from "@/components/form-field";
import { PageSkeleton } from "@/components/skeletons";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { ProductName } from "@/config/brand";
import { t } from "@/config/strings";
import { fieldClass } from "@/features/academics/shared";
import { passwordSchema } from "@/features/auth/password";
import { BrandLogo } from "@/features/branding/BrandLogo";
import {
  getPublicStaffRegistrationInfo,
  submitPublicStaffRegistration,
} from "@/features/staff/registrationApi";
import { ApiError } from "@/lib/api";

export function StaffRegisterPage() {
  const [params] = useSearchParams();
  const token = (params.get("token") ?? "").trim();
  const [submitted, setSubmitted] = useState(false);
  const [formError, setFormError] = useState("");

  const info = useQuery({
    queryKey: ["public-staff-registration", token],
    enabled: Boolean(token),
    retry: false,
    queryFn: () => getPublicStaffRegistrationInfo(token),
  });

  const schema = z
    .object({
      fullName: z.string().trim().min(2).max(200),
      email: z.string().trim().email(),
      phone: z.string().trim().max(32).optional(),
      requestedRoleType: z.enum(["Staff", "Teacher"]),
      branchId: z.string().optional(),
      notes: z.string().trim().max(500).optional(),
      password: passwordSchema(t("auth.passwordHint")),
      confirmPassword: z.string(),
    })
    .refine((v) => v.password === v.confirmPassword, {
      message: t("auth.passwordMismatch"),
      path: ["confirmPassword"],
    });

  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: "",
      email: "",
      phone: "",
      requestedRoleType: "Staff",
      branchId: "",
      notes: "",
      password: "",
      confirmPassword: "",
    },
  });

  const submit = useMutation({
    mutationFn: (values: z.infer<typeof schema>) =>
      submitPublicStaffRegistration(token, {
        fullName: values.fullName,
        email: values.email,
        password: values.password,
        phone: values.phone?.trim() ? values.phone.trim() : null,
        requestedRoleType: values.requestedRoleType,
        branchId: values.branchId || info.data?.defaultBranchId || null,
        notes: values.notes?.trim() ? values.notes.trim() : null,
      }),
    onSuccess: () => {
      setFormError("");
      setSubmitted(true);
    },
    onError: (err) => {
      setFormError(err instanceof ApiError ? err.message : t("errors.generic"));
    },
  });

  if (!token || info.isError) {
    return (
      <main className="mx-auto flex min-h-screen max-w-md items-center px-4 py-10">
        <section className="w-full rounded-lg border border-border bg-card p-6">
          <h1 className="text-lg font-semibold">{t("staff.publicTitle")}</h1>
          <p className="mt-2 text-sm text-muted">{t("staff.publicInvalidLink")}</p>
          <Link className="mt-4 inline-block text-sm text-primary hover:underline" to="/login">
            {t("auth.backToLogin")}
          </Link>
        </section>
      </main>
    );
  }

  if (info.isLoading) {
    return (
      <main className="mx-auto max-w-md px-4 py-10">
        <PageSkeleton />
      </main>
    );
  }

  const institute = info.data;
  const displayName = institute?.instituteName || ProductName;

  return (
    <div className="min-h-screen bg-background px-4 py-10">
      <main className="mx-auto w-full max-w-md rounded-lg border border-border bg-card overflow-hidden">
        <header
          className="flex items-center gap-3 px-6 py-4 text-primary-foreground"
          style={{ backgroundColor: institute?.primaryColor || "var(--primary)" }}
        >
          <BrandLogo logoUrl={institute?.logoUrl} displayName={displayName} size="md" inverted />
          <div className="min-w-0">
            <p className="truncate text-base font-semibold">{displayName}</p>
            {institute?.tagline ? (
              <p className="truncate text-xs opacity-90">{institute.tagline}</p>
            ) : null}
          </div>
        </header>

        <div className="p-6">
          {submitted ? (
            <div className="grid gap-3">
              <div className="flex items-center gap-2 text-success">
                <CheckCircle2 className="h-5 w-5" aria-hidden="true" />
                <h1 className="text-lg font-semibold">{t("staff.publicSubmittedTitle")}</h1>
              </div>
              <p className="text-sm text-muted">{t("staff.publicSubmittedBody")}</p>
              <Link className="mt-2 inline-block text-sm font-medium text-primary hover:underline" to="/login">
                {t("auth.backToLogin")}
              </Link>
            </div>
          ) : (
            <>
              <h1 className="text-xl font-semibold">{t("staff.publicTitle")}</h1>
              <p className="mt-1 text-[13px] text-muted">{t("staff.publicDescription")}</p>

              <form
                className="mt-5 grid gap-3"
                onSubmit={form.handleSubmit((values) => {
                  setFormError("");
                  submit.mutate(values);
                })}
              >
                <FormField label={t("staff.name")} htmlFor="reg-fullname" error={fieldError(form, "fullName")}>
                  <Input id="reg-fullname" {...form.register("fullName")} />
                </FormField>

                <FormField label={t("staff.email")} htmlFor="reg-email" error={fieldError(form, "email")}>
                  <Input id="reg-email" type="email" {...form.register("email")} />
                </FormField>

                <FormField label={t("staff.phone")} htmlFor="reg-phone" error={fieldError(form, "phone")}>
                  <Input id="reg-phone" type="tel" inputMode="tel" {...form.register("phone")} />
                </FormField>

                <FormField
                  label={t("staff.requestedRole")}
                  htmlFor="reg-role"
                  error={fieldError(form, "requestedRoleType")}
                >
                  <select id="reg-role" className={fieldClass} {...form.register("requestedRoleType")}>
                    <option value="Staff">{t("staff.roleStaff")}</option>
                    <option value="Teacher">{t("staff.roleTeacher")}</option>
                  </select>
                </FormField>

                {institute && institute.branches.length > 0 ? (
                  <FormField label={t("staff.branch")} htmlFor="reg-branch" error={fieldError(form, "branchId")}>
                    <select
                      id="reg-branch"
                      className={fieldClass}
                      defaultValue={institute.defaultBranchId ?? ""}
                      {...form.register("branchId")}
                    >
                      <option value="">{t("common.optional")}</option>
                      {institute.branches.map((b) => (
                        <option key={b.id} value={b.id}>
                          {b.name}
                        </option>
                      ))}
                    </select>
                  </FormField>
                ) : null}

                <FormField
                  label={t("auth.password")}
                  hint={t("auth.passwordHint")}
                  htmlFor="reg-password"
                  error={fieldError(form, "password")}
                >
                  <Input id="reg-password" type="password" {...form.register("password")} />
                </FormField>

                <FormField
                  label={t("auth.confirmPassword")}
                  htmlFor="reg-confirm-password"
                  error={fieldError(form, "confirmPassword")}
                >
                  <Input id="reg-confirm-password" type="password" {...form.register("confirmPassword")} />
                </FormField>

                <FormField
                  label={t("staff.notes")}
                  hint={t("staff.publicNotesHint")}
                  htmlFor="reg-notes"
                  error={fieldError(form, "notes")}
                >
                  <Textarea id="reg-notes" rows={2} {...form.register("notes")} />
                </FormField>

                {formError ? <p className="text-sm text-danger">{formError}</p> : null}

                <Button type="submit" disabled={submit.isPending}>
                  {submit.isPending ? t("common.saving") : t("staff.publicSubmit")}
                </Button>
              </form>
            </>
          )}
        </div>
      </main>
    </div>
  );
}
