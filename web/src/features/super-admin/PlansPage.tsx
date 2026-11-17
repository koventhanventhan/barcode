import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useMemo, useState } from "react";
import { useForm } from "react-hook-form";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { DataTable, type DataColumn } from "@/components/data-table/DataTable";
import { FormField, fieldError } from "@/components/form-field";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { FEATURE_KEYS, LIMIT_KEYS } from "@/domain/catalog";
import { formatMonthlyPrice, monthsFree, parseOptionalMoney } from "@/features/marketing/price";
import { api } from "@/lib/api";
import type { Plan } from "@/lib/types";

function moneyField(message: string) {
  return z.string().refine((value) => !Number.isNaN(parseOptionalMoney(value)), { message });
}

function buildSchema(invalidPrice: string) {
  return z.object({
    code: z.string().min(2).max(50),
    name: z.string().min(2),
    description: z.string(),
    priceMonthlyLkr: moneyField(invalidPrice),
    priceYearlyLkr: moneyField(invalidPrice),
    setupFeeLkr: moneyField(invalidPrice),
    extraSmsPerSegmentLkr: moneyField(invalidPrice),
    extraStoragePerGbLkr: moneyField(invalidPrice),
    sortOrder: z.coerce.number().int(),
    isActive: z.boolean(),
    features: z.array(z.string()),
    limits: z.record(z.string()),
  });
}

type PlanForm = z.infer<ReturnType<typeof buildSchema>>;

function moneyText(value: number | null | undefined) {
  return value === null || value === undefined ? "" : String(value);
}

function emptyForm(): PlanForm {
  return {
    code: "",
    name: "",
    description: "",
    priceMonthlyLkr: "",
    priceYearlyLkr: "",
    setupFeeLkr: "",
    extraSmsPerSegmentLkr: "",
    extraStoragePerGbLkr: "",
    sortOrder: 10,
    isActive: true,
    features: [],
    limits: Object.fromEntries(LIMIT_KEYS.map((key) => [key, ""])),
  };
}

function fromPlan(plan: Plan): PlanForm {
  return {
    code: plan.code,
    name: plan.name,
    description: plan.description ?? "",
    priceMonthlyLkr: moneyText(plan.priceMonthlyLkr),
    priceYearlyLkr: moneyText(plan.priceYearlyLkr),
    setupFeeLkr: moneyText(plan.setupFeeLkr),
    extraSmsPerSegmentLkr: moneyText(plan.extraSmsPerSegmentLkr),
    extraStoragePerGbLkr: moneyText(plan.extraStoragePerGbLkr),
    sortOrder: plan.sortOrder,
    isActive: plan.isActive,
    features: plan.features,
    limits: Object.fromEntries(LIMIT_KEYS.map((key) => [key, plan.limits[key] === null || plan.limits[key] === undefined ? "" : String(plan.limits[key])])),
  };
}

