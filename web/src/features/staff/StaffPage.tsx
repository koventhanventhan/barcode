import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { zodResolver } from "@hookform/resolvers/zod";
import { useState } from "react";
import { useForm } from "react-hook-form";
import { useTranslation } from "react-i18next";
import { z } from "zod";
import { DataTable, type DataColumn } from "@/components/data-table/DataTable";
import { EmptyState } from "@/components/empty-state";
import { fieldError, FormField } from "@/components/form-field";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { useAuth } from "@/features/auth/AuthProvider";
import { fieldClass } from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";
import type { Branch } from "@/lib/types";

type StaffUser = {
  id: string;
  email: string;
  fullName: string;
  phone: string | null;
  roleType: string;
  customRoleName: string | null;
  isActive: boolean;
};

type Template = { key: string; name: string; roleType: string; permissions: string[] };

const schema = z.object({
  fullName: z.string().trim().min(1).max(200),
  email: z.string().trim().email(),
  password: z.string().min(8),
  phone: z.string().trim().max(40),
  templateKey: z.string(),
  branchId: z.string(),
});

export function StaffPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const { hasPermission, isReadOnly } = useAuth();
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const users = useQuery({ queryKey: ["users"], queryFn: () => api<StaffUser[]>("/api/users") });
  const templates = useQuery({ queryKey: ["staff-templates"], queryFn: () => api<Template[]>("/api/staff/role-templates") });
  const branches = useQuery({ queryKey: ["branches"], queryFn: () => api<Branch[]>("/api/branches") });
  const toggle = useMutation({
    mutationFn: (input: { id: string; isActive: boolean }) =>
      api(`/api/users/${input.id}/status`, { method: "PUT", body: JSON.stringify({ isActive: input.isActive }) }),
    onSuccess: (_data, input) => {
      toast.success(input.isActive ? t("staff.enabled") : t("staff.disabled"));
      void queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });
  const create = useMutation({
    mutationFn: (body: z.infer<typeof schema>) =>
      api("/api/staff", {
        method: "POST",
        body: JSON.stringify({
          ...body,
          phone: body.phone || null,
          branchId: body.branchId || null,
          templateKey: body.templateKey || null,
        }),
      }),
    onSuccess: () => {
      toast.success(t("staff.saved"));
      setOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["users"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  if (!hasPermission("users.view")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  if (users.isLoading) return <PageSkeleton />;
  const rows = (users.data ?? []).filter((user) => user.roleType !== "Parent");
  const canManage = hasPermission("users.manage") && !isReadOnly;
  const columns: DataColumn<StaffUser>[] = [
    { id: "name", header: t("staff.name"), cell: (row) => row.fullName, exportValue: (row) => row.fullName, sortValue: (row) => row.fullName },
    { id: "email", header: t("staff.email"), cell: (row) => row.email, exportValue: (row) => row.email },
    { id: "role", header: t("staff.role"), cell: (row) => row.customRoleName ?? row.roleType, exportValue: (row) => row.customRoleName ?? row.roleType },
    {
      id: "status",
      header: t("staff.status"),
      cell: (row) => (
        <Switch
          checked={row.isActive}
          disabled={!canManage || toggle.isPending}
          aria-label={row.fullName}
          onCheckedChange={(checked) => toggle.mutate({ id: row.id, isActive: checked })}
        />
      ),
      exportValue: (row) => (row.isActive ? "active" : "inactive"),
    },
  ];

  return (
    <div>
      <PageHeader
        title={t("staff.title")}
        description={t("staff.description")}
        crumbs={[{ label: t("nav.staff") }]}
        actions={canManage ? <Button type="button" onClick={() => setOpen(true)}>{t("staff.new")}</Button> : null}
      />
      {rows.length === 0 ? <EmptyState title={t("staff.empty")} /> : (
        <DataTable data={rows} columns={columns} filename="staff.csv" getRowId={(row) => row.id} enableExport={false} />
      )}
      <StaffDialog
        open={open}
        templates={templates.data ?? []}
        branches={branches.data ?? []}
        pending={create.isPending}
        onOpenChange={setOpen}
        onSubmit={(body) => create.mutate(body)}
      />
    </div>
  );
}

function StaffDialog({
  open,
  templates,
  branches,
  pending,
  onOpenChange,
  onSubmit,
}: {
  open: boolean;
  templates: Template[];
  branches: Branch[];
  pending: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (body: z.infer<typeof schema>) => void;
}) {
  const { t } = useTranslation();
  const form = useForm<z.infer<typeof schema>>({
    resolver: zodResolver(schema),
    defaultValues: { fullName: "", email: "", password: "", phone: "", templateKey: templates[0]?.key ?? "front-desk", branchId: "" },
  });
  const template = templates.find((item) => item.key === form.watch("templateKey"));
  return (
    <Dialog open={open} onOpenChange={onOpenChange} title={t("staff.new")} description={t("staff.description")}>
      <form className="grid gap-3" onSubmit={form.handleSubmit(onSubmit)}>
        <FormField label={t("staff.name")} htmlFor="staff-name" error={fieldError(form, "fullName")}>
          <Input id="staff-name" {...form.register("fullName")} />
        </FormField>
        <FormField label={t("staff.email")} htmlFor="staff-email" error={fieldError(form, "email")}>
          <Input id="staff-email" type="email" {...form.register("email")} />
        </FormField>
        <FormField label={t("staff.password")} htmlFor="staff-password" error={fieldError(form, "password")} hint={t("auth.passwordHint")}>
          <Input id="staff-password" type="password" {...form.register("password")} />
        </FormField>
        <FormField label={t("staff.phone")} htmlFor="staff-phone">
          <Input id="staff-phone" {...form.register("phone")} />
        </FormField>
        <FormField label={t("staff.template")} htmlFor="staff-template" error={fieldError(form, "templateKey")}>
          <select id="staff-template" className={fieldClass} {...form.register("templateKey")}>
            {templates.map((item) => <option key={item.key} value={item.key}>{item.name}</option>)}
          </select>
        </FormField>
        {template ? <p className="text-[13px] text-muted">{template.permissions.join(", ")}</p> : null}
        <FormField label={t("staff.branch")} htmlFor="staff-branch">
          <select id="staff-branch" className={fieldClass} {...form.register("branchId")}>
            <option value="">{t("common.none")}</option>
            {branches.map((branch) => <option key={branch.id} value={branch.id}>{branch.name}</option>)}
          </select>
        </FormField>
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>{t("common.cancel")}</Button>
          <Button type="submit" disabled={pending}>{t("common.save")}</Button>
        </div>
      </form>
    </Dialog>
  );
}
