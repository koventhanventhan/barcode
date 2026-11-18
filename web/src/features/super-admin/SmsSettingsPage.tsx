import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, RefreshCw, Send, ShieldCheck } from "lucide-react";
import { useEffect, useState } from "react";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { StatCard } from "@/components/stat-card";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { t } from "@/config/strings";
import { fieldClass } from "@/features/academics/shared";
import { ApiError, api } from "@/lib/api";
import type {
  SmsGatewayBalance,
  SuperAdminSmsSettings,
  SuperAdminTestSmsResult,
} from "@/lib/types";

export function SmsSettingsPage() {
  const toast = useToast();
  const queryClient = useQueryClient();

  const [provider, setProvider] = useState<"Fake" | "NotifyLk">("Fake");
  const [userId, setUserId] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [clearApiKey, setClearApiKey] = useState(false);
  const [senderId, setSenderId] = useState("EduFlow");
  const [promotionalSenderId, setPromotionalSenderId] = useState("");
  const [baseUrl, setBaseUrl] = useState("https://app.notify.lk/api/v1");
  const [testMode, setTestMode] = useState(true);
  const [testAllowList, setTestAllowList] = useState("");
  const [lowBalanceThreshold, setLowBalanceThreshold] = useState("500");

  const [testTo, setTestTo] = useState("");
  const [testMessage, setTestMessage] = useState("EduFlow SMS gateway test message.");
  const [testSenderId, setTestSenderId] = useState("");
  const [testPromotional, setTestPromotional] = useState(false);
  const [lastTestResult, setLastTestResult] = useState<SuperAdminTestSmsResult | null>(null);

  const settingsQuery = useQuery({
    queryKey: ["super-admin-sms-settings"],
    queryFn: () => api<SuperAdminSmsSettings>("/api/super-admin/settings/sms"),
  });

  const balanceQuery = useQuery({
    queryKey: ["super-admin-sms-balance"],
    queryFn: () => api<SmsGatewayBalance>("/api/super-admin/settings/sms/balance"),
  });

  useEffect(() => {
    if (!settingsQuery.data) return;
    const s = settingsQuery.data;
    setProvider(s.provider);
    setUserId(s.userId);
    setSenderId(s.senderId);
    setPromotionalSenderId(s.promotionalSenderId ?? "");
    setBaseUrl(s.baseUrl);
    setTestMode(s.testMode);
    setTestAllowList(s.testAllowList.join(", "));
    setLowBalanceThreshold(String(s.lowBalanceThreshold));
    setApiKey("");
    setClearApiKey(false);
  }, [settingsQuery.data]);

  const saveMutation = useMutation({
    mutationFn: () =>
      api<SuperAdminSmsSettings>("/api/super-admin/settings/sms", {
        method: "PUT",
        body: JSON.stringify({
          provider,
          userId,
          apiKey: apiKey.trim() || null,
          senderId,
          promotionalSenderId: promotionalSenderId.trim() || null,
          baseUrl,
          testMode,
          testAllowList,
          lowBalanceThreshold: Number(lowBalanceThreshold) || 0,
          clearApiKey,
        }),
      }),
    onSuccess: (updated) => {
      setApiKey("");
      setClearApiKey(false);
      toast.success(t("superAdmin.smsSettingsSaved"));
      queryClient.setQueryData(["super-admin-sms-settings"], updated);
      void queryClient.invalidateQueries({ queryKey: ["super-admin-sms-balance"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const refreshBalanceMutation = useMutation({
    mutationFn: () => api<SmsGatewayBalance>("/api/super-admin/settings/sms/balance?refresh=true"),
    onSuccess: (fresh) => {
      queryClient.setQueryData(["super-admin-sms-balance"], fresh);
      void queryClient.invalidateQueries({ queryKey: ["super-admin-sms-settings"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const testSmsMutation = useMutation({
    mutationFn: () =>
      api<SuperAdminTestSmsResult>("/api/super-admin/settings/sms/test", {
        method: "POST",
        body: JSON.stringify({
          to: testTo,
          message: testMessage,
          senderId: testSenderId.trim() || null,
          isPromotional: testPromotional,
        }),
      }),
    onSuccess: (res) => {
      setLastTestResult(res);
      if (res.isSkipped) {
        toast.error(t("superAdmin.smsTestSkipped", { reason: res.skipReason ?? "Skipped:TestMode" }));
      } else if (res.succeeded) {
        toast.success(t("superAdmin.smsTestSent", { provider: res.provider, segments: res.segments }));
      } else {
        toast.error(res.errorMessage ?? t("errors.generic"));
      }
      void queryClient.invalidateQueries({ queryKey: ["super-admin-sms-settings"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  if (settingsQuery.isLoading) return <PageSkeleton />;
  if (!settingsQuery.data) {
    return (
      <p className="text-sm">
        {t("errors.load")}{" "}
        <button type="button" className="text-primary" onClick={() => void settingsQuery.refetch()}>
          {t("common.retry")}
        </button>
      </p>
    );
  }

  const settings = settingsQuery.data;
  const balance = balanceQuery.data;
  const keyBadge =
    settings.apiKeySource === "Database"
      ? t("superAdmin.smsApiKeyStatusDb")
      : settings.apiKeySource === "Config"
        ? t("superAdmin.smsApiKeyStatusConfig")
        : t("superAdmin.smsApiKeyStatusNone");

  return (
    <div>
      <PageHeader
        title={t("superAdmin.smsTitle")}
        description={t("superAdmin.smsDescription")}
        crumbs={[
          { label: t("nav.platform"), href: "/admin/dashboard" },
          { label: t("nav.smsGateway") },
        ]}
        actions={
          <Button
            variant="outline"
            disabled={refreshBalanceMutation.isPending}
            onClick={() => refreshBalanceMutation.mutate()}
          >
            <RefreshCw className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
            {t("superAdmin.smsRefreshBalance")}
          </Button>
        }
      />

      <div className="mb-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <StatCard
          label={t("superAdmin.smsBalanceTitle")}
          value={
            balance?.succeeded && balance.balance !== null
              ? `${balance.balance.toFixed(2)} ${balance.currency}`
              : t("superAdmin.smsBalanceUnavailable")
          }
          hint={balance?.fromCache ? t("superAdmin.smsBalanceCached") : t("superAdmin.smsBalanceLive")}
        />
        <StatCard
          label={t("superAdmin.smsProvider")}
          value={settings.provider}
          hint={balance?.isActive === true ? t("superAdmin.smsBalanceActive") : balance?.isActive === false ? t("superAdmin.smsBalanceInactive") : undefined}
        />
        <StatCard label={t("superAdmin.smsSenderId")} value={settings.senderId || "—"} />
        <StatCard
          label={t("superAdmin.smsLowBalanceThreshold")}
          value={`${settings.lowBalanceThreshold.toFixed(2)} LKR`}
          hint={
            settings.lastLowBalanceAlertSentAt
              ? t("superAdmin.smsLastAlertAt", { date: new Date(settings.lastLowBalanceAlertSentAt).toLocaleDateString() })
              : undefined
          }
        />
      </div>

      {balance?.isBelowThreshold ? (
        <div className="mb-4 flex items-center gap-2 rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-foreground">
          <AlertTriangle className="h-4 w-4 shrink-0 text-warning" aria-hidden="true" />
          <span>{t("superAdmin.smsBalanceLowWarning", { threshold: balance.lowBalanceThreshold.toFixed(2) })}</span>
        </div>
      ) : null}

      {balance && !balance.succeeded && balance.errorMessage ? (
        <div className="mb-4 flex items-center gap-2 rounded-md border border-danger/40 bg-danger/10 px-3 py-2 text-xs text-danger">
          <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
          <span>{balance.errorMessage}</span>
        </div>
      ) : null}

      <div className="grid gap-4 lg:grid-cols-[minmax(0,1.4fr)_minmax(0,1fr)]">
        <section className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("superAdmin.smsConfigTitle")}</h2>
          <form
            className="mt-3 grid gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              saveMutation.mutate();
            }}
          >
            <div className="grid gap-3 sm:grid-cols-2">
              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("superAdmin.smsProvider")}</span>
                <select
                  aria-label={t("superAdmin.smsProvider")}
                  className={fieldClass}
                  value={provider}
                  onChange={(e) => setProvider(e.target.value as "Fake" | "NotifyLk")}
                >
                  <option value="Fake">Fake (Development)</option>
                  <option value="NotifyLk">Notify.lk</option>
                </select>
              </label>

              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("superAdmin.smsUserId")}</span>
                <Input value={userId} onChange={(e) => setUserId(e.target.value)} placeholder="12345" />
              </label>
            </div>

            <div className="grid gap-1 text-xs">
              <div className="flex flex-wrap items-center justify-between gap-2">
                <label htmlFor="sms-api-key" className="font-medium text-muted">
                  {t("superAdmin.smsApiKey")}
                </label>
                <span className="inline-flex items-center gap-1 rounded bg-secondary px-2 py-0.5 text-[11px] text-muted">
                  <ShieldCheck className="h-3 w-3" aria-hidden="true" />
                  {keyBadge}
                </span>
              </div>
              <Input
                id="sms-api-key"
                type="password"
                autoComplete="new-password"
                value={apiKey}
                onChange={(e) => {
                  setApiKey(e.target.value);
                  if (e.target.value) setClearApiKey(false);
                }}
                placeholder={t("superAdmin.smsApiKeyPlaceholder")}
              />
              {settings.apiKeySource === "Database" ? (
                <label className="mt-1 inline-flex items-center gap-2 text-xs text-muted">
                  <input
                    type="checkbox"
                    checked={clearApiKey}
                    onChange={(e) => {
                      setClearApiKey(e.target.checked);
                      if (e.target.checked) setApiKey("");
                    }}
                  />
                  <span>{t("superAdmin.smsClearApiKey")}</span>
                </label>
              ) : null}
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("superAdmin.smsSenderId")}</span>
                <Input
                  maxLength={11}
                  value={senderId}
                  onChange={(e) => setSenderId(e.target.value)}
                  placeholder="EduFlow"
                />
              </label>
              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("superAdmin.smsPromotionalSenderId")}</span>
                <Input
                  maxLength={11}
                  value={promotionalSenderId}
                  onChange={(e) => setPromotionalSenderId(e.target.value)}
                  placeholder="Optional"
                />
              </label>
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("superAdmin.smsBaseUrl")}</span>
                <Input
                  value={baseUrl}
                  onChange={(e) => setBaseUrl(e.target.value)}
                  placeholder="https://app.notify.lk/api/v1"
                />
              </label>
              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("superAdmin.smsLowBalanceThreshold")}</span>
                <Input
                  type="number"
                  min={0}
                  step="0.01"
                  value={lowBalanceThreshold}
                  onChange={(e) => setLowBalanceThreshold(e.target.value)}
                />
              </label>
            </div>

            <div className="flex items-center justify-between rounded-md border border-border p-3">
              <div className="pr-3">
                <p className="text-sm font-medium">{t("superAdmin.smsTestMode")}</p>
                <p className="text-xs text-muted">{t("superAdmin.smsTestModeHint")}</p>
              </div>
              <Switch
                checked={testMode}
                aria-label={t("superAdmin.smsTestMode")}
                onCheckedChange={setTestMode}
              />
            </div>

            <label className="grid gap-1 text-xs">
              <span className="font-medium text-muted">{t("superAdmin.smsTestAllowList")}</span>
              <Textarea
                className="min-h-16"
                value={testAllowList}
                onChange={(e) => setTestAllowList(e.target.value)}
                placeholder="0771234567, +94712345678"
              />
              <span className="text-[11px] text-muted">{t("superAdmin.smsTestAllowListHint")}</span>
            </label>

            <div>
              <Button type="submit" disabled={saveMutation.isPending}>
                {saveMutation.isPending ? t("common.saving") : t("common.save")}
              </Button>
            </div>
          </form>
        </section>

        <section className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("superAdmin.smsTestTitle")}</h2>
          <p className="mt-0.5 text-xs text-muted">{t("superAdmin.smsTestDescription")}</p>
          <form
            className="mt-3 grid gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              testSmsMutation.mutate();
            }}
          >
            <label className="grid gap-1 text-xs">
              <span className="font-medium text-muted">{t("superAdmin.smsTestTo")}</span>
              <Input
                value={testTo}
                onChange={(e) => setTestTo(e.target.value)}
                placeholder="0771234567"
                required
              />
            </label>
            <label className="grid gap-1 text-xs">
              <span className="font-medium text-muted">{t("notifications.senderIdLabel")}</span>
              <Input
                maxLength={11}
                value={testSenderId}
                onChange={(e) => setTestSenderId(e.target.value)}
                placeholder={settings.senderId || "NotifyDEMO"}
              />
            </label>
            <label className="grid gap-1 text-xs">
              <span className="font-medium text-muted">{t("superAdmin.smsTestMessage")}</span>
              <Textarea
                className="min-h-20"
                value={testMessage}
                onChange={(e) => setTestMessage(e.target.value)}
                required
              />
            </label>
            <label className="inline-flex items-center gap-2 text-xs">
              <input
                type="checkbox"
                checked={testPromotional}
                onChange={(e) => setTestPromotional(e.target.checked)}
              />
              <span>{t("superAdmin.smsTestPromotional")}</span>
            </label>
            <Button type="submit" disabled={testSmsMutation.isPending}>
              <Send className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
              {t("superAdmin.smsTestButton")}
            </Button>
          </form>

          {lastTestResult ? (
            <div className="mt-3 rounded-md border border-border bg-secondary/50 p-3 text-xs">
              <p className="font-medium">
                {lastTestResult.isSkipped
                  ? lastTestResult.skipReason
                  : lastTestResult.succeeded
                    ? `Sent via ${lastTestResult.provider}`
                    : `Failed (${lastTestResult.errorCode})`}
              </p>
              <p className="mt-1 text-muted">
                Segments: {lastTestResult.segments}
                {lastTestResult.gatewaySegments !== null ? ` · Gateway: ${lastTestResult.gatewaySegments}` : ""}
                {lastTestResult.gatewayCost !== null ? ` · Cost: ${lastTestResult.gatewayCost}` : ""}
              </p>
              {lastTestResult.errorMessage ? (
                <p className="mt-1 text-danger">{lastTestResult.errorMessage}</p>
              ) : null}
            </div>
          ) : null}
        </section>
      </div>

      <section className="mt-4 rounded-lg border border-border bg-card">
        <h2 className="border-b border-border px-4 py-3 text-sm font-semibold">
          {t("superAdmin.smsErrorsTitle")}
        </h2>
        {settings.recentErrors.length === 0 ? (
          <p className="px-4 py-6 text-sm text-muted">{t("superAdmin.smsErrorsEmpty")}</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="border-b border-border bg-secondary/40 text-muted">
                <tr>
                  <th className="px-4 py-2 font-medium">{t("notifications.colTime")}</th>
                  <th className="px-4 py-2 font-medium">Operation</th>
                  <th className="px-4 py-2 font-medium">{t("notifications.colRecipient")}</th>
                  <th className="px-4 py-2 font-medium">Code</th>
                  <th className="px-4 py-2 font-medium">Message</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {settings.recentErrors.map((err) => (
                  <tr key={err.id}>
                    <td className="whitespace-nowrap px-4 py-2 tabular-nums">
                      {new Date(err.occurredAt).toLocaleString()}
                    </td>
                    <td className="px-4 py-2">
                      {err.provider} · {err.operation}
                    </td>
                    <td className="px-4 py-2 tabular-nums">{err.recipient ?? "—"}</td>
                    <td className="px-4 py-2 font-mono">{err.errorCode}</td>
                    <td className="px-4 py-2 text-danger">{err.errorMessage}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
}
