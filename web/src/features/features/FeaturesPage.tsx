import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Lock, RotateCcw, Send } from "lucide-react";
import { useEffect, useState } from "react";
import { t } from "@/config/strings";
import { Link } from "react-router-dom";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import {
  NOTIFICATION_FEATURES,
  NOTIFICATION_MESSAGE_TYPES,
} from "@/domain/catalog";
import { useAuth } from "@/features/auth/AuthProvider";
import { api } from "@/lib/api";
import {
  calculateSmsSegments,
  minutesToTimeInput,
  renderNotificationTemplate,
  timeInputToMinutes,
} from "@/lib/sms";
import type {
  FeatureStatus,
  NotificationSettingsWorkspace,
  NotificationTemplate,
  NotificationTypeConfig,
  TenantNotificationConfig,
  TestSendNotificationResult,
} from "@/lib/types";
import { SmsQuotaCard } from "./SmsQuotaCard";
import { StorageUsageCard } from "./StorageUsageCard";
import { BankAccountSettingsCard } from "./BankAccountSettingsCard";

export function FeaturesPage() {
  const toast = useToast();
  const { isReadOnly, hasPermission } = useAuth();
  const canManage = hasPermission("settings.manage") && !isReadOnly;
  const queryClient = useQueryClient();
  const [locked, setLocked] = useState<string | null>(null);

  const [selectedType, setSelectedType] = useState<string>("AttendancePresent");
  const [selectedLang, setSelectedLang] = useState<string>("en");
  const [draftSubject, setDraftSubject] = useState<string>("");
  const [draftBody, setDraftBody] = useState<string>("");

  const [configDraft, setConfigDraft] = useState<TenantNotificationConfig | null>(null);

  const [testModalOpen, setTestModalOpen] = useState(false);
  const [testType, setTestType] = useState<string>("AttendancePresent");
  const [testLang, setTestLang] = useState<string>("en");
  const [testPhone, setTestPhone] = useState<string>("+94771234567");
  const [testEmail, setTestEmail] = useState<string>("parent@demo.eduflow.lk");

  const featuresQuery = useQuery({
    queryKey: ["features"],
    queryFn: () => api<FeatureStatus[]>("/api/settings/features"),
  });

  const workspaceQuery = useQuery({
    queryKey: ["notification-settings"],
    queryFn: () => api<NotificationSettingsWorkspace>("/api/notifications/settings"),
  });

  useEffect(() => {
    if (workspaceQuery.data?.config && !configDraft) {
      setConfigDraft(workspaceQuery.data.config);
    }
  }, [workspaceQuery.data, configDraft]);

  const currentTemplate: NotificationTemplate | undefined =
    workspaceQuery.data?.templates.find(
      (tpl) => tpl.messageType === selectedType && tpl.language === selectedLang,
    );

  useEffect(() => {
    if (currentTemplate) {
      setDraftSubject(currentTemplate.subject);
      setDraftBody(currentTemplate.body);
    }
  }, [currentTemplate]);

  const featureSwitchMutation = useMutation({
    mutationFn: (input: { featureKey: string; isEnabled: boolean }) =>
      api<FeatureStatus>(`/api/settings/features/${input.featureKey}`, {
        method: "PUT",
        body: JSON.stringify({ isEnabled: input.isEnabled }),
      }),
    onMutate: async (input) => {
      await queryClient.cancelQueries({ queryKey: ["features"] });
      const previous = queryClient.getQueryData<FeatureStatus[]>(["features"]);
      queryClient.setQueryData<FeatureStatus[]>(["features"], (current) =>
        (current ?? []).map((feature) =>
          feature.featureKey === input.featureKey
            ? {
                ...feature,
                isTenantEnabled: input.isEnabled,
                isEffectiveEnabled: feature.isInPlan && input.isEnabled,
              }
            : feature,
        ),
      );
      return { previous };
    },
    onError: (error, _input, context) => {
      if (context?.previous) queryClient.setQueryData(["features"], context.previous);
      toast.error(error instanceof Error ? error.message : t("errors.generic"));
    },
    onSuccess: (updated) => {
      toast.success(
        t("features.saved", {
          feature: t(`feature.${updated.featureKey}`, { defaultValue: updated.featureKey }),
          state: updated.isTenantEnabled ? t("features.on") : t("features.off"),
        }),
      );
      void queryClient.invalidateQueries({ queryKey: ["features"] });
      void queryClient.invalidateQueries({ queryKey: ["subscription"] });
      void queryClient.invalidateQueries({ queryKey: ["notification-settings"] });
    },
  });

  const typeSettingMutation = useMutation({
    mutationFn: (input: {
      messageType: string;
      isEnabled: boolean;
      smsEnabled: boolean;
      emailEnabled: boolean;
      whatsAppEnabled: boolean;
    }) =>
      api<NotificationTypeConfig>(
        `/api/notifications/settings/types/${input.messageType}`,
        {
          method: "PUT",
          body: JSON.stringify({
            isEnabled: input.isEnabled,
            smsEnabled: input.smsEnabled,
            emailEnabled: input.emailEnabled,
            whatsAppEnabled: input.whatsAppEnabled,
          }),
        },
      ),
    onSuccess: (updated) => {
      toast.success(
        t("features.saved", {
          feature: t(`notifications.type.${updated.messageType}`, {
            defaultValue: updated.messageType,
          }),
          state: updated.isEnabled ? t("features.on") : t("features.off"),
        }),
      );
      void queryClient.invalidateQueries({ queryKey: ["notification-settings"] });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : t("errors.generic"));
    },
  });

  const saveConfigMutation = useMutation({
    mutationFn: (input: TenantNotificationConfig) =>
      api<TenantNotificationConfig>("/api/notifications/settings/config", {
        method: "PUT",
        body: JSON.stringify(input),
      }),
    onSuccess: (saved) => {
      setConfigDraft(saved);
      toast.success(t("notifications.configSaved"));
      void queryClient.invalidateQueries({ queryKey: ["notification-settings"] });
      void queryClient.invalidateQueries({ queryKey: ["sms-quota"] });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : t("errors.generic"));
    },
  });

  const saveTemplateMutation = useMutation({
    mutationFn: (input: {
      messageType: string;
      language: string;
      subject: string;
      body: string;
    }) =>
      api<NotificationTemplate>(
        `/api/notifications/templates/${input.messageType}/${input.language}`,
        {
          method: "PUT",
          body: JSON.stringify({ subject: input.subject, body: input.body }),
        },
      ),
    onSuccess: () => {
      toast.success(t("notifications.templateSaved"));
      void queryClient.invalidateQueries({ queryKey: ["notification-settings"] });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : t("errors.generic"));
    },
  });

  const resetTemplateMutation = useMutation({
    mutationFn: (input: { messageType: string; language: string }) =>
      api<NotificationTemplate>(
        `/api/notifications/templates/${input.messageType}/${input.language}/reset`,
        {
          method: "POST",
        },
      ),
    onSuccess: (resetTpl) => {
      setDraftSubject(resetTpl.subject);
      setDraftBody(resetTpl.body);
      toast.success(t("notifications.templateReset"));
      void queryClient.invalidateQueries({ queryKey: ["notification-settings"] });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : t("errors.generic"));
    },
  });

  const testSendMutation = useMutation({
    mutationFn: (input: {
      messageType: string;
      language: string;
      recipientPhone: string;
      recipientEmail: string;
    }) =>
      api<TestSendNotificationResult>("/api/notifications/test-send", {
        method: "POST",
        body: JSON.stringify(input),
      }),
    onSuccess: (res) => {
      if (res.sentCount > 0) {
        toast.success(
          t("notifications.testSentToast", { count: res.sentCount }),
        );
      } else if (res.skippedReason) {
        toast.error(
          t("notifications.testSkippedToast", { reason: res.skippedReason }),
        );
      } else {
        toast.success(t("notifications.testQueuedToast"));
      }
      setTestModalOpen(false);
      void queryClient.invalidateQueries({ queryKey: ["notification-settings"] });
      void queryClient.invalidateQueries({ queryKey: ["notification-messages"] });
      void queryClient.invalidateQueries({ queryKey: ["sms-quota"] });
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : t("errors.generic"));
    },
  });

  if (featuresQuery.isLoading) return <PageSkeleton />;
  if (!featuresQuery.data) {
    return (
      <p className="text-sm">
        {t("errors.load")}{" "}
        <button
          type="button"
          className="text-primary"
          onClick={() => void featuresQuery.refetch()}
        >
          {t("common.retry")}
        </button>
      </p>
    );
  }

  const notifications = featuresQuery.data.filter((feature) =>
    NOTIFICATION_FEATURES.has(feature.featureKey),
  );
  const modules = featuresQuery.data.filter(
    (feature) => !NOTIFICATION_FEATURES.has(feature.featureKey),
  );

  const workspace = workspaceQuery.data;
  const sampleVars = workspace?.sampleVariables ?? {
    studentName: "Nimal Perera",
    studentCode: "S000001",
    className: "2026 O/L Combined Maths",
    amount: "LKR 3,500.00",
    dueDate: "2026-11-25",
    receiptNo: "RCP-000142",
    examName: "Term 3 Combined Maths",
    examDate: "2026-12-05",
    marks: "86%",
    grade: "A",
    instituteName: "Royal Vidyalaya",
    date: "2026-11-17",
    time: "04:05 PM",
    message: "Classes on Poya day will be held online at 8:00 AM.",
  };

  const renderedPreviewSubject = renderNotificationTemplate(
    draftSubject,
    sampleVars,
  );
  const renderedPreviewBody = renderNotificationTemplate(draftBody, sampleVars);
  const liveSmsCalc = calculateSmsSegments(renderedPreviewBody);

  return (
    <div>
      <PageHeader
        title={t("features.title")}
        description={t("features.description")}
        crumbs={[
          { label: t("nav.settings"), href: "/app/settings/features" },
          { label: t("nav.features") },
        ]}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            <Link
              to="/app/settings/messages"
              className="inline-flex h-9 items-center rounded-md border border-border bg-card px-3 text-xs font-medium hover:bg-secondary"
            >
              {t("notifications.viewMessageLog")}
            </Link>
            <Button
              onClick={() => setTestModalOpen(true)}
              disabled={!canManage}
            >
              <Send className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
              {t("notifications.testSendButton")}
            </Button>
          </div>
        }
      />

      {workspace?.quota ? (
        <SmsQuotaCard quota={workspace.quota} showLogLink />
      ) : null}
      <StorageUsageCard />
      <BankAccountSettingsCard disabled={!canManage} />

      <FeatureGroup
        title={t("features.modules")}
        features={modules}
        disabled={!canManage}
        onToggle={(feature, isEnabled) => {
          if (!feature.isInPlan) {
            setLocked(feature.featureKey);
            return;
          }
          featureSwitchMutation.mutate({
            featureKey: feature.featureKey,
            isEnabled,
          });
        }}
      />

      <FeatureGroup
        title={t("features.notifications")}
        features={notifications}
        disabled={!canManage}
        onToggle={(feature, isEnabled) => {
          if (!feature.isInPlan) {
            setLocked(feature.featureKey);
            return;
          }
          featureSwitchMutation.mutate({
            featureKey: feature.featureKey,
            isEnabled,
          });
        }}
      />

      {workspace ? (
        <>
          <section className="mb-4 rounded-lg border border-border bg-card">
            <div className="border-b border-border px-4 py-3">
              <h2 className="text-sm font-semibold">
                {t("notifications.messageTypesTitle")}
              </h2>
              <p className="text-xs text-muted">
                {t("notifications.messageTypesDescription")}
              </p>
            </div>
            <div className="divide-y divide-border">
              {workspace.messageTypes.map((mt) => {
                const label = t(`notifications.type.${mt.messageType}`, {
                  defaultValue: mt.messageType,
                });
                const emailAllowed = workspace.allowedChannels.emailInPlan;
                const waAllowed = workspace.allowedChannels.whatsAppInPlan;
                return (
                  <div
                    key={mt.messageType}
                    className="flex flex-wrap items-center justify-between gap-3 px-4 py-3"
                  >
                    <div className="min-w-[200px]">
                      <div className="flex items-center gap-2">
                        <p className="text-sm font-medium">{label}</p>
                        {mt.exemptFromQuietHours ? (
                          <span className="rounded bg-secondary px-1.5 py-0.5 text-[11px] text-muted">
                            {t("notifications.quietHoursExempt")}
                          </span>
                        ) : null}
                      </div>
                      <p className="text-xs text-muted">
                        {!mt.isParentFeatureInPlan
                          ? t("features.locked")
                          : !mt.isParentFeatureEnabled
                            ? t("notifications.parentSwitchOff")
                            : mt.isEffectiveEnabled
                              ? t("features.on")
                              : t("features.off")}
                      </p>
                    </div>

                    <div className="flex flex-wrap items-center gap-4">
                      <label className="inline-flex items-center gap-1.5 text-xs">
                        <input
                          type="checkbox"
                          checked={mt.smsEnabled}
                          disabled={!canManage || !mt.isParentFeatureInPlan}
                          onChange={(e) =>
                            typeSettingMutation.mutate({
                              messageType: mt.messageType,
                              isEnabled: mt.isEnabled,
                              smsEnabled: e.target.checked,
                              emailEnabled: mt.emailEnabled,
                              whatsAppEnabled: mt.whatsAppEnabled,
                            })
                          }
                        />
                        <span>{t("notifications.channel.Sms")}</span>
                      </label>

                      <label className="inline-flex items-center gap-1.5 text-xs">
                        <input
                          type="checkbox"
                          checked={mt.emailEnabled}
                          disabled={!canManage}
                          onChange={(e) => {
                            if (!emailAllowed) {
                              setLocked("EmailNotifications");
                              return;
                            }
                            typeSettingMutation.mutate({
                              messageType: mt.messageType,
                              isEnabled: mt.isEnabled,
                              smsEnabled: mt.smsEnabled,
                              emailEnabled: e.target.checked,
                              whatsAppEnabled: mt.whatsAppEnabled,
                            });
                          }}
                        />
                        <span>{t("notifications.channel.Email")}</span>
                        {!emailAllowed ? (
                          <Lock className="h-3 w-3 text-muted" aria-hidden="true" />
                        ) : null}
                      </label>

                      <label className="inline-flex items-center gap-1.5 text-xs">
                        <input
                          type="checkbox"
                          checked={mt.whatsAppEnabled}
                          disabled={!canManage}
                          onChange={(e) => {
                            if (!waAllowed) {
                              setLocked("WhatsAppMessaging");
                              return;
                            }
                            typeSettingMutation.mutate({
                              messageType: mt.messageType,
                              isEnabled: mt.isEnabled,
                              smsEnabled: mt.smsEnabled,
                              emailEnabled: mt.emailEnabled,
                              whatsAppEnabled: e.target.checked,
                            });
                          }}
                        />
                        <span>{t("notifications.channel.WhatsApp")}</span>
                        {!waAllowed ? (
                          <Lock className="h-3 w-3 text-muted" aria-hidden="true" />
                        ) : null}
                      </label>

                      <button
                        type="button"
                        className="text-xs font-medium text-primary hover:underline"
                        onClick={() => setSelectedType(mt.messageType)}
                      >
                        {t("notifications.editTemplate")}
                      </button>

                      <button
                        type="button"
                        className="text-xs font-medium text-primary hover:underline"
                        onClick={() => {
                          setTestType(mt.messageType);
                          setTestModalOpen(true);
                        }}
                      >
                        {t("notifications.testSendButton")}
                      </button>

                      <Switch
                        checked={mt.isEnabled}
                        disabled={!canManage || !mt.isParentFeatureInPlan}
                        aria-label={label}
                        onCheckedChange={(checked) =>
                          typeSettingMutation.mutate({
                            messageType: mt.messageType,
                            isEnabled: checked,
                            smsEnabled: mt.smsEnabled,
                            emailEnabled: mt.emailEnabled,
                            whatsAppEnabled: mt.whatsAppEnabled,
                          })
                        }
                      />
                    </div>
                  </div>
                );
              })}
            </div>
          </section>

          <section className="mb-4 rounded-lg border border-border bg-card p-4">
            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
              <div>
                <h2 className="text-sm font-semibold">
                  {t("notifications.templateEditorTitle")}
                </h2>
                <p className="text-xs text-muted">
                  {t("notifications.templateEditorDescription")}
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <select
                  aria-label={t("notifications.selectMessageType")}
                  className="h-9 rounded-md border border-border bg-background px-2 text-sm"
                  value={selectedType}
                  onChange={(e) => setSelectedType(e.target.value)}
                >
                  {NOTIFICATION_MESSAGE_TYPES.map((type) => (
                    <option key={type} value={type}>
                      {t(`notifications.type.${type}`, { defaultValue: type })}
                    </option>
                  ))}
                </select>
                <select
                  aria-label={t("notifications.selectLanguage")}
                  className="h-9 rounded-md border border-border bg-background px-2 text-sm"
                  value={selectedLang}
                  onChange={(e) => setSelectedLang(e.target.value)}
                >
                  <option value="en">English</option>
                  <option value="ta">Tamil</option>
                  <option value="si">Sinhala</option>
                </select>
              </div>
            </div>

            <div className="grid gap-4 lg:grid-cols-2">
              <div className="grid gap-3">
                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.emailSubject")}
                  </span>
                  <Input
                    lang={selectedLang}
                    value={draftSubject}
                    disabled={!canManage}
                    onChange={(e) => setDraftSubject(e.target.value)}
                  />
                </label>

                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.templateBody")}
                  </span>
                  <Textarea
                    lang={selectedLang}
                    rows={4}
                    value={draftBody}
                    disabled={!canManage}
                    onChange={(e) => setDraftBody(e.target.value)}
                  />
                </label>

                {currentTemplate?.availableVariables.length ? (
                  <div>
                    <p className="mb-1.5 text-xs font-medium text-muted">
                      {t("notifications.insertVariable")}
                    </p>
                    <div className="flex flex-wrap gap-1.5">
                      {currentTemplate.availableVariables.map((variable) => (
                        <button
                          key={variable}
                          type="button"
                          disabled={!canManage}
                          className="rounded border border-border bg-secondary px-2 py-1 font-mono text-xs hover:bg-primary/10"
                          onClick={() =>
                            setDraftBody((prev) => `${prev}${prev ? " " : ""}${variable}`)
                          }
                        >
                          {variable}
                        </button>
                      ))}
                    </div>
                  </div>
                ) : null}

                <div className="flex flex-wrap items-center gap-2 pt-1">
                  <Button
                    disabled={!canManage || saveTemplateMutation.isPending}
                    onClick={() =>
                      saveTemplateMutation.mutate({
                        messageType: selectedType,
                        language: selectedLang,
                        subject: draftSubject,
                        body: draftBody,
                      })
                    }
                  >
                    {t("notifications.saveTemplate")}
                  </Button>
                  <Button
                    variant="outline"
                    disabled={!canManage || resetTemplateMutation.isPending}
                    onClick={() =>
                      resetTemplateMutation.mutate({
                        messageType: selectedType,
                        language: selectedLang,
                      })
                    }
                  >
                    <RotateCcw className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                    {t("notifications.resetTemplate")}
                  </Button>
                </div>
              </div>

              <div className="rounded-md border border-border bg-background p-3">
                <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
                  <span className="text-xs font-semibold uppercase tracking-wide text-muted">
                    {t("notifications.livePreview")}
                  </span>
                  <span className="rounded border border-border bg-card px-2 py-0.5 text-xs tabular-nums">
                    {liveSmsCalc.encoding} · {liveSmsCalc.characterCount}{" "}
                    {t("notifications.chars")} · {liveSmsCalc.segments}{" "}
                    {t("notifications.segments")} ({liveSmsCalc.remainingInCurrentSegment}{" "}
                    {t("notifications.left")})
                  </span>
                </div>
                <p lang={selectedLang} className="mb-1 text-xs font-medium text-muted">
                  {renderedPreviewSubject}
                </p>
                <p lang={selectedLang} className="whitespace-pre-wrap rounded border border-border bg-card p-3 text-sm">
                  {renderedPreviewBody}
                </p>
              </div>
            </div>
          </section>

          {configDraft ? (
            <section className="mb-4 rounded-lg border border-border bg-card p-4">
              <h2 className="text-sm font-semibold">
                {t("notifications.timingAndQuietHoursTitle")}
              </h2>
              <p className="mb-3 text-xs text-muted">
                {t("notifications.timingAndQuietHoursDescription", {
                  tz: configDraft.timeZone,
                })}
              </p>

              <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
                <div className="flex items-center justify-between rounded-md border border-border p-3">
                  <div>
                    <p className="text-sm font-medium">
                      {t("notifications.quietHoursEnabled")}
                    </p>
                    <p className="text-xs text-muted">
                      {t("notifications.quietHoursHint")}
                    </p>
                  </div>
                  <Switch
                    checked={configDraft.quietHoursEnabled}
                    disabled={!canManage}
                    aria-label={t("notifications.quietHoursEnabled")}
                    onCheckedChange={(checked) =>
                      setConfigDraft({
                        ...configDraft,
                        quietHoursEnabled: checked,
                      })
                    }
                  />
                </div>

                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.quietStart")}
                  </span>
                  <Input
                    type="time"
                    disabled={!canManage}
                    value={minutesToTimeInput(configDraft.quietHoursStartMinutes)}
                    onChange={(e) =>
                      setConfigDraft({
                        ...configDraft,
                        quietHoursStartMinutes: timeInputToMinutes(e.target.value),
                      })
                    }
                  />
                </label>

                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.quietEnd")}
                  </span>
                  <Input
                    type="time"
                    disabled={!canManage}
                    value={minutesToTimeInput(configDraft.quietHoursEndMinutes)}
                    onChange={(e) =>
                      setConfigDraft({
                        ...configDraft,
                        quietHoursEndMinutes: timeInputToMinutes(e.target.value),
                      })
                    }
                  />
                </label>

                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.overQuotaPolicy")}
                  </span>
                  <select
                    aria-label={t("notifications.overQuotaPolicy")}
                    disabled={!canManage}
                    className="h-9 rounded-md border border-border bg-background px-2 text-sm"
                    value={configDraft.smsOverQuotaPolicy}
                    onChange={(e) =>
                      setConfigDraft({
                        ...configDraft,
                        smsOverQuotaPolicy:
                          e.target.value === "Block"
                            ? "Block"
                            : "AllowAndRecordExtra",
                      })
                    }
                  >
                    <option value="AllowAndRecordExtra">
                      {t("notifications.policyAllow")}
                    </option>
                    <option value="Block">{t("notifications.policyBlock")}</option>
                  </select>
                </label>

                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.feeReminderDaysBefore")}
                  </span>
                  <Input
                    type="number"
                    min={0}
                    max={60}
                    disabled={!canManage}
                    value={configDraft.feeReminderDaysBefore}
                    onChange={(e) =>
                      setConfigDraft({
                        ...configDraft,
                        feeReminderDaysBefore: Number(e.target.value) || 0,
                      })
                    }
                  />
                </label>

                <div className="flex items-center justify-between rounded-md border border-border p-3">
                  <span className="text-sm font-medium">
                    {t("notifications.feeReminderOnDueDate")}
                  </span>
                  <Switch
                    checked={configDraft.feeReminderOnDueDate}
                    disabled={!canManage}
                    aria-label={t("notifications.feeReminderOnDueDate")}
                    onCheckedChange={(checked) =>
                      setConfigDraft({
                        ...configDraft,
                        feeReminderOnDueDate: checked,
                      })
                    }
                  />
                </div>

                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.feeReminderDaysOverdue")}
                  </span>
                  <Input
                    type="number"
                    min={0}
                    max={90}
                    disabled={!canManage}
                    value={configDraft.feeReminderDaysAfterOverdue}
                    onChange={(e) =>
                      setConfigDraft({
                        ...configDraft,
                        feeReminderDaysAfterOverdue: Number(e.target.value) || 0,
                      })
                    }
                  />
                </label>

                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.examReminderDaysBefore")}
                  </span>
                  <Input
                    type="number"
                    min={0}
                    max={60}
                    disabled={!canManage}
                    value={configDraft.examReminderDaysBefore}
                    onChange={(e) =>
                      setConfigDraft({
                        ...configDraft,
                        examReminderDaysBefore: Number(e.target.value) || 0,
                      })
                    }
                  />
                </label>
                <label className="grid gap-1 text-xs">
                  <span className="font-medium text-muted">
                    {t("notifications.examReminderHoursBefore")}
                  </span>
                  <Input
                    type="number"
                    min={0}
                    max={23}
                    disabled={!canManage}
                    value={configDraft.examReminderHoursBefore ?? 0}
                    onChange={(e) =>
                      setConfigDraft({
                        ...configDraft,
                        examReminderHoursBefore: Number(e.target.value) || 0,
                      })
                    }
                  />
                </label>
              </div>

              <div className="mt-3">
                <Button
                  disabled={!canManage || saveConfigMutation.isPending}
                  onClick={() => saveConfigMutation.mutate(configDraft)}
                >
                  {t("common.save")}
                </Button>
              </div>
            </section>
          ) : null}
        </>
      ) : null}

      <Dialog
        open={Boolean(locked)}
        onOpenChange={(open) => !open && setLocked(null)}
        title={t("features.upgrade")}
        description={t("features.upgradeBody", {
          feature: t(`feature.${locked ?? ""}`, { defaultValue: locked ?? "" }),
        })}
      >
        <button
          type="button"
          className="text-sm text-primary"
          onClick={() => setLocked(null)}
        >
          {t("common.close")}
        </button>
      </Dialog>

      <Dialog
        open={testModalOpen}
        onOpenChange={setTestModalOpen}
        title={t("notifications.testSendTitle")}
        description={t("notifications.testSendDescription")}
      >
        <div className="grid gap-3">
          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">
              {t("notifications.selectMessageType")}
            </span>
            <select
              aria-label={t("notifications.selectMessageType")}
              className="h-9 rounded-md border border-border bg-background px-2 text-sm"
              value={testType}
              onChange={(e) => setTestType(e.target.value)}
            >
              {NOTIFICATION_MESSAGE_TYPES.map((type) => (
                <option key={type} value={type}>
                  {t(`notifications.type.${type}`, { defaultValue: type })}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">
              {t("notifications.selectLanguage")}
            </span>
            <select
              aria-label={t("notifications.selectLanguage")}
              className="h-9 rounded-md border border-border bg-background px-2 text-sm"
              value={testLang}
              onChange={(e) => setTestLang(e.target.value)}
            >
              <option value="en">English</option>
              <option value="ta">Tamil</option>
              <option value="si">Sinhala</option>
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("tenants.phone")}</span>
            <Input
              value={testPhone}
              onChange={(e) => setTestPhone(e.target.value)}
            />
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("auth.email")}</span>
            <Input
              value={testEmail}
              onChange={(e) => setTestEmail(e.target.value)}
            />
          </label>

          <div className="flex justify-end gap-2 pt-2">
            <Button variant="outline" onClick={() => setTestModalOpen(false)}>
              {t("common.cancel")}
            </Button>
            <Button
              disabled={testSendMutation.isPending}
              onClick={() =>
                testSendMutation.mutate({
                  messageType: testType,
                  language: testLang,
                  recipientPhone: testPhone,
                  recipientEmail: testEmail,
                })
              }
            >
              {t("notifications.sendNow")}
            </Button>
          </div>
        </div>
      </Dialog>
    </div>
  );
}

