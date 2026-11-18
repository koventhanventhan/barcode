import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Plus, Trash2 } from "lucide-react";
import { useState } from "react";
import { t } from "@/config/strings";
import { FeatureGate, useFeature } from "@/components/feature-gate";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import {
  AcademicNav,
  WEEKDAYS,
  fieldClass,
  minutesToTime,
  timeToMinutes,
  type Batch,
  type Subject,
  type TeacherOption,
} from "@/features/academics/shared";
import { useAuth } from "@/features/auth/AuthProvider";
import type { TimetableClash, WeeklyTimetable } from "@/features/operations/api";
import { api, ApiError } from "@/lib/api";

export function TimetablePage() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { hasPermission, isReadOnly } = useAuth();
  const canManage = hasPermission("academics.manage") && !isReadOnly;
  const timetableFeature = useFeature("Timetable");

  const [batchFilter, setBatchFilter] = useState<string>("");
  const [teacherFilter, setTeacherFilter] = useState<string>("");
  const [roomFilter, setRoomFilter] = useState<string>("");

  const [modalOpen, setModalOpen] = useState(false);
  const [batchId, setBatchId] = useState<string>("");
  const [subjectId, setSubjectId] = useState<string>("");
  const [teacherUserId, setTeacherUserId] = useState<string>("");
  const [weekday, setWeekday] = useState<number>(1);
  const [starts, setStarts] = useState<string>("08:00");
  const [ends, setEnds] = useState<string>("10:00");
  const [room, setRoom] = useState<string>("");
  const [name, setName] = useState<string>("");
  const [detectedClashes, setDetectedClashes] = useState<TimetableClash[]>([]);

  const queryParams = new URLSearchParams();
  if (batchFilter) queryParams.set("batchId", batchFilter);
  if (teacherFilter) queryParams.set("teacherUserId", teacherFilter);
  if (roomFilter) queryParams.set("room", roomFilter);
  const qs = queryParams.toString();

  const timetable = useQuery({
    queryKey: ["timetable", batchFilter, teacherFilter, roomFilter],
    enabled: timetableFeature.isEnabled,
    queryFn: () => api<WeeklyTimetable>(`/api/timetable${qs ? `?${qs}` : ""}`),
  });

  const batches = useQuery({
    queryKey: ["batches"],
    enabled: timetableFeature.isEnabled,
    queryFn: () => api<Batch[]>("/api/batches"),
  });

  const subjects = useQuery({
    queryKey: ["subjects"],
    enabled: timetableFeature.isEnabled,
    queryFn: () => api<Subject[]>("/api/subjects"),
  });

  const teachers = useQuery({
    queryKey: ["teachers"],
    enabled: timetableFeature.isEnabled,
    queryFn: () => api<TeacherOption[]>("/api/class-schedules/teachers"),
  });

  const checkClashes = useMutation({
    mutationFn: () =>
      api<TimetableClash[]>("/api/timetable/check-clashes", {
        method: "POST",
        body: JSON.stringify({
          excludeScheduleId: null,
          batchId: batchId || batches.data?.[0]?.id,
          subjectId: subjectId || subjects.data?.[0]?.id,
          teacherUserId: teacherUserId || teachers.data?.[0]?.id,
          weekday,
          startMinutes: timeToMinutes(starts),
          endMinutes: timeToMinutes(ends),
          room: room || null,
        }),
      }),
    onSuccess: (clashes) => setDetectedClashes(clashes),
  });

  const createSlot = useMutation({
    mutationFn: () =>
      api("/api/timetable/slots", {
        method: "POST",
        body: JSON.stringify({
          name: name || null,
          batchId: batchId || batches.data?.[0]?.id,
          subjectId: subjectId || subjects.data?.[0]?.id,
          teacherUserId: teacherUserId || teachers.data?.[0]?.id,
          weekday,
          startMinutes: timeToMinutes(starts),
          endMinutes: timeToMinutes(ends),
          room: room || null,
          isActive: true,
        }),
      }),
    onSuccess: () => {
      toast.success(t("timetable.slotSaved"));
      setModalOpen(false);
      setDetectedClashes([]);
      void queryClient.invalidateQueries({ queryKey: ["timetable"] });
      void queryClient.invalidateQueries({ queryKey: ["classes"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  const deleteSlot = useMutation({
    mutationFn: (id: string) => api(`/api/timetable/slots/${id}`, { method: "DELETE" }),
    onSuccess: () => {
      toast.success(t("academic.deleted"));
      void queryClient.invalidateQueries({ queryKey: ["timetable"] });
      void queryClient.invalidateQueries({ queryKey: ["classes"] });
    },
    onError: (err) => toast.error(err instanceof ApiError ? err.message : t("errors.generic")),
  });

  if (!hasPermission("academics.view")) {
    return <p className="text-sm">{t("auth.deniedBody")}</p>;
  }

  const weekdayLabel = (day: number) =>
    t(`academic.${["", "mon", "tue", "wed", "thu", "fri", "sat", "sun"][day]}`);

  return (
    <div>
      <PageHeader
        title={t("timetable.title")}
        description={t("timetable.description")}
        crumbs={[
          { label: t("nav.academic"), href: "/app/academics/levels" },
          { label: t("nav.timetable") },
        ]}
        actions={
          timetableFeature.isEnabled && canManage ? (
            <Button
              onClick={() => {
                setBatchId(batches.data?.[0]?.id ?? "");
                setSubjectId(subjects.data?.[0]?.id ?? "");
                setTeacherUserId(teachers.data?.[0]?.id ?? "");
                setDetectedClashes([]);
                setModalOpen(true);
              }}
            >
              <Plus className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
              {t("timetable.newSlot")}
            </Button>
          ) : null
        }
      />

      <AcademicNav />

      <FeatureGate
        feature="Timetable"
        fallback={
          <section className="rounded-lg border border-border bg-card p-4 text-sm text-muted">
            {t("timetable.lockedNotice")}
          </section>
        }
      >
        {timetable.isLoading ? (
          <PageSkeleton />
        ) : (
          <div className="grid gap-4">
            <div className="grid gap-3 rounded-lg border border-border bg-card p-3 sm:grid-cols-3">
              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("nav.batches")}</span>
                <select
                  className={fieldClass}
                  value={batchFilter}
                  onChange={(e) => setBatchFilter(e.target.value)}
                >
                  <option value="">{t("timetable.allBatches")}</option>
                  {(batches.data ?? []).map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.name}
                    </option>
                  ))}
                </select>
              </label>

              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("academic.teacher")}</span>
                <select
                  className={fieldClass}
                  value={teacherFilter}
                  onChange={(e) => setTeacherFilter(e.target.value)}
                >
                  <option value="">{t("timetable.allTeachers")}</option>
                  {(teachers.data ?? []).map((teacher) => (
                    <option key={teacher.id} value={teacher.id}>
                      {teacher.fullName}
                    </option>
                  ))}
                </select>
              </label>

              <label className="grid gap-1 text-xs">
                <span className="font-medium text-muted">{t("academic.room")}</span>
                <select
                  className={fieldClass}
                  value={roomFilter}
                  onChange={(e) => setRoomFilter(e.target.value)}
                >
                  <option value="">{t("timetable.allRooms")}</option>
                  {(timetable.data?.rooms ?? []).map((r) => (
                    <option key={r} value={r}>
                      {r}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            {(timetable.data?.clashes ?? []).length > 0 ? (
              <div className="rounded-lg border border-danger bg-card p-3">
                <div className="flex items-center gap-2 text-sm font-semibold text-danger">
                  <AlertTriangle className="h-4 w-4" aria-hidden="true" />
                  <span>{t("timetable.clashesDetected")}</span>
                </div>
                <ul className="mt-1.5 list-disc pl-5 text-xs text-muted">
                  {(timetable.data?.clashes ?? []).map((c, idx) => (
                    <li key={`${c.conflictingScheduleId}-${idx}`}>
                      [{c.clashType}] {c.message}
                    </li>
                  ))}
                </ul>
              </div>
            ) : null}

            <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-7">
              {(timetable.data?.days ?? []).map((col) => (
                <section
                  key={col.weekday}
                  className="flex flex-col rounded-lg border border-border bg-card"
                >
                  <header className="border-b border-border px-3 py-2 text-xs font-semibold uppercase tracking-wide text-muted">
                    {weekdayLabel(col.weekday)}
                  </header>
                  <div className="flex-1 space-y-2 p-2">
                    {col.slots.length === 0 ? (
                      <p className="py-4 text-center text-xs text-muted">
                        {t("timetable.noSlots")}
                      </p>
                    ) : (
                      col.slots.map((slot) => (
                        <div
                          key={slot.id}
                          className="rounded-md border border-border bg-background p-2.5 text-xs"
                        >
                          <div className="flex items-start justify-between gap-1">
                            <span className="font-mono font-semibold text-primary tabular-nums">
                              {minutesToTime(slot.startMinutes)}–
                              {minutesToTime(Number(slot.endMinutes))}
                            </span>
                            {canManage ? (
                              <button
                                type="button"
                                className="text-muted hover:text-danger"
                                aria-label={t("common.delete")}
                                onClick={() => deleteSlot.mutate(slot.id)}
                              >
                                <Trash2 className="h-3.5 w-3.5" />
                              </button>
                            ) : null}
                          </div>
                          <p className="mt-1 font-medium text-foreground">{slot.subjectName}</p>
                          <p className="text-muted">{slot.batchName}</p>
                          <p className="text-muted">{slot.teacherName}</p>
                          {slot.room ? (
                            <span className="mt-1 inline-block rounded bg-secondary px-1.5 py-0.5 text-[11px]">
                              {slot.room}
                            </span>
                          ) : null}
                        </div>
                      ))
                    )}
                  </div>
                </section>
              ))}
            </div>
          </div>
        )}
      </FeatureGate>

      <Dialog
        open={modalOpen}
        onOpenChange={setModalOpen}
        title={t("timetable.newSlot")}
        description={t("timetable.clashCheckHint")}
      >
        <form
          className="grid gap-3 sm:grid-cols-2"
          onSubmit={(e) => {
            e.preventDefault();
            createSlot.mutate();
          }}
        >
          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("nav.batches")}</span>
            <select
              className={fieldClass}
              value={batchId}
              onChange={(e) => setBatchId(e.target.value)}
            >
              {(batches.data ?? []).map((b) => (
                <option key={b.id} value={b.id}>
                  {b.name}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("academic.subject")}</span>
            <select
              className={fieldClass}
              value={subjectId}
              onChange={(e) => setSubjectId(e.target.value)}
            >
              {(subjects.data ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("academic.teacher")}</span>
            <select
              className={fieldClass}
              value={teacherUserId}
              onChange={(e) => setTeacherUserId(e.target.value)}
            >
              {(teachers.data ?? []).map((tItem) => (
                <option key={tItem.id} value={tItem.id}>
                  {tItem.fullName}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("academic.weekday")}</span>
            <select
              className={fieldClass}
              value={weekday}
              onChange={(e) => setWeekday(Number(e.target.value))}
            >
              {WEEKDAYS.map((d) => (
                <option key={d} value={d}>
                  {weekdayLabel(d)}
                </option>
              ))}
            </select>
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("academic.starts")}</span>
            <Input
              type="time"
              value={starts}
              onChange={(e) => setStarts(e.target.value)}
            />
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("academic.ends")}</span>
            <Input
              type="time"
              value={ends}
              onChange={(e) => setEnds(e.target.value)}
            />
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("academic.room")}</span>
            <Input
              value={room}
              onChange={(e) => setRoom(e.target.value)}
              placeholder="Hall 1"
            />
          </label>

          <label className="grid gap-1 text-xs">
            <span className="font-medium text-muted">{t("staff.name")}</span>
            <Input value={name} onChange={(e) => setName(e.target.value)} />
          </label>

          {detectedClashes.length > 0 ? (
            <div className="rounded-md border border-danger bg-secondary p-2.5 text-xs text-danger sm:col-span-2">
              {detectedClashes.map((c, idx) => (
                <p key={idx}>
                  {c.clashType}: {c.message}
                </p>
              ))}
            </div>
          ) : null}

          <div className="flex flex-wrap justify-between gap-2 sm:col-span-2">
            <Button
              type="button"
              variant="outline"
              disabled={checkClashes.isPending}
              onClick={() => checkClashes.mutate()}
            >
              {t("timetable.checkClashes")}
            </Button>
            <div className="flex gap-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => setModalOpen(false)}
              >
                {t("common.cancel")}
              </Button>
              <Button type="submit" disabled={createSlot.isPending}>
                {t("common.save")}
              </Button>
            </div>
          </div>
        </form>
      </Dialog>
    </div>
  );
}
