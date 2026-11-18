import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { t } from "@/config/strings";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { api } from "@/lib/api";

type AddOn = {
  id: string;
  type: string;
  quantity: number;
  validFrom: string;
  validTo: string | null;
  isActive: boolean;
  note: string | null;
};

const typeOptions = [
  ["ExtraStorageGb", "addons.extraStorage"],
  ["ExtraStudents", "addons.extraStudents"],
  ["ExtraSmsSegments", "addons.extraSms"],
] as const;

export function AddOnsPanel({ tenantId, planCode }: { tenantId: string; planCode: string }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [type, setType] = useState<(typeof typeOptions)[number][0]>("ExtraStorageGb");
  const [quantity, setQuantity] = useState("10");
  const [validTo, setValidTo] = useState("");
  const [note, setNote] = useState("");
  const enterprise = planCode.toLowerCase() === "enterprise";

  const addOns = useQuery({
    queryKey: ["tenant-addons", tenantId],
    queryFn: () => api<AddOn[]>(`/api/super-admin/tenants/${tenantId}/add-ons`),
  });
  const grant = useMutation({
    mutationFn: () =>
      api(`/api/super-admin/tenants/${tenantId}/add-ons`, {
        method: "POST",
        body: JSON.stringify({
          type,
          quantity: Number(quantity),
          validTo: validTo ? new Date(validTo).toISOString() : null,
          note: note.trim() || null,
        }),
      }),
    onSuccess: async () => {
      toast.success(t("addons.granted"));
      setNote("");
      await queryClient.invalidateQueries({ queryKey: ["tenant-addons", tenantId] });
    },
    onError: (error: Error) => toast.error(error.message),
  });
  const revoke = useMutation({
    mutationFn: (id: string) => api(`/api/super-admin/tenants/${tenantId}/add-ons/${id}`, { method: "DELETE" }),
    onSuccess: async () => {
      toast.success(t("addons.revoked"));
      await queryClient.invalidateQueries({ queryKey: ["tenant-addons", tenantId] });
    },
    onError: (error: Error) => toast.error(error.message),
  });

  return (
    <section className="mt-4 rounded-lg border border-border bg-card">
      <h2 className="border-b border-border px-4 py-3 text-sm font-semibold">{t("addons.title")}</h2>
      <form
        className="grid gap-3 border-b border-border p-4 sm:grid-cols-2"
        onSubmit={(event) => {
          event.preventDefault();
          grant.mutate();
        }}
      >
        <label className="text-xs font-medium">
          {t("addons.type")}
          <select
            className="mt-1 h-9 w-full rounded-md border border-border bg-card px-3 text-sm"
            value={type}
            onChange={(event) => setType(event.target.value as (typeof typeOptions)[number][0])}
          >
            {typeOptions.map(([value, label]) => (
              <option key={value} value={value} disabled={value === "ExtraStudents" && !enterprise}>
                {t(label)}
              </option>
            ))}
          </select>
        </label>
        <label className="text-xs font-medium">
          {t("addons.quantity")}
          <Input className="mt-1" inputMode="numeric" value={quantity} onChange={(event) => setQuantity(event.target.value)} required />
        </label>
        <label className="text-xs font-medium">
          {t("addons.until")}
          <Input className="mt-1" type="date" value={validTo} onChange={(event) => setValidTo(event.target.value)} />
        </label>
        <label className="text-xs font-medium">
          {t("addons.note")}
          <Input className="mt-1" value={note} onChange={(event) => setNote(event.target.value)} />
        </label>
        <div className="sm:col-span-2">
          {!enterprise ? <p className="mb-2 text-xs text-muted">{t("addons.enterpriseOnly")}</p> : null}
          <Button type="submit" disabled={grant.isPending || (type === "ExtraStudents" && !enterprise)}>{t("addons.grant")}</Button>
        </div>
      </form>
      <ul>
        {(addOns.data ?? []).map((addOn) => (
          <li key={addOn.id} className="flex flex-wrap items-center justify-between gap-2 border-b border-border px-4 py-2 text-sm last:border-0">
            <span>
              {t(`addons.${addOn.type}`, { defaultValue: addOn.type })} · {addOn.quantity}
              <span className="ml-2 text-xs text-muted">{addOn.isActive ? t("addons.active") : t("addons.inactive")}</span>
            </span>
            <Button size="sm" variant="outline" onClick={() => revoke.mutate(addOn.id)}>{t("addons.revoke")}</Button>
          </li>
        ))}
        {(addOns.data ?? []).length === 0 ? <li className="px-4 py-4 text-sm text-muted">{t("addons.empty")}</li> : null}
      </ul>
    </section>
  );
}
