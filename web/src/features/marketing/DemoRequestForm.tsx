import { useMutation } from "@tanstack/react-query";
import { useState, type FormEvent } from "react";
import { t } from "@/config/strings";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError, api } from "@/lib/api";

export function DemoRequestForm({
  title,
  submitLabel,
}: {
  title?: string;
  submitLabel?: string;
}) {
  const [sent, setSent] = useState("");
  const [error, setError] = useState("");
  const mutation = useMutation({
    mutationFn: (body: {
      instituteName: string;
      contactName: string;
      email: string;
      phone: string;
      city: string;
      approximateStudents: number | null;
      message: string;
    }) =>
      api<{ message: string }>("/api/public/demo-requests", {
        method: "POST",
        body: JSON.stringify(body),
      }),
    onSuccess: (result) => {
      setError("");
      setSent(result.message);
    },
    onError: (reason) => {
      setSent("");
      setError(reason instanceof ApiError ? reason.message : t("errors.generic"));
    },
  });

  function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const students = String(data.get("approximateStudents") ?? "");
    mutation.mutate({
      instituteName: String(data.get("instituteName") ?? ""),
      contactName: String(data.get("contactName") ?? ""),
      email: String(data.get("email") ?? ""),
      phone: String(data.get("phone") ?? ""),
      city: String(data.get("city") ?? ""),
      approximateStudents: students ? Number(students) : null,
      message: String(data.get("message") ?? ""),
    });
  }

  return (
    <form className="grid gap-3.5 rounded-lg border border-border bg-card p-6" onSubmit={onSubmit}>
      <h2 className="font-heading text-lg font-bold">{title ?? t("marketing.formTitle")}</h2>
      <div className="grid gap-3.5 sm:grid-cols-2">
        <Field name="instituteName" label={t("marketing.institute")} required />
        <Field name="contactName" label={t("marketing.contactName")} required />
      </div>
      <div className="grid gap-3.5 sm:grid-cols-2">
        <Field name="email" label={t("marketing.email")} type="email" required />
        <Field name="phone" label={t("marketing.phone")} />
      </div>
      <div className="grid gap-3.5 sm:grid-cols-2">
        <Field name="city" label={t("marketing.city")} />
        <Field name="approximateStudents" label={t("marketing.students")} type="number" />
      </div>
      <label className="grid gap-1.5 text-[13px] font-medium">
        {t("marketing.message")}
        <textarea
          name="message"
          rows={3}
          className="rounded-md border border-border bg-background px-3 py-2 text-sm text-foreground"
        />
      </label>
      {error ? (
        <p className="text-sm text-danger" role="alert">
          {error}
        </p>
      ) : null}
      {sent ? (
        <p className="rounded-md border border-primary/20 bg-brand-50 px-3 py-2 text-sm font-medium text-primary">
          {sent}
        </p>
      ) : null}
      <Button type="submit" disabled={mutation.isPending}>
        {mutation.isPending ? t("common.saving") : (submitLabel ?? t("marketing.submitDemo"))}
      </Button>
    </form>
  );
}

function Field({
  name,
  label,
  type = "text",
  required = false,
}: {
  name: string;
  label: string;
  type?: string;
  required?: boolean;
}) {
  return (
    <label className="grid gap-1.5 text-[13px] font-medium">
      {label}
      <Input name={name} type={type} required={required} />
    </label>
  );
}
