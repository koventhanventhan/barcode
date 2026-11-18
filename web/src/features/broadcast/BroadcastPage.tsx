import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Megaphone } from "lucide-react";
import { useMemo, useState } from "react";
import { t } from "@/config/strings";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { FeatureGate, useFeature } from "@/components/feature-gate";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { useAuth } from "@/features/auth/AuthProvider";
import { ApiError, api } from "@/lib/api";

type Named = { id: string; name: string; isEnabled?: boolean; isActive?: boolean };
type StudentHit = { id: string; fullName: string; studentCode: string; guardianPhone: string };
type StudentPage = { items: StudentHit[] };
type Preview = {
  recipientCount: number;
  skippedCount: number;
  estimatedSegments: number;
  sampleSegments: number;
  sampleBody: string;
  encoding: string;
  isUnicode: boolean;
  characterLimit: number;
  usedSegments: number;
  monthlyQuota: number | null;
  remainingSegments: number;
  wouldExceedQuota: boolean;
  overQuotaPolicy: string;
  quietHoursActive: boolean;
  quietHoursResumeAt: string | null;
  promotionalSenderIdConfigured?: boolean;
};
type BroadcastSmsStatus = {
  promotionalSenderIdConfigured: boolean;
  effectivePromotionalSenderId: string | null;
};
type Campaign = {
  id: string;
  title: string;
  audience: string;
  channel: string;
  status: string;
  recipientCount: number;
  estimatedSegments: number;
  scheduledAt: string;
  createdAt: string;
};
type History = { items: Campaign[] };
type Detail = {
  campaign: Campaign & { message: string };
  recipients: { id: string; studentName: string; recipient: string; status: string; smsSegments: number; statusReason: string | null }[];
};

const audiences = ["All", "Level", "Batch", "BatchGroup", "SelectedStudents", "FeeOverdue"] as const;
type Audience = (typeof audiences)[number];

const fieldClass =
  "h-9 w-full rounded-md border border-border bg-card px-3 text-sm text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring";

export function BroadcastPage() {
  const blocked = (
    <p className="text-sm text-muted">{t("broadcast.disabled")}</p>
  );
  return (
    <FeatureGate feature="BroadcastMessages" fallback={blocked}>
      <BroadcastComposer />
    </FeatureGate>
  );
}

