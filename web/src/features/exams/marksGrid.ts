export type MarkDraft = { marks: string; absent: boolean };

export function cellKey(studentId: string, subjectId: string) {
  return `${studentId}:${subjectId}`;
}

export function applyMarkPaste(
  studentIds: string[],
  subjectIds: string[],
  startStudent: number,
  startSubject: number,
  text: string,
  current: Record<string, MarkDraft>,
): Record<string, MarkDraft> {
  const next = { ...current };
  const lines = text.replace(/\r/g, "").split("\n");
  if (lines.at(-1) === "") lines.pop();
  lines.forEach((line, rowOffset) => {
    if (!line.trim()) return;
    line.split("\t").forEach((raw, colOffset) => {
      const studentId = studentIds[startStudent + rowOffset];
      const subjectId = subjectIds[startSubject + colOffset];
      if (!studentId || !subjectId) return;
      const value = raw.trim();
      if (!value) return;
      const absent = /^(a|abs|absent)$/i.test(value);
      next[cellKey(studentId, subjectId)] = absent
        ? { marks: "", absent: true }
        : { marks: value.replace(/,/g, ""), absent: false };
    });
  });
  return next;
}
