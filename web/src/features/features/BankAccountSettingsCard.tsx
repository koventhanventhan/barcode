import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Building2 } from "lucide-react";
import { useEffect, useState } from "react";
import { t } from "@/config/strings";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import type { TenantBankAccountDto } from "@/features/fees/api";
import { api } from "@/lib/api";

export function BankAccountSettingsCard({ disabled }: { disabled: boolean }) {
  const toast = useToast();
  const queryClient = useQueryClient();

  const bankQuery = useQuery({
    queryKey: ["bank-account-settings"],
    queryFn: () => api<TenantBankAccountDto>("/api/settings/bank-account"),
  });

  const [bankName, setBankName] = useState("");
  const [branchName, setBranchName] = useState("");
  const [accountName, setAccountName] = useState("");
  const [accountNumber, setAccountNumber] = useState("");
  const [instructions, setInstructions] = useState("");
  const [isEnabled, setIsEnabled] = useState(true);

  useEffect(() => {
    if (bankQuery.data) {
      setBankName(bankQuery.data.bankName);
      setBranchName(bankQuery.data.branchName ?? "");
      setAccountName(bankQuery.data.accountName);
      setAccountNumber(bankQuery.data.accountNumber);
      setInstructions(bankQuery.data.instructions ?? "");
      setIsEnabled(bankQuery.data.isConfigured ? bankQuery.data.isEnabled : true);
    }
  }, [bankQuery.data]);

  const saveMutation = useMutation({
    mutationFn: () =>
      api<TenantBankAccountDto>("/api/settings/bank-account", {
        method: "PUT",
        body: JSON.stringify({
          bankName,
          branchName: branchName.trim() || undefined,
          accountName,
          accountNumber,
          instructions: instructions.trim() || undefined,
          isEnabled,
        }),
      }),
    onSuccess: () => {
      toast.success(t("fees.bankSettingsSaved"));
      void queryClient.invalidateQueries({ queryKey: ["bank-account-settings"] });
      void queryClient.invalidateQueries({ queryKey: ["parent-bank-account"] });
    },
    onError: (err) => toast.error(err instanceof Error ? err.message : t("errors.generic")),
  });

  return (
    <section className="mb-4 rounded-lg border border-border bg-card p-4">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Building2 className="h-4 w-4 text-primary" aria-hidden="true" />
          <div>
            <h2 className="text-sm font-semibold">{t("fees.bankSettingsTitle")}</h2>
            <p className="text-xs text-muted">{t("fees.bankSettingsDescription")}</p>
          </div>
        </div>
        <Switch
          checked={isEnabled}
          disabled={disabled}
          aria-label={t("fees.bankSettingsEnabled")}
          onCheckedChange={setIsEnabled}
        />
      </div>

      <form
        className="mt-4 grid gap-3 sm:grid-cols-2"
        onSubmit={(e) => {
          e.preventDefault();
          saveMutation.mutate();
        }}
      >
        <div>
          <label className="text-xs font-medium" htmlFor="bank-name">
            {t("fees.bankName")}
          </label>
          <Input
            id="bank-name"
            value={bankName}
            disabled={disabled}
            onChange={(e) => setBankName(e.target.value)}
            placeholder="Commercial Bank of Ceylon"
            required={isEnabled}
          />
        </div>
        <div>
          <label className="text-xs font-medium" htmlFor="bank-branch">
            {t("fees.bankBranch")}
          </label>
          <Input
            id="bank-branch"
            value={branchName}
            disabled={disabled}
            onChange={(e) => setBranchName(e.target.value)}
            placeholder="Colombo 07"
          />
        </div>
        <div>
          <label className="text-xs font-medium" htmlFor="bank-account-name">
            {t("fees.accountHolder")}
          </label>
          <Input
            id="bank-account-name"
            value={accountName}
            disabled={disabled}
            onChange={(e) => setAccountName(e.target.value)}
            placeholder="Admino Academy (Pvt) Ltd"
            required={isEnabled}
          />
        </div>
        <div>
          <label className="text-xs font-medium" htmlFor="bank-account-number">
            {t("fees.accountNumber")}
          </label>
          <Input
            id="bank-account-number"
            value={accountNumber}
            disabled={disabled}
            onChange={(e) => setAccountNumber(e.target.value)}
            placeholder="1000456789"
            required={isEnabled}
          />
        </div>
        <div className="sm:col-span-2">
          <label className="text-xs font-medium" htmlFor="bank-instructions">
            {t("fees.bankInstructions")}
          </label>
          <Input
            id="bank-instructions"
            value={instructions}
            disabled={disabled}
            onChange={(e) => setInstructions(e.target.value)}
            placeholder={t("fees.bankInstructionsPlaceholder")}
          />
        </div>
        <div className="sm:col-span-2 flex justify-end">
          <Button type="submit" size="sm" disabled={disabled || saveMutation.isPending}>
            {t("common.save")}
          </Button>
        </div>
      </form>
    </section>
  );
}