function BroadcastComposer() {
  const toast = useToast();
  const auth = useAuth();
  const queryClient = useQueryClient();
  const batchGroups = useFeature("BatchGroups");
  const email = useFeature("EmailNotifications");
  const whatsApp = useFeature("WhatsAppMessaging");
  const canSend = auth.hasPermission("broadcasts.send");

  const [audience, setAudience] = useState<Audience>("All");
  const [audienceId, setAudienceId] = useState("");
  const [channel, setChannel] = useState("Sms");
  const [language, setLanguage] = useState("en");
  const [subject, setSubject] = useState("");
  const [bodyTemplate, setBodyTemplate] = useState("{instituteName}: {message}");
  const [message, setMessage] = useState("");
  const [scheduledAt, setScheduledAt] = useState("");
  const [search, setSearch] = useState("");
  const [selected, setSelected] = useState<string[]>([]);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [openId, setOpenId] = useState<string | null>(null);

  const levels = useQuery({
    queryKey: ["education-levels"],
    queryFn: () => api<Named[]>("/api/education-levels"),
  });
  const batches = useQuery({
    queryKey: ["batches"],
    queryFn: () => api<Named[]>("/api/batches"),
  });
  const groups = useQuery({
    queryKey: ["batch-groups"],
    enabled: batchGroups.isEnabled,
    queryFn: () => api<Named[]>("/api/batch-groups"),
  });
  const students = useQuery({
    queryKey: ["broadcast-students", search],
    enabled: audience === "SelectedStudents",
    queryFn: () => api<StudentPage>(`/api/students?search=${encodeURIComponent(search)}&status=Active&pageSize=20`),
  });
  const history = useQuery({
    queryKey: ["broadcasts"],
    queryFn: () => api<History>("/api/broadcasts"),
  });
  const detail = useQuery({
    queryKey: ["broadcast", openId],
    enabled: Boolean(openId),
    queryFn: () => api<Detail>(`/api/broadcasts/${openId}`),
  });
  const smsStatus = useQuery({
    queryKey: ["broadcast-sms-status"],
    queryFn: () => api<BroadcastSmsStatus>("/api/broadcasts/sms-status"),
  });

  const payload = useMemo(
    () => ({
      audience,
      audienceId: audienceId || null,
      studentIds: selected,
      channel,
      language,
      subject: subject.trim() || null,
      bodyTemplate: bodyTemplate.trim() || null,
      message,
      scheduledAt: scheduledAt ? new Date(scheduledAt).toISOString() : null,
    }),
    [audience, audienceId, bodyTemplate, channel, language, message, scheduledAt, selected, subject],
  );

  const needsId = audience === "Level" || audience === "Batch" || audience === "BatchGroup";
  const needsStudents = audience === "SelectedStudents";
  const previewReady = message.trim().length > 0 && (!needsId || Boolean(audienceId)) && (!needsStudents || selected.length > 0);
  const preview = useQuery({
    queryKey: ["broadcast-preview", payload],
    enabled: previewReady,
    queryFn: () => api<Preview>("/api/broadcasts/preview", { method: "POST", body: JSON.stringify(payload) }),
  });

  const send = useMutation({
    mutationFn: () => api<{ campaignId: string }>("/api/broadcasts", { method: "POST", body: JSON.stringify(payload) }),
    onSuccess: async () => {
      setConfirmOpen(false);
      setMessage("");
      toast.success(t("broadcast.sent"));
      await queryClient.invalidateQueries({ queryKey: ["broadcasts"] });
    },
    onError: (error: Error) => toast.error(error.message),
  });

  if (levels.isLoading || batches.isLoading || history.isLoading) return <PageSkeleton />;

  const audienceOptions = audiences.filter((item) => item !== "BatchGroup" || batchGroups.isEnabled);
  const previewError = preview.error instanceof ApiError ? preview.error.message : preview.error ? t("errors.generic") : null;
  const quotaLabel = preview.data?.monthlyQuota == null
    ? t("common.unlimited")
    : `${preview.data.remainingSegments} / ${preview.data.monthlyQuota}`;

  const showPromotionalSenderWarning =
    channel === "Sms"
    && (smsStatus.data?.promotionalSenderIdConfigured === false
      || preview.data?.promotionalSenderIdConfigured === false);

  return (
    <div>
      <PageHeader
        title={t("broadcast.title")}
        description={t("broadcast.description")}
        crumbs={[{ label: t("nav.broadcast") }]}
      />
      {!canSend ? <p className="mb-4 text-sm text-muted">{t("broadcast.noPermission")}</p> : null}
      {showPromotionalSenderWarning ? (
        <div role="alert" className="mb-4 rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-foreground">
          {t("broadcast.promotionalSenderWarning")}
        </div>
      ) : null}
      <div className="grid gap-4 lg:grid-cols-[minmax(0,1.4fr)_minmax(16rem,0.8fr)]">
        <form
          className="space-y-3 rounded-lg border border-border bg-card p-4"
          onSubmit={(event) => {
            event.preventDefault();
            if (preview.data && preview.data.recipientCount > 0) setConfirmOpen(true);
          }}
        >
          <label className="block text-xs font-medium">
            {t("broadcast.audience")}
            <select className={`${fieldClass} mt-1`} value={audience} onChange={(event) => { setAudience(event.target.value as Audience); setAudienceId(""); }}>
              {audienceOptions.map((item) => (
                <option key={item} value={item}>{t(`broadcast.audience_${item}`)}</option>
              ))}
            </select>
          </label>
          {audience === "Level" ? (
            <label className="block text-xs font-medium">
              {t("nav.levels")}
              <select className={`${fieldClass} mt-1`} value={audienceId} onChange={(event) => setAudienceId(event.target.value)}>
                <option value="">{t("broadcast.choose")}</option>
                {(levels.data ?? []).filter((level) => level.isEnabled !== false).map((level) => (
                  <option key={level.id} value={level.id}>{level.name}</option>
                ))}
              </select>
            </label>
          ) : null}
          {audience === "Batch" ? (
            <label className="block text-xs font-medium">
              {t("nav.batches")}
              <select className={`${fieldClass} mt-1`} value={audienceId} onChange={(event) => setAudienceId(event.target.value)}>
                <option value="">{t("broadcast.choose")}</option>
                {(batches.data ?? []).map((batch) => (
                  <option key={batch.id} value={batch.id}>{batch.name}</option>
                ))}
              </select>
            </label>
          ) : null}
          {audience === "BatchGroup" ? (
            <label className="block text-xs font-medium">
              {t("broadcast.batchGroup")}
              <select className={`${fieldClass} mt-1`} value={audienceId} onChange={(event) => setAudienceId(event.target.value)}>
                <option value="">{t("broadcast.choose")}</option>
                {(groups.data ?? []).map((group) => (
                  <option key={group.id} value={group.id}>{group.name}</option>
                ))}
              </select>
            </label>
          ) : null}
          {audience === "SelectedStudents" ? (
            <div>
              <label className="block text-xs font-medium" htmlFor="broadcast-student-search">{t("broadcast.findStudents")}</label>
              <Input id="broadcast-student-search" className="mt-1" value={search} onChange={(event) => setSearch(event.target.value)} />
              <ul className="mt-2 max-h-40 overflow-auto rounded-md border border-border">
                {(students.data?.items ?? []).map((student) => (
                  <li key={student.id} className="flex items-center gap-2 border-b border-border px-3 py-2 text-sm last:border-0">
                    <input
                      type="checkbox"
                      checked={selected.includes(student.id)}
                      onChange={() => setSelected((current) => current.includes(student.id) ? current.filter((id) => id !== student.id) : [...current, student.id])}
                      aria-label={student.fullName}
                    />
                    <span>{student.fullName}</span>
                    <span className="text-xs text-muted">{student.studentCode}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : null}
          <div className="grid gap-3 sm:grid-cols-2">
            <label className="block text-xs font-medium">
              {t("broadcast.channel")}
              <select className={`${fieldClass} mt-1`} value={channel} onChange={(event) => setChannel(event.target.value)}>
                <option value="Sms">{t("broadcast.channelSms")}</option>
                <option value="Email" disabled={!email.isEnabled}>{t("broadcast.channelEmail")}</option>
                <option value="WhatsApp" disabled={!whatsApp.isEnabled}>{t("broadcast.channelWhatsApp")}</option>
              </select>
            </label>
            <label className="block text-xs font-medium">
              {t("broadcast.language")}
              <select className={`${fieldClass} mt-1`} value={language} onChange={(event) => setLanguage(event.target.value)}>
                <option value="en">English</option>
                <option value="ta">Tamil</option>
                <option value="si">Sinhala</option>
              </select>
            </label>
          </div>
          <label className="block text-xs font-medium">
            {t("broadcast.subject")}
            <Input className="mt-1" value={subject} onChange={(event) => setSubject(event.target.value)} placeholder={t("broadcast.subjectPlaceholder")} />
          </label>
          <label className="block text-xs font-medium">
            {t("broadcast.template")}
            <Textarea lang={language} className="mt-1 min-h-16" value={bodyTemplate} onChange={(event) => setBodyTemplate(event.target.value)} />
          </label>
          <p className="text-xs text-muted">{t("broadcast.variables")}</p>
          <label className="block text-xs font-medium">
            {t("broadcast.message")}
            <Textarea lang={language} className="mt-1" value={message} onChange={(event) => setMessage(event.target.value)} required />
          </label>
          <label className="block text-xs font-medium">
            {t("broadcast.schedule")}
            <Input className="mt-1" type="datetime-local" value={scheduledAt} onChange={(event) => setScheduledAt(event.target.value)} />
          </label>
          <Button type="submit" disabled={!canSend || !preview.data || preview.data.recipientCount === 0 || send.isPending}>
            <Megaphone className="h-4 w-4" aria-hidden="true" />
            {t("broadcast.review")}
          </Button>
        </form>
        <aside className="rounded-lg border border-border bg-card p-4">
          <h2 className="text-sm font-semibold">{t("broadcast.estimate")}</h2>
          {preview.isFetching ? <p className="mt-3 text-sm text-muted">{t("common.loading")}</p> : null}
          {previewError ? <p className="mt-3 text-sm text-danger">{previewError}</p> : null}
          {preview.data ? (
            <dl className="mt-3 space-y-2 text-sm">
              <div className="flex justify-between gap-3"><dt>{t("broadcast.recipientsLabel")}</dt><dd className="tabular-nums font-medium">{t("broadcast.recipients", { count: preview.data.recipientCount })}</dd></div>
              <div className="flex justify-between gap-3"><dt>{t("broadcast.skippedLabel")}</dt><dd className="tabular-nums">{preview.data.skippedCount}</dd></div>
              <div className="flex justify-between gap-3"><dt>{t("broadcast.segmentsLabel")}</dt><dd className="tabular-nums font-medium">{t("broadcast.segments", { count: preview.data.estimatedSegments })}</dd></div>
              <div className="flex justify-between gap-3"><dt>{t("broadcast.perMessage")}</dt><dd className="tabular-nums">{preview.data.sampleSegments} · {preview.data.encoding} · {preview.data.characterLimit}</dd></div>
              <div className="flex justify-between gap-3"><dt>{t("broadcast.quotaLeft")}</dt><dd className="tabular-nums">{quotaLabel}</dd></div>
              {preview.data.isUnicode ? <p className="text-xs text-muted">{t("broadcast.unicodeHint")}</p> : <p className="text-xs text-muted">{t("broadcast.gsmHint")}</p>}
              {preview.data.wouldExceedQuota ? <p className="text-xs text-warning">{t("broadcast.overQuota", { policy: preview.data.overQuotaPolicy })}</p> : null}
              {preview.data.quietHoursActive ? <p className="text-xs text-warning">{t("broadcast.quietHours")}</p> : null}
              {preview.data.sampleBody ? <p lang={language} className="rounded-md border border-border bg-secondary px-3 py-2 text-xs">{preview.data.sampleBody}</p> : null}
            </dl>
          ) : !previewError ? <p className="mt-3 text-sm text-muted">{t("broadcast.estimateEmpty")}</p> : null}
        </aside>
      </div>
      <section className="mt-4 rounded-lg border border-border bg-card">
        <h2 className="border-b border-border px-4 py-3 text-sm font-semibold">{t("broadcast.history")}</h2>
        {(history.data?.items ?? []).length === 0 ? (
          <p className="px-4 py-6 text-sm text-muted">{t("broadcast.historyEmpty")}</p>
        ) : (
          <ul>
            {(history.data?.items ?? []).map((item) => (
              <li key={item.id} className="border-b border-border last:border-0">
                <button type="button" className="flex w-full flex-wrap items-center justify-between gap-2 px-4 py-3 text-left text-sm hover:bg-secondary" onClick={() => setOpenId(openId === item.id ? null : item.id)}>
                  <span className="font-medium">{item.title}</span>
                  <span className="text-xs text-muted">{item.channel} · {item.status} · {t("broadcast.recipients", { count: item.recipientCount })}</span>
                </button>
                {openId === item.id && detail.data?.campaign.id === item.id ? (
                  <ul className="border-t border-border bg-secondary/40 px-4 py-2">
                    {detail.data.recipients.map((recipient) => (
                      <li key={recipient.id} className="flex flex-wrap justify-between gap-2 py-1 text-xs">
                        <span>{recipient.studentName} · {recipient.recipient || "—"}</span>
                        <span className="tabular-nums">{recipient.status}{recipient.smsSegments ? ` · ${recipient.smsSegments}` : ""}{recipient.statusReason ? ` · ${recipient.statusReason}` : ""}</span>
                      </li>
                    ))}
                  </ul>
                ) : null}
              </li>
            ))}
          </ul>
        )}
      </section>
      <ConfirmDialog
        open={confirmOpen}
        title={t("broadcast.confirmTitle")}
        description={t("broadcast.confirmBody", {
          count: preview.data?.recipientCount ?? 0,
          segments: preview.data?.estimatedSegments ?? 0,
          channel,
        })}
        confirmLabel={t("broadcast.send")}
        pending={send.isPending}
        onConfirm={() => send.mutate()}
        onOpenChange={setConfirmOpen}
      />
    </div>
  );
}