function FeatureGroup({
  title,
  features,
  disabled,
  onToggle,
}: {
  title: string;
  features: FeatureStatus[];
  disabled: boolean;
  onToggle: (feature: FeatureStatus, isEnabled: boolean) => void;
}) {
  return (
    <section className="mb-4 rounded-lg border border-border bg-card">
      <h2 className="border-b border-border px-4 py-3 text-sm font-semibold">
        {title}
      </h2>
      <ul>
        {features.map((feature) => {
          const label = t(`feature.${feature.featureKey}`, {
            defaultValue: feature.featureKey,
          });
          return (
            <li
              key={feature.featureKey}
              className="flex items-center justify-between gap-3 border-b border-border px-4 py-3 last:border-0"
            >
              <div>
                <p className="text-sm font-medium">{label}</p>
                <p className="text-xs text-muted">
                  {!feature.isInPlan
                    ? t("features.locked")
                    : feature.isOverridden
                      ? t("features.override")
                      : feature.isEffectiveEnabled
                        ? t("features.on")
                        : t("features.off")}
                </p>
              </div>
              {feature.isInPlan ? (
                <Switch
                  checked={feature.isTenantEnabled}
                  disabled={disabled}
                  aria-label={label}
                  onCheckedChange={(checked) => onToggle(feature, checked)}
                />
              ) : (
                <button
                  type="button"
                  className="inline-flex items-center gap-1 text-xs text-muted"
                  onClick={() => onToggle(feature, true)}
                >
                  <Lock className="h-3.5 w-3.5" aria-hidden="true" />
                  {t("features.upgrade")}
                </button>
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
