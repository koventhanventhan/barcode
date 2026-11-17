import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import { getSelectedBranch, setSelectedBranch, subscribeBranch } from "@/lib/session";

type BranchOption = { id: string; name: string; code: string };
type BranchScope = { canViewAll: boolean; branches: BranchOption[] };

export function BranchSwitcher() {
  const { t } = useTranslation();
  const { user } = useAuth();
  const queryClient = useQueryClient();
  const [selected, setSelected] = useState<string | null>(getSelectedBranch());
  const scope = useQuery({
    queryKey: ["my-branches", user?.id],
    enabled: Boolean(user && user.role !== "Parent"),
    queryFn: () => api<BranchScope>("/api/me/branches"),
  });

  useEffect(() => subscribeBranch(() => setSelected(getSelectedBranch())), []);

  const branches = scope.data?.branches ?? [];
  if (user?.role === "Parent" || branches.length < 2) return null;

  const canViewAll = Boolean(scope.data?.canViewAll);
  const value = canViewAll && !selected ? "all" : (selected ?? branches[0]?.id ?? "");

  return (
    <label className="flex min-w-0 max-w-36 shrink-0 items-center gap-1 text-xs text-muted">
      <span className="sr-only">{t("shell.branch")}</span>
      <select
        aria-label={t("shell.branch")}
        className="h-8 w-full min-w-0 rounded-md border border-border bg-card px-2 text-[13px] text-foreground"
        value={value}
        onChange={(event) => {
          const next = event.target.value === "all" ? null : event.target.value;
          setSelectedBranch(next);
          setSelected(next);
          void queryClient.invalidateQueries();
        }}
      >
        {canViewAll ? <option value="all">{t("shell.allBranches")}</option> : null}
        {branches.map((branch) => (
          <option key={branch.id} value={branch.id}>
            {branch.name}
          </option>
        ))}
      </select>
    </label>
  );
}
