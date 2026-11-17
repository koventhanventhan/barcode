export type ExamKind = "Term" | "Unit" | "Test";
export type ExamStatus = "Draft" | "Scheduled" | "Locked" | "Published";

export type GradingScheme = {
  id: string;
  name: string;
  isDefault: boolean;
  boundaries: { id: string; grade: string; minPercent: number; maxPercent: number; gradePoint: number | null; sortOrder: number }[];
};

export type ExamSubject = {
  id: string;
  subjectId: string;
  subjectName: string;
  subjectCode: string;
  maxMarks: number;
  scheduledAt: string | null;
  room: string | null;
  sortOrder: number;
};

export type ExamListItem = {
  id: string;
  name: string;
  kind: ExamKind;
  status: ExamStatus;
  batchId: string;
  batchName: string;
  scheduledAt: string | null;
  room: string | null;
  subjectCount: number;
  includeMarksInNotification: boolean;
  publishedAt: string | null;
};

export type ExamDetail = ExamListItem & {
  educationLevelId: string;
  educationLevelName: string;
  gradingSchemeId: string;
  gradingSchemeName: string;
  seriesKey: string;
  reminderDaysBefore: number | null;
  reminderHoursBefore: number | null;
  scheduledNotifiedAt: string | null;
  reminderSentAt: string | null;
  lockedAt: string | null;
  subjects: ExamSubject[];
};

export type MarksGrid = {
  examId: string;
  status: ExamStatus;
  canEdit: boolean;
  students: { id: string; fullName: string; studentCode: string }[];
  subjects: { id: string; subjectId: string; name: string; maxMarks: number }[];
  cells: { studentId: string; examSubjectId: string; marks: number | null; isAbsent: boolean; grade: string | null; subjectRank: number | null }[];
  remarks: { studentId: string; teacherRemarks: string | null; principalRemarks: string | null }[];
};

export type RankList = {
  scope: string;
  subjectName: string | null;
  rows: {
    studentId: string;
    studentName: string;
    studentCode: string;
    batchName: string;
    score: number;
    maxScore: number;
    percentage: number;
    grade: string | null;
    rank: number | null;
    isAbsent: boolean;
  }[];
};

export type GradeDistribution = { grade: string; count: number; percentOfCohort: number };

export type PublishResult = { studentsRanked: number; messagesEnqueued: number; messagesSkipped: number };

export type ExamSummary = { upcomingCount: number; publishedCount: number; upcoming: ExamListItem[] };