export function PlansPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState<Plan | null>(null);
  const [creating, setCreating] = useState(false);
  const [removeId, setRemoveId] = useState<Plan | null>(null);
  const plans = useQuery({ queryKey: ["plans"], queryFn: () => api<Plan[]>("/api/super-admin/plans") });
  const schema = useMemo(() => buildSchema(t("plans.priceInvalid")), [t]);
  const form = useForm<PlanForm>({ resolver: zodResolver(schema), defaultValues: emptyForm() });

  function openCreate() {
    form.reset(emptyForm());
    setEditing(null);
    setCreating(true);
  }

  function openEdit(plan: Plan) {
    form.reset(fromPlan(plan));
    setEditing(plan);
    setCreating(true);
  }

  const save = useMutation({
    mutationFn: (values: PlanForm) => {
      const limits = LIMIT_KEYS.map((key) => ({
        limitKey: key,
        maxValue: values.limits[key]?.trim() ? Number(values.limits[key]) : null,
      }));
      const body = {
        name: values.name,
        description: values.description || null,
        priceMonthlyLkr: parseOptionalMoney(values.priceMonthlyLkr),
        priceYearlyLkr: parseOptionalMoney(values.priceYearlyLkr),
        setupFeeLkr: parseOptionalMoney(values.setupFeeLkr),
        extraSmsPerSegmentLkr: parseOptionalMoney(values.extraSmsPerSegmentLkr),
        extraStoragePerGbLkr: parseOptionalMoney(values.extraStoragePerGbLkr),
        isActive: values.isActive,
        sortOrder: values.sortOrder,
        features: values.features,
        limits,
      };
      if (editing) {
        return api(`/api/super-admin/plans/${editing.id}`, { method: "PUT", body: JSON.stringify(body) });
      }
      return api("/api/super-admin/plans", {
        method: "POST",
        body: JSON.stringify({ ...body, code: values.code, currency: "LKR" }),
      });
    },
    onSuccess: async () => {
      toast.success(t("plans.saved"));
      setCreating(false);
      await queryClient.invalidateQueries({ queryKey: ["plans"] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  const remove = useMutation({
    mutationFn: (plan: Plan) => api(`/api/super-admin/plans/${plan.id}`, { method: "DELETE" }),
    onSuccess: async () => {
      toast.success(t("plans.saved"));
      setRemoveId(null);
      await queryClient.invalidateQueries({ queryKey: ["plans"] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  if (plans.isLoading) return <PageSkeleton />;

  const columns: DataColumn<Plan>[] = [
    { id: "name", header: t("plans.name"), sortValue: (row) => row.name, exportValue: (row) => row.name, cell: (row) => row.name },
    { id: "code", header: t("plans.code"), sortValue: (row) => row.code, exportValue: (row) => row.code, cell: (row) => row.code },
    {
      id: "price",
      header: t("plans.price"),
      sortValue: (row) => row.priceMonthlyLkr ?? -1,
      exportValue: (row) => (row.priceMonthlyLkr === null ? "" : String(row.priceMonthlyLkr)),
      cell: (row) => (row.priceMonthlyLkr === null ? t("marketing.contactUs") : `${row.priceMonthlyLkr} ${row.currency}`),
    },
    {
      id: "active",
      header: t("common.active"),
      sortValue: (row) => (row.isActive ? 1 : 0),
      exportValue: (row) => (row.isActive ? t("common.yes") : t("common.no")),
      cell: (row) => (row.isActive ? t("common.yes") : t("common.no")),
    },
    {
      id: "actions",
      header: t("common.edit"),
      enableHiding: false,
      exportValue: () => "",
      cell: (row) => (
        <span className="flex gap-2">
          <Button type="button" size="sm" variant="outline" onClick={() => openEdit(row)}>
            {t("common.edit")}
          </Button>
          <Button type="button" size="sm" variant="danger" onClick={() => setRemoveId(row)}>
            {t("common.delete")}
          </Button>
        </span>
      ),
    },
  ];

  const selectedFeatures = form.watch("features");
  const preview = form.watch();
  const previewMonthly = parseOptionalMoney(preview.priceMonthlyLkr || "");
  const previewYearly = parseOptionalMoney(preview.priceYearlyLkr || "");
  const previewPrice = formatMonthlyPrice(Number.isNaN(previewMonthly) ? null : previewMonthly, "LKR");
  const previewFree = monthsFree(
    Number.isNaN(previewMonthly) ? null : previewMonthly,
    Number.isNaN(previewYearly) ? null : previewYearly,
  );

  return (
    <div>
      <PageHeader
        title={t("plans.title")}
        description={t("plans.description")}
        crumbs={[{ label: t("nav.platform") }, { label: t("nav.plans") }]}
        actions={<Button onClick={openCreate}>{t("plans.create")}</Button>}
      />
      <DataTable data={plans.data ?? []} columns={columns} filename="plans.csv" getRowId={(row) => row.id} />
      <Dialog open={creating} onOpenChange={setCreating} title={editing ? t("common.edit") : t("plans.create")}>
        <form className="grid max-h-[70vh] gap-3 overflow-y-auto pr-1" onSubmit={form.handleSubmit((values) => save.mutate(values))}>
          <FormField label={t("plans.code")} htmlFor="code" error={fieldError(form, "code")}>
            <Input id="code" disabled={Boolean(editing)} {...form.register("code")} />
          </FormField>
          <FormField label={t("plans.name")} htmlFor="planName" error={fieldError(form, "name")}>
            <Input id="planName" {...form.register("name")} />
          </FormField>
          <FormField label={t("plans.descriptionLabel")} htmlFor="description">
            <Textarea id="description" {...form.register("description")} />
          </FormField>
          <FormField label={t("plans.price")} hint={t("plans.priceHint")} htmlFor="price" error={fieldError(form, "priceMonthlyLkr")}>
            <Input id="price" inputMode="decimal" {...form.register("priceMonthlyLkr")} />
          </FormField>
          <FormField label={t("plans.priceYearly")} hint={t("plans.priceHint")} htmlFor="priceYearly" error={fieldError(form, "priceYearlyLkr")}>
            <Input id="priceYearly" inputMode="decimal" {...form.register("priceYearlyLkr")} />
          </FormField>
          <FormField label={t("plans.setupFee")} hint={t("plans.priceHint")} htmlFor="setupFee" error={fieldError(form, "setupFeeLkr")}>
            <Input id="setupFee" inputMode="decimal" {...form.register("setupFeeLkr")} />
          </FormField>
          <FormField label={t("plans.extraSms")} hint={t("plans.priceHint")} htmlFor="extraSms" error={fieldError(form, "extraSmsPerSegmentLkr")}>
            <Input id="extraSms" inputMode="decimal" {...form.register("extraSmsPerSegmentLkr")} />
          </FormField>
          <FormField label={t("plans.extraStorage")} hint={t("plans.priceHint")} htmlFor="extraStorage" error={fieldError(form, "extraStoragePerGbLkr")}>
            <Input id="extraStorage" inputMode="decimal" {...form.register("extraStoragePerGbLkr")} />
          </FormField>
          <div className="rounded-lg border border-border bg-background p-4" aria-live="polite">
            <p className="text-[11px] font-semibold uppercase tracking-[0.08em] text-muted">{t("plans.preview")}</p>
            <div className="mt-2 flex items-start justify-between gap-2">
              <p className="font-heading text-lg font-bold">{preview.name || t("plans.name")}</p>
              {preview.code === "Pro" ? (
                <span className="rounded bg-brand-50 px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wide text-primary">
                  {t("marketing.mostPopular")}
                </span>
              ) : null}
            </div>
            <p className="mt-1 text-xs leading-5 text-muted">{preview.description || t("plans.previewHint")}</p>
            <p className="mt-2 font-heading text-2xl font-extrabold tabular">{previewPrice ?? t("marketing.contactUs")}</p>
            {previewFree ? <p className="text-xs font-semibold text-primary">{t("marketing.monthsFree", { count: previewFree })}</p> : null}
          </div>
          <FormField label={t("plans.sort")} htmlFor="sort">
            <Input id="sort" type="number" {...form.register("sortOrder")} />
          </FormField>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" {...form.register("isActive")} />
            {t("plans.active")}
          </label>
          <fieldset>
            <legend className="mb-1 text-[13px] font-medium">{t("plans.features")}</legend>
            <div className="grid gap-1 sm:grid-cols-2">
              {FEATURE_KEYS.map((feature) => (
                <label key={feature} className="flex items-center gap-2 text-[13px]">
                  <input
                    type="checkbox"
                    checked={selectedFeatures.includes(feature)}
                    onChange={(event) => {
                      const next = event.target.checked
                        ? [...selectedFeatures, feature]
                        : selectedFeatures.filter((item) => item !== feature);
                      form.setValue("features", next);
                    }}
                  />
                  {t(`feature.${feature}`)}
                </label>
              ))}
            </div>
          </fieldset>
          <fieldset>
            <legend className="mb-1 text-[13px] font-medium">{t("plans.limits")}</legend>
            <p className="mb-2 text-xs text-muted">{t("plans.emptyLimit")}</p>
            <div className="grid gap-2 sm:grid-cols-2">
              {LIMIT_KEYS.map((limit) => (
                <FormField key={limit} label={t(`limit.${limit}`)} htmlFor={limit}>
                  <Input id={limit} inputMode="numeric" {...form.register(`limits.${limit}`)} />
                </FormField>
              ))}
            </div>
          </fieldset>
          <Button type="submit" disabled={save.isPending}>{t("common.save")}</Button>
        </form>
      </Dialog>
      <ConfirmDialog
        open={Boolean(removeId)}
        title={t("common.delete")}
        description={t("plans.deleteBody", { name: removeId?.name ?? "" })}
        confirmLabel={t("common.delete")}
        destructive
        pending={remove.isPending}
        onOpenChange={(open) => !open && setRemoveId(null)}
        onConfirm={() => removeId && remove.mutate(removeId)}
      />
    </div>
  );
}
