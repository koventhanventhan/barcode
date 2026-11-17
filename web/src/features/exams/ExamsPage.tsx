import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { ClipboardCheck, Lock, LockOpen, Printer, Send } from "lucide-react";
import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useFeature } from "@/components/feature-gate";
import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";
import { PageSkeleton } from "@/components/skeletons";
import { useToast } from "@/components/toast";
import { Button } from "@/components/ui/button";
import { Dialog } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { type Batch, type Subject } from "@/features/academics/shared";
import { api } from "@/lib/api";
import { downloadApi } from "@/lib/download";
import {
  type ExamDetail,
  type ExamKind,
  type ExamListItem,
  type GradeDistribution,
  type GradingScheme,
  type MarksGrid,
  type PublishResult,
  type RankList,
} from "./api";
import { applyMarkPaste, cellKey, type MarkDraft } from "./marksGrid";

type DraftSubject = { subjectId: string; maxMarks: string };

export function ExamsPage() {
  const { t } = useTranslation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const reports = useFeature("PerformanceReports");
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [name, setName] = useState("");
  const [kind, setKind] = useState<ExamKind>("Term");
  const [batchId, setBatchId] = useState("");
  const [schemeId, setSchemeId] = useState("");
  const [seriesKey, setSeriesKey] = useState("");
  const [includeMarks, setIncludeMarks] = useState(true);
  const [papers, setPapers] = useState<DraftSubject[]>([{ subjectId: "", maxMarks: "100" }]);
  const [room, setRoom] = useState("Hall A");
  const [when, setWhen] = useState("");
  const [drafts, setDrafts] = useState<Record<string, MarkDraft>>({});
  const [remarks, setRemarks] = useState<Record<string, { teacher: string; principal: string }>>({});
  const [rankScope, setRankScope] = useState("batch");
  const [rankSubject, setRankSubject] = useState("");

  const exams = useQuery({ queryKey: ["exams"], queryFn: () => api<ExamListItem[]>("/api/exams") });
  const schemes = useQuery({ queryKey: ["grading-schemes"], queryFn: () => api<GradingScheme[]>("/api/exams/grading-schemes") });
  const batches = useQuery({ queryKey: ["batches"], queryFn: () => api<Batch[]>("/api/batches") });
  const subjects = useQuery({ queryKey: ["subjects"], queryFn: () => api<Subject[]>("/api/subjects") });
  const detail = useQuery({
    queryKey: ["exam", selectedId],
    enabled: Boolean(selectedId),
    queryFn: () => api<ExamDetail>(`/api/exams/${selectedId}`),
  });
  const marks = useQuery({
    queryKey: ["exam-marks", selectedId],
    enabled: Boolean(selectedId),
    queryFn: () => api<MarksGrid>(`/api/exams/${selectedId}/marks`),
  });
  const ranks = useQuery({
    queryKey: ["exam-ranks", selectedId, rankScope, rankSubject],
    enabled: Boolean(selectedId) && detail.data?.status === "Published",
    queryFn: () => api<RankList>(`/api/exams/${selectedId}/ranks?scope=${rankScope}${rankSubject ? `&subjectId=${rankSubject}` : ""}`),
  });
  const distribution = useQuery({
    queryKey: ["exam-distribution", selectedId],
    enabled: Boolean(selectedId) && detail.data?.status === "Published",
    queryFn: () => api<GradeDistribution[]>(`/api/exams/${selectedId}/distribution`),
  });

  const grid = marks.data;
  const studentIds = grid?.students.map((student) => student.id) ?? [];
  const subjectIds = grid?.subjects.map((subject) => subject.id) ?? [];
  const mergedDrafts = useMemo(() => {
    const seed: Record<string, MarkDraft> = {};
    grid?.cells.forEach((cell) => {
      seed[cellKey(cell.studentId, cell.examSubjectId)] = {
        marks: cell.marks == null ? "" : String(cell.marks),
        absent: cell.isAbsent,
      };
    });
    return { ...seed, ...drafts };
  }, [grid, drafts]);

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: ["exams"] });
    void queryClient.invalidateQueries({ queryKey: ["exam", selectedId] });
    void queryClient.invalidateQueries({ queryKey: ["exam-marks", selectedId] });
    void queryClient.invalidateQueries({ queryKey: ["exam-ranks"] });
    void queryClient.invalidateQueries({ queryKey: ["exam-distribution"] });
    void queryClient.invalidateQueries({ queryKey: ["exam-summary"] });
  }

  const createExam = useMutation({
    mutationFn: () =>
      api<ExamDetail>("/api/exams", {
        method: "POST",
        body: JSON.stringify({
          name,
          kind,
          batchId,
          gradingSchemeId: schemeId || null,
          seriesKey: seriesKey || null,
          includeMarksInNotification: includeMarks,
          reminderDaysBefore: null,
          reminderHoursBefore: null,
          subjects: papers
            .filter((paper) => paper.subjectId)
            .map((paper, index) => ({
              subjectId: paper.subjectId,
              maxMarks: Number(paper.maxMarks),
              room: room || null,
              sortOrder: index + 1,
            })),
        }),
      }),
    onSuccess: (created) => {
      setCreateOpen(false);
      setSelectedId(created.id);
      setName("");
      toast.success(t("exams.created"));
      invalidate();
    },
    onError: (error: Error) => toast.error(error.message),
  });

  const act = useMutation({
    mutationFn: (path: string) => api<ExamDetail | PublishResult>(`/api/exams/${selectedId}/${path}`, { method: "POST", body: path === "schedule" ? JSON.stringify({ scheduledAt: new Date(when).toISOString(), room }) : undefined }),
    onSuccess: () => {
      toast.success(t("exams.saved"));
      invalidate();
    },
    onError: (error: Error) => toast.error(error.message),
  });

  const saveMarks = useMutation({
    mutationFn: () => {
      const entries = studentIds.flatMap((studentId) =>
        subjectIds.flatMap((subjectId) => {
          const draft = mergedDrafts[cellKey(studentId, subjectId)];
          if (!draft || (!draft.absent && draft.marks === "")) return [];
          return [{ studentId, examSubjectId: subjectId, marks: draft.absent ? null : Number(draft.marks), isAbsent: draft.absent }];
        }),
      );
      return api<MarksGrid>(`/api/exams/${selectedId}/marks`, {
        method: "PUT",
        body: JSON.stringify({
          entries,
          remarks: Object.entries(remarks).map(([studentId, value]) => ({
            studentId,
            teacherRemarks: value.teacher || null,
            principalRemarks: value.principal || null,
          })),
        }),
      });
    },
    onSuccess: () => {
      setDrafts({});
      toast.success(t("exams.marksSaved"));
      invalidate();
    },
    onError: (error: Error) => toast.error(error.message),
  });

  function moveFocus(row: number, col: number) {
    const next = document.querySelector<HTMLInputElement>(`[data-mark-cell="${row}-${col}"]`);
    next?.focus();
    next?.select();
  }

  if (exams.isLoading || schemes.isLoading) return <PageSkeleton />;

  const selected = detail.data;
  return (
    <div>
      <PageHeader
        title={t("exams.title")}
        description={t("exams.description")}
        crumbs={[{ label: t("nav.academic") }, { label: t("nav.exams") }]}
        actions={<Button onClick={() => setCreateOpen(true)}>{t("exams.newExam")}</Button>}
      />
      <div className="grid gap-4 lg:grid-cols-[280px_minmax(0,1fr)]">
        <div className="grid content-start gap-2">
          {(exams.data ?? []).length === 0 ? <EmptyState title={t("exams.emptyTitle")} description={t("exams.emptyDescription")} /> : null}
          {(exams.data ?? []).map((exam) => (
            <button
              key={exam.id}
              type="button"
              onClick={() => setSelectedId(exam.id)}
              className={`rounded-lg border px-3 py-2 text-left ${selectedId === exam.id ? "border-primary bg-secondary" : "border-border bg-card"}`}
            >
              <span className="block text-sm font-medium">{exam.name}</span>
              <span className="mt-1 block text-xs text-muted">{exam.batchName} · {t(`exams.status.${exam.status}`)}</span>
            </button>
          ))}
        </div>
        {selected ? (
          <section className="grid gap-4">
            <div className="flex flex-wrap items-end justify-between gap-3 rounded-lg border border-border bg-card p-4">
              <div>
                <h2 className="text-base font-semibold">{selected.name}</h2>
                <p className="text-[13px] text-muted">{selected.batchName} · {selected.gradingSchemeName} · {t(`exams.kind.${selected.kind}`)}</p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Input className="w-44" type="datetime-local" value={when} onChange={(event) => setWhen(event.target.value)} aria-label={t("exams.when")} />
                <Input className="w-28" value={room} onChange={(event) => setRoom(event.target.value)} aria-label={t("exams.room")} />
                <Button variant="outline" disabled={!when || act.isPending} onClick={() => act.mutate("schedule")}>{t("exams.schedule")}</Button>
                {selected.status === "Scheduled" ? <Button variant="outline" onClick={() => act.mutate("lock")}><Lock className="h-4 w-4" />{t("exams.lock")}</Button> : null}
                {selected.status === "Locked" ? <Button variant="outline" onClick={() => act.mutate("unlock")}><LockOpen className="h-4 w-4" />{t("exams.unlock")}</Button> : null}
                <Button disabled={act.isPending} onClick={() => act.mutate("publish")}><Send className="h-4 w-4" />{t("exams.publish")}</Button>
                {reports.isEnabled && selected.status === "Published" ? (
                  <Button variant="outline" onClick={() => void downloadApi(`/api/exams/${selected.id}/report-cards`, `${selected.name}-report-cards.pdf`).catch((error: Error) => toast.error(error.message))}>
                    <Printer className="h-4 w-4" />{t("exams.reportCards")}
                  </Button>
                ) : null}
              </div>
            </div>
            {!reports.isInPlan ? <p className="text-xs text-muted">{t("exams.reportCardsLocked")}</p> : null}
            {grid ? (
              <div className="overflow-x-auto rounded-lg border border-border bg-card">
                <table className="min-w-[640px] w-full text-left text-sm">
                  <thead className="sticky top-0 bg-card">
                    <tr className="border-b border-border text-xs text-muted">
                      <th className="px-3 py-2 font-medium">{t("exams.student")}</th>
                      {grid.subjects.map((subject) => <th key={subject.id} className="px-3 py-2 font-medium">{subject.name}<span className="ml-1 tabular-nums">/{subject.maxMarks}</span></th>)}
                      <th className="px-3 py-2 font-medium">{t("exams.teacherRemarks")}</th>
                      <th className="px-3 py-2 font-medium">{t("exams.principalRemarks")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {grid.students.map((student, row) => (
                      <tr key={student.id} className="border-b border-border">
                        <td className="px-3 py-2"><span className="font-medium">{student.fullName}</span><span className="mt-0.5 block text-xs text-muted">{student.studentCode}</span></td>
                        {grid.subjects.map((subject, col) => {
                          const key = cellKey(student.id, subject.id);
                          const draft = mergedDrafts[key] ?? { marks: "", absent: false };
                          return (
                            <td key={subject.id} className="px-3 py-2">
                              <div className="flex items-center gap-2">
                                <Input
                                  data-mark-cell={`${row}-${col}`}
                                  className="w-20 tabular-nums"
                                  inputMode="decimal"
                                  disabled={!grid.canEdit || draft.absent}
                                  value={draft.marks}
                                  aria-label={`${student.fullName} ${subject.name}`}
                                  onChange={(event) => setDrafts((current) => ({ ...mergedDrafts, ...current, [key]: { marks: event.target.value, absent: false } }))}
                                  onPaste={(event) => {
                                    event.preventDefault();
                                    setDrafts(applyMarkPaste(studentIds, subjectIds, row, col, event.clipboardData.getData("text"), mergedDrafts));
                                  }}
                                  onKeyDown={(event) => {
                                    if (event.key === "ArrowRight") moveFocus(row, col + 1);
                                    if (event.key === "ArrowLeft") moveFocus(row, col - 1);
                                    if (event.key === "ArrowDown" || event.key === "Enter") moveFocus(row + 1, col);
                                    if (event.key === "ArrowUp") moveFocus(row - 1, col);
                                  }}
                                />
                                <label className="flex items-center gap-1 text-xs text-muted">
                                  <input
                                    type="checkbox"
                                    checked={draft.absent}
                                    disabled={!grid.canEdit}
                                    onChange={(event) => setDrafts((current) => ({ ...mergedDrafts, ...current, [key]: { marks: "", absent: event.target.checked } }))}
                                  />
                                  {t("exams.absent")}
                                </label>
                              </div>
                            </td>
                          );
                        })}
                        <td className="px-3 py-2"><Input value={remarks[student.id]?.teacher ?? grid.remarks.find((item) => item.studentId === student.id)?.teacherRemarks ?? ""} onChange={(event) => setRemarks((current) => ({ ...current, [student.id]: { teacher: event.target.value, principal: current[student.id]?.principal ?? "" } }))} /></td>
                        <td className="px-3 py-2"><Input value={remarks[student.id]?.principal ?? grid.remarks.find((item) => item.studentId === student.id)?.principalRemarks ?? ""} onChange={(event) => setRemarks((current) => ({ ...current, [student.id]: { teacher: current[student.id]?.teacher ?? "", principal: event.target.value } }))} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
                <div className="flex justify-end p-3">
                  <Button disabled={!grid.canEdit || saveMarks.isPending} onClick={() => saveMarks.mutate()}>{t("exams.saveMarks")}</Button>
                </div>
              </div>
            ) : null}
            <p className="text-xs text-muted">{t("exams.pasteHint")}</p>
            {selected.status === "Published" && distribution.data ? (
              <div className="grid gap-4 xl:grid-cols-2">
                <section className="rounded-lg border border-border bg-card p-4">
                  <h3 className="mb-3 text-sm font-semibold">{t("exams.distribution")}</h3>
                  <div className="h-52">
                    <ResponsiveContainer width="100%" height="100%">
                      <BarChart data={distribution.data}>
                        <CartesianGrid stroke="var(--border)" vertical={false} />
                        <XAxis dataKey="grade" tick={{ fontSize: 12 }} />
                        <YAxis allowDecimals={false} tick={{ fontSize: 12 }} />
                        <Tooltip />
                        <Bar dataKey="count" fill="var(--primary)" name={t("exams.students")} />
                      </BarChart>
                    </ResponsiveContainer>
                  </div>
                </section>
                <section className="rounded-lg border border-border bg-card p-4">
                  <div className="mb-3 flex flex-wrap gap-2">
                    <h3 className="text-sm font-semibold">{t("exams.ranks")}</h3>
                    <select className="h-8 rounded-md border border-border bg-card px-2 text-xs" value={rankScope} onChange={(event) => setRankScope(event.target.value)} aria-label={t("exams.rankScope")}>
                      <option value="batch">{t("exams.scope.batch")}</option>
                      <option value="level">{t("exams.scope.level")}</option>
                      <option value="subject">{t("exams.scope.subject")}</option>
                    </select>
                    {rankScope === "subject" ? (
                      <select className="h-8 rounded-md border border-border bg-card px-2 text-xs" value={rankSubject} onChange={(event) => setRankSubject(event.target.value)} aria-label={t("exams.subject")}>
                        <option value="">{t("exams.firstSubject")}</option>
                        {selected.subjects.map((subject) => <option key={subject.subjectId} value={subject.subjectId}>{subject.subjectName}</option>)}
                      </select>
                    ) : null}
                  </div>
                  <div className="overflow-x-auto">
                    <table className="w-full text-left text-sm">
                      <thead><tr className="border-b border-border text-xs text-muted"><th className="py-1">{t("exams.rank")}</th><th>{t("exams.student")}</th><th>{t("exams.score")}</th><th>{t("exams.grade")}</th></tr></thead>
                      <tbody>
                        {ranks.data?.rows.map((row) => (
                          <tr key={row.studentId} className="border-b border-border">
                            <td className="py-1 tabular-nums">{row.rank ?? "—"}</td>
                            <td>{row.studentName}<span className="ml-1 text-xs text-muted">{row.batchName}</span></td>
                            <td className="tabular-nums">{row.percentage.toFixed(2)}%</td>
                            <td>{row.grade ?? "—"}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </section>
              </div>
            ) : null}
          </section>
        ) : (
          <EmptyState title={t("exams.pickTitle")} description={t("exams.pickDescription")} />
        )}
      </div>
      <Dialog open={createOpen} onOpenChange={setCreateOpen} title={t("exams.newExam")} description={t("exams.newExamHelp")}>
        <form className="grid gap-3" onSubmit={(event) => { event.preventDefault(); createExam.mutate(); }}>
          <Input value={name} onChange={(event) => setName(event.target.value)} placeholder={t("exams.name")} required aria-label={t("exams.name")} />
          <div className="grid gap-3 sm:grid-cols-2">
            <select className="h-9 rounded-md border border-border bg-card px-2 text-sm" value={kind} onChange={(event) => setKind(event.target.value as ExamKind)} aria-label={t("exams.kindLabel")}>
              <option value="Term">{t("exams.kind.Term")}</option>
              <option value="Unit">{t("exams.kind.Unit")}</option>
              <option value="Test">{t("exams.kind.Test")}</option>
            </select>
            <select className="h-9 rounded-md border border-border bg-card px-2 text-sm" value={batchId} onChange={(event) => setBatchId(event.target.value)} required aria-label={t("exams.batch")}>
              <option value="">{t("exams.chooseBatch")}</option>
              {(batches.data ?? []).map((batch) => <option key={batch.id} value={batch.id}>{batch.name}</option>)}
            </select>
          </div>
          <select className="h-9 rounded-md border border-border bg-card px-2 text-sm" value={schemeId} onChange={(event) => setSchemeId(event.target.value)} aria-label={t("exams.scheme")}>
            {(schemes.data ?? []).map((scheme) => <option key={scheme.id} value={scheme.id}>{scheme.name}</option>)}
          </select>
          <Input value={seriesKey} onChange={(event) => setSeriesKey(event.target.value)} placeholder={t("exams.seriesKey")} aria-label={t("exams.seriesKey")} />
          <label className="flex items-center gap-2 text-sm"><input type="checkbox" checked={includeMarks} onChange={(event) => setIncludeMarks(event.target.checked)} />{t("exams.includeMarks")}</label>
          {papers.map((paper, index) => (
            <div key={index} className="grid grid-cols-[1fr_90px] gap-2">
              <select className="h-9 rounded-md border border-border bg-card px-2 text-sm" value={paper.subjectId} onChange={(event) => setPapers((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, subjectId: event.target.value } : item))} aria-label={t("exams.subject")}>
                <option value="">{t("exams.chooseSubject")}</option>
                {(subjects.data ?? []).map((subject) => <option key={subject.id} value={subject.id}>{subject.name}</option>)}
              </select>
              <Input className="tabular-nums" value={paper.maxMarks} onChange={(event) => setPapers((current) => current.map((item, itemIndex) => itemIndex === index ? { ...item, maxMarks: event.target.value } : item))} aria-label={t("exams.maxMarks")} />
            </div>
          ))}
          <Button type="button" variant="outline" onClick={() => setPapers((current) => [...current, { subjectId: "", maxMarks: "100" }])}>{t("exams.addSubject")}</Button>
          <Button type="submit" disabled={createExam.isPending}><ClipboardCheck className="h-4 w-4" />{t("exams.create")}</Button>
        </form>
      </Dialog>
    </div>
  );
}
