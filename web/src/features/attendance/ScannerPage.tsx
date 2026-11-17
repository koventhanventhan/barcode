import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { BrowserMultiFormatReader } from "@zxing/browser";
import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { PageHeader } from "@/components/page-header";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useAuth } from "@/features/auth/AuthProvider";
import { fieldClass, type ClassSchedule } from "@/features/academics/shared";
import { api, ApiError } from "@/lib/api";
import { connectAttendance, playScanCue, type AttendanceBoard, type ClassSession, type ScanResult } from "./api";

export function ScannerPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { hasPermission, isReadOnly } = useAuth();
  const inputRef = useRef<HTMLInputElement>(null);
  const lastCode = useRef("");
  const videoRef = useRef<HTMLVideoElement>(null);
  const [scheduleId, setScheduleId] = useState("");
  const [sessionId, setSessionId] = useState("");
  const [code, setCode] = useState("");
  const [cameraOn, setCameraOn] = useState(false);
  const [result, setResult] = useState<ScanResult | null>(null);
  const [recent, setRecent] = useState<ScanResult[]>([]);
  const [board, setBoard] = useState<AttendanceBoard | null>(null);
  const schedules = useQuery({ queryKey: ["class-schedules"], queryFn: () => api<ClassSchedule[]>("/api/class-schedules") });
  const sessions = useQuery({ queryKey: ["attendance-sessions"], queryFn: () => api<ClassSession[]>("/api/attendance/sessions") });
  const active = sessions.data?.find((item) => item.id === sessionId) ?? sessions.data?.find((item) => item.status === "Started");

  useEffect(() => {
    inputRef.current?.focus();
  }, [sessionId, result]);

  useEffect(() => {
    if (!hasPermission("attendance.mark")) return;
    const connection = connectAttendance((next) => {
      setBoard(next);
      void queryClient.invalidateQueries({ queryKey: ["attendance-sessions"] });
    });
    void connection.start().catch(() => undefined);
    return () => {
      void connection.stop();
    };
  }, [hasPermission, queryClient]);

  useEffect(() => {
    if (!cameraOn || !videoRef.current) return;
    const reader = new BrowserMultiFormatReader();
    let stopped = false;
    const controls = reader.decodeFromVideoDevice(undefined, videoRef.current, (scan) => {
      const text = scan?.getText()?.trim();
      if (!text || stopped || text === lastCode.current) return;
      lastCode.current = text;
      window.setTimeout(() => {
        if (lastCode.current === text) lastCode.current = "";
      }, 1500);
      void submit(text);
    });
    return () => {
      stopped = true;
      void controls.then((control) => control.stop());
    };
    // submit is recreated each render; the camera stays up until toggled off
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [cameraOn]);

  const action = useMutation({
    mutationFn: async (kind: "open" | "start" | "stop" | "cancel") => {
      let id = sessionId || active?.id || "";
      if (kind === "open" || !id) {
        const opened = await api<ClassSession>("/api/attendance/sessions", {
          method: "POST",
          body: JSON.stringify({ classScheduleId: scheduleId }),
        });
        id = opened.id;
        setSessionId(opened.id);
        if (kind === "open") return opened;
      }
      return api<ClassSession>(`/api/attendance/sessions/${id}/${kind}`, { method: "POST", body: "{}" });
    },
    onSuccess: (session) => {
      setSessionId(session.id);
      toast.success(t("attendance.saved"));
      void queryClient.invalidateQueries({ queryKey: ["attendance-sessions"] });
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : t("errors.generic")),
  });

  async function submit(raw: string) {
    const value = raw.trim();
    if (!value) return;
    const id = sessionId || active?.id;
    try {
      const scanned = await api<ScanResult>("/api/attendance/scan", {
        method: "POST",
        body: JSON.stringify({ code: value, sessionId: id || null }),
      });
      setResult(scanned);
      setRecent((current) => [scanned, ...current].slice(0, 8));
      playScanCue(scanned.outcome === "Marked");
      if (scanned.sessionId) setSessionId(scanned.sessionId);
    } catch (error) {
      const message = error instanceof ApiError ? error.message : t("errors.generic");
      setResult({ outcome: "AlreadyMarked", message, sessionId: id ?? null, studentName: null, studentCode: value, status: null, markedAtLabel: null });
      playScanCue(false);
    } finally {
      setCode("");
      inputRef.current?.focus();
    }
  }

  if (!hasPermission("attendance.mark")) return <p className="text-sm">{t("auth.deniedBody")}</p>;
  const shown = board && (board.sessionId === (sessionId || active?.id)) ? board : null;
  const tone = result?.outcome === "Marked" ? "border-success bg-success/10" : result ? "border-danger bg-danger/10" : "border-border bg-card";

  return (
    <div className="mx-auto grid max-w-lg gap-4">
      <PageHeader
        title={t("attendance.scannerTitle")}
        description={t("attendance.scannerDescription")}
        crumbs={[{ label: t("nav.attendance") }]}
        actions={<Button asChild variant="outline"><Link to="/app/attendance/register">{t("nav.register")}</Link></Button>}
      />
      <label className="grid gap-1 text-[13px] font-medium">
        {t("attendance.class")}
        <select className={fieldClass} value={scheduleId} onChange={(event) => setScheduleId(event.target.value)}>
          <option value="">{t("attendance.chooseClass")}</option>
          {(schedules.data ?? []).filter((item) => item.isActive).map((item) => (
            <option key={item.id} value={item.id}>{item.name}</option>
          ))}
        </select>
      </label>
      <div className="flex flex-wrap gap-2">
        <Button type="button" disabled={isReadOnly || !scheduleId || action.isPending} onClick={() => action.mutate("start")}>{t("attendance.start")}</Button>
        <Button type="button" variant="outline" disabled={isReadOnly || !(sessionId || active) || action.isPending} onClick={() => action.mutate("stop")}>{t("attendance.stop")}</Button>
        <Button type="button" variant="outline" disabled={isReadOnly || !(sessionId || active) || action.isPending} onClick={() => action.mutate("cancel")}>{t("attendance.cancel")}</Button>
      </div>
      <p className="text-sm tabular-nums text-muted">
        {t("attendance.counts", {
          present: shown?.presentCount ?? active?.presentCount ?? 0,
          late: shown?.lateCount ?? active?.lateCount ?? 0,
          absent: shown?.absentCount ?? active?.absentCount ?? 0,
        })}
        {active?.joinCode ? ` · ${t("attendance.joinCode")} ${active.joinCode}` : ""}
      </p>
      <form onSubmit={(event) => { event.preventDefault(); void submit(code); }}>
        <Input
          ref={inputRef}
          value={code}
          autoFocus
          autoComplete="off"
          aria-label={t("attendance.code")}
          placeholder={t("attendance.code")}
          className="h-14 text-lg"
          onChange={(event) => setCode(event.target.value)}
        />
      </form>
      <Button type="button" variant="outline" onClick={() => setCameraOn((value) => !value)}>
        {cameraOn ? t("attendance.cameraOff") : t("attendance.cameraOn")}
      </Button>
      {cameraOn ? <video ref={videoRef} className="aspect-[3/4] w-full rounded-lg bg-foreground object-cover" muted playsInline /> : null}
      <div className={`rounded-lg border p-4 ${tone}`} role="status">
        <p className="text-xl font-semibold">{result?.studentName ?? (result ? result.studentCode : t("attendance.waiting"))}</p>
        <p className="mt-1 text-sm">{result?.message ?? t("attendance.waitingHint")}</p>
      </div>
      <ul className="grid gap-2">
        {recent.map((item, index) => (
          <li key={`${item.studentCode}-${index}`} className="flex items-center justify-between rounded-md border border-border px-3 py-2 text-sm">
            <span>{item.studentName ?? item.studentCode}</span>
            <span className="tabular-nums text-muted">{item.markedAtLabel ?? item.status}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
