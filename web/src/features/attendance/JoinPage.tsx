import { useState, type FormEvent } from "react";
import { t } from "@/config/strings";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { ApiError } from "@/lib/api";
import type { ScanResult } from "./api";

export function JoinPage() {
  const [institute, setInstitute] = useState("");
  const [joinCode, setJoinCode] = useState("");
  const [studentCode, setStudentCode] = useState("");
  const [message, setMessage] = useState("");
  const [ok, setOk] = useState(false);
  const [pending, setPending] = useState(false);

  async function submit(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    try {
      const response = await fetch("/api/attendance/join", {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Tenant": institute.trim() },
        body: JSON.stringify({ joinCode, studentCode }),
      });
      const body = (await response.json()) as ScanResult & { code?: string; message?: string; detail?: string; title?: string };
      if (!response.ok) {
        if (body.code === "FEATURE_NOT_IN_PLAN") throw new ApiError(response.status, t("attendance.joinNotInPlan"), body.code);
        if (body.code === "FEATURE_DISABLED") throw new ApiError(response.status, t("attendance.joinOff"), body.code);
        throw new ApiError(response.status, body.message || body.detail || body.title || t("errors.generic"), body.code);
      }
      setOk(body.outcome === "Marked");
      setMessage(body.message);
    } catch (error) {
      setOk(false);
      setMessage(error instanceof Error ? error.message : t("errors.generic"));
    } finally {
      setPending(false);
    }
  }

  return (
    <main className="mx-auto grid min-h-screen max-w-md content-center gap-4 px-4">
      <h1 className="text-2xl font-semibold">{t("attendance.joinTitle")}</h1>
      <p className="text-sm text-muted">{t("attendance.joinDescription")}</p>
      <form className="grid gap-3" onSubmit={(event) => void submit(event)}>
        <Input aria-label={t("auth.institute")} placeholder={t("auth.institute")} value={institute} onChange={(event) => setInstitute(event.target.value)} />
        <Input aria-label={t("attendance.joinCode")} placeholder={t("attendance.joinCode")} value={joinCode} onChange={(event) => setJoinCode(event.target.value.toUpperCase())} />
        <Input aria-label={t("attendance.code")} placeholder={t("attendance.code")} value={studentCode} onChange={(event) => setStudentCode(event.target.value)} className="h-12 text-lg" />
        <Button type="submit" disabled={pending || !institute.trim() || !joinCode.trim() || !studentCode.trim()}>{t("attendance.join")}</Button>
      </form>
      {message ? <p className={`rounded-lg border p-4 text-sm ${ok ? "border-success" : "border-danger"}`} role="status">{message}</p> : null}
    </main>
  );
}
