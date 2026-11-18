import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { t } from "@/config/strings";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import type { ParentAccount } from "./model";

type StudentRow = { id: string; fullName: string; studentCode: string };
type StudentPage = { items: StudentRow[] };

export function ParentsAdminPage() {
  const toast = useToast();
  const { isReadOnly, hasPermission } = useAuth();
  const canEdit = hasPermission("students.edit") && !isReadOnly;
  const queryClient = useQueryClient();
  const [name, setName] = useState("");
  const [phone, setPhone] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [invite, setInvite] = useState(true);
  const [selected, setSelected] = useState<string[]>([]);

  const parents = useQuery({
    queryKey: ["parent-accounts"],
    queryFn: () => api<ParentAccount[]>("/api/parents"),
  });
  const students = useQuery({
    queryKey: ["parent-student-picker"],
    queryFn: () => api<StudentPage>("/api/students?page=1&pageSize=200"),
  });

  const create = useMutation({
    mutationFn: () =>
      api<ParentAccount>("/api/parents", {
        method: "POST",
        body: JSON.stringify({
          fullName: name,
          phone,
          email: email.trim() || null,
          password: invite ? null : password,
          sendInvite: invite,
          studentIds: selected,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("parentsAdmin.created"));
      setName("");
      setPhone("");
      setEmail("");
      setPassword("");
      setSelected([]);
      await queryClient.invalidateQueries({ queryKey: ["parent-accounts"] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  const resend = useMutation({
    mutationFn: (id: string) => api(`/api/parents/${id}/invite`, { method: "POST" }),
    onSuccess: async () => {
      toast.success(t("parentsAdmin.resent"));
      await queryClient.invalidateQueries({ queryKey: ["parent-accounts"] });
    },
    onError: (error) => toast.error(error instanceof Error ? error.message : t("errors.generic")),
  });

  if (parents.isLoading || students.isLoading) return <PageSkeleton />;

  return (
    <div>
      <PageHeader title={t("parentsAdmin.title")} description={t("parentsAdmin.description")} />
      {canEdit ? (
        <form
          className="mt-4 grid max-w-xl gap-3 rounded-lg border border-border bg-card p-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (selected.length === 0) {
              toast.error(t("parentsAdmin.studentsRequired"));
              return;
            }
            create.mutate();
          }}
        >
          <h2 className="text-sm font-semibold">{t("parentsAdmin.create")}</h2>
          <label className="grid gap-1 text-sm" htmlFor="parent-name">
            {t("parentsAdmin.name")}
            <Input id="parent-name" value={name} onChange={(event) => setName(event.target.value)} required />
          </label>
          <label className="grid gap-1 text-sm" htmlFor="parent-phone">
            {t("parentsAdmin.phone")}
            <Input id="parent-phone" value={phone} onChange={(event) => setPhone(event.target.value)} inputMode="tel" required />
          </label>
          <label className="grid gap-1 text-sm" htmlFor="parent-email">
            {t("parentsAdmin.email")}
            <Input id="parent-email" type="email" value={email} onChange={(event) => setEmail(event.target.value)} />
          </label>
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={invite} onChange={(event) => setInvite(event.target.checked)} />
            {t("parentsAdmin.sendInvite")}
          </label>
          {invite ? null : (
            <label className="grid gap-1 text-sm" htmlFor="parent-password">
              {t("auth.password")}
              <Input id="parent-password" type="password" value={password} onChange={(event) => setPassword(event.target.value)} required />
            </label>
          )}
          <fieldset className="grid max-h-48 gap-1 overflow-auto rounded-md border border-border p-2">
            <legend className="px-1 text-[12px] text-muted">{t("parentsAdmin.students")}</legend>
            {(students.data?.items ?? []).map((student) => (
              <label key={student.id} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={selected.includes(student.id)}
                  onChange={(event) =>
                    setSelected((current) =>
                      event.target.checked ? [...current, student.id] : current.filter((id) => id !== student.id),
                    )
                  }
                />
                {student.fullName} · {student.studentCode}
              </label>
            ))}
          </fieldset>
          <Button type="submit" disabled={create.isPending}>
            {t("parentsAdmin.create")}
          </Button>
        </form>
      ) : null}
      <ul className="mt-4 grid gap-2">
        {(parents.data ?? []).map((parent) => (
          <li key={parent.id} className="rounded-lg border border-border bg-card p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h2 className="text-sm font-semibold">{parent.fullName}</h2>
                <p className="text-[13px] text-muted">
                  {parent.phone}
                  {parent.email ? ` · ${parent.email}` : ""}
                  {parent.invitePending ? ` · ${t("parentsAdmin.pending")}` : ""}
                  {!parent.isActive && !parent.invitePending ? ` · ${t("parentsAdmin.inactive")}` : ""}
                </p>
                <p className="mt-1 text-sm">
                  {parent.students.map((student) => student.fullName).join(", ") || t("parentsAdmin.none")}
                </p>
              </div>
              {canEdit && parent.invitePending ? (
                <Button variant="outline" disabled={resend.isPending} onClick={() => resend.mutate(parent.id)}>
                  {t("parentsAdmin.resend")}
                </Button>
              ) : null}
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}
