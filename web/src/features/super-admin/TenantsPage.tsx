import { zodResolver } from "@hookform/resolvers/zod";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { t } from "@/config/strings";
import { Link } from "react-router-dom";
import { z } from "zod";
import { DataTable, type DataColumn } from "@/components/data-table/DataTable";
import { FormField, fieldError } from "@/components/form-field";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";
import type { Plan, TenantSummary } from "@/lib/types";
import { passwordSchema } from "@/features/auth/password";

export function TenantsPage() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const tenants = useQuery({ queryKey: ["tenants"], queryFn: () => api<TenantSummary[]>("/api/super-admin/tenants") });
  const plans = useQuery({ queryKey: ["plans"], queryFn: () => api<Plan[]>("/api/super-admin/plans") });
  const schema = z.object({
    name: z.string().min(2),
    subdomain: z.string().regex(/^[a-z0-9-]{2,63}$/),
    adminEmail: z.string().email(),
    adminFullName: z.string().min(2),
    adminPassword: passwordSchema(t("auth.passwordHint")),
    planCode: z.string().min(1),
    contactEmail: z.string(),
    contactPhone: z.string(),
  });
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: "",
      subdomain: "",
      adminEmail: "",
      adminFullName: "",
      adminPassword: "",
      planCode: "Starter",
      contactEmail: "",
      contactPhone: "",
    },
  });
  const create = useMutation({
    mutationFn: (values: z.infer<typeof schema>) =>
      api("/api/super-admin/tenants", {
        method: "POST",
        body: JSON.stringify({
          ...values,
          contactEmail: values.contactEmail || null,
          contactPhone: values.contactPhone || null,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("tenants.createdToast", { name: form.getValues("name") }));
      setOpen(false);
      form.reset();
      await queryClient.invalidateQueries({ queryKey: ["tenants"] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  if (tenants.isLoading) return <PageSkeleton />;

  const columns: DataColumn<TenantSummary>[] = [
    {
      id: "name",
      header: t("tenants.name"),
      sortValue: (row) => row.name,
      exportValue: (row) => row.name,
      cell: (row) => (
        <Link className="font-medium text-primary" to={`/admin/tenants/${row.id}`}>
          {row.name}
        </Link>
      ),
    },
    { id: "subdomain", header: t("tenants.subdomain"), sortValue: (row) => row.subdomain, exportValue: (row) => row.subdomain, cell: (row) => row.subdomain },
    { id: "plan", header: t("tenants.plan"), sortValue: (row) => row.planName ?? "", exportValue: (row) => row.planName ?? "", cell: (row) => row.planName ?? t("common.none") },
    { id: "status", header: t("tenants.status"), sortValue: (row) => row.subscriptionStatus ?? "", exportValue: (row) => row.subscriptionStatus ?? "", cell: (row) => row.subscriptionStatus ?? t("common.none") },
    {
      id: "active",
      header: t("common.active"),
      sortValue: (row) => (row.isActive ? 1 : 0),
      exportValue: (row) => (row.isActive ? t("common.yes") : t("common.no")),
      cell: (row) => (row.isActive ? t("common.yes") : t("common.no")),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t("tenants.title")}
        description={t("tenants.description")}
        crumbs={[{ label: t("nav.platform") }, { label: t("nav.tenants") }]}
        actions={<Button onClick={() => setOpen(true)}>{t("tenants.create")}</Button>}
      />
      <DataTable data={tenants.data ?? []} columns={columns} filename="institutes.csv" getRowId={(row) => row.id} />
      <Dialog open={open} onOpenChange={setOpen} title={t("tenants.create")}>
        <form className="grid gap-3" onSubmit={form.handleSubmit((values) => create.mutate(values))}>
          <FormField label={t("tenants.name")} htmlFor="name" error={fieldError(form, "name")}>
            <Input id="name" {...form.register("name")} />
          </FormField>
          <FormField label={t("tenants.subdomain")} htmlFor="subdomain" error={fieldError(form, "subdomain")}>
            <Input id="subdomain" {...form.register("subdomain")} />
          </FormField>
          <FormField label={t("tenants.adminName")} htmlFor="adminFullName" error={fieldError(form, "adminFullName")}>
            <Input id="adminFullName" {...form.register("adminFullName")} />
          </FormField>
          <FormField label={t("tenants.adminEmail")} htmlFor="adminEmail" error={fieldError(form, "adminEmail")}>
            <Input id="adminEmail" type="email" {...form.register("adminEmail")} />
          </FormField>
          <FormField label={t("tenants.adminPassword")} hint={t("auth.passwordHint")} htmlFor="adminPassword" error={fieldError(form, "adminPassword")}>
            <Input id="adminPassword" type="password" {...form.register("adminPassword")} />
          </FormField>
          <FormField label={t("tenants.plan")} htmlFor="planCode">
            <select id="planCode" className="h-9 rounded-md border border-border bg-card px-2" {...form.register("planCode")}>
              {(plans.data ?? []).filter((plan) => plan.isActive).map((plan) => (
                <option key={plan.id} value={plan.code}>
                  {plan.name}
                </option>
              ))}
            </select>
          </FormField>
          <FormField label={t("tenants.contactEmail")} htmlFor="contactEmail">
            <Input id="contactEmail" {...form.register("contactEmail")} />
          </FormField>
          <FormField label={t("tenants.phone")} htmlFor="contactPhone">
            <Input id="contactPhone" {...form.register("contactPhone")} />
          </FormField>
          <Button type="submit" disabled={create.isPending}>
            {t("common.create")}
          </Button>
        </form>
      </Dialog>
    </div>
  );
}
