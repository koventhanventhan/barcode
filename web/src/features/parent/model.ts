export type ParentChild = {
  id: string;
  fullName: string;
  studentCode: string;
  photoUrl: string | null;
  levelName: string;
  batchNames: string;
};

export type ParentHome = {
  children: ParentChild[];
  canSeeMarks: boolean;
  canPayOnline: boolean;
  reportCardsAvailable: boolean;
};

export type ParentAttendanceItem = {
  id: string;
  sessionDate: string;
  className: string;
  subjectName: string;
  status: string;
  markedAt: string | null;
};

export type ParentClassItem = {
  id: string;
  name: string;
  subjectName: string;
  teacherName: string;
  batchName: string;
  weekday: number;
  startMinutes: number;
  endMinutes: number;
  room: string | null;
  nextDate: string;
};

export type ParentExamSubject = {
  subject: string;
  maxMarks: number;
  marks: number | null;
  isAbsent: boolean;
  grade: string | null;
  rank: number | null;
};

export type ParentExamItem = {
  id: string;
  name: string;
  kind: string;
  status: string;
  scheduledAt: string | null;
  room: string | null;
  percentage: number | null;
  grade: string | null;
  batchRank: number | null;
  levelRank: number | null;
  subjects: ParentExamSubject[] | null;
};

export type ParentInvoice = {
  id: string;
  invoiceNumber: string;
  title: string;
  billingPeriod: string;
  netAmount: number;
  paidAmount: number;
  balanceAmount: number;
  currency: string;
  dueDate: string;
  status: string;
};

export type ParentPayment = {
  id: string;
  receiptNumber: string;
  invoiceNumber: string;
  amount: number;
  currency: string;
  method: string;
  status: string;
  paidAt: string;
};

export type ParentMessage = {
  id: string;
  messageType: string;
  channel: string;
  status: string;
  subject: string | null;
  body: string;
  studentName: string | null;
  createdAt: string;
  sentAt: string | null;
};

export type ParentDashboard = {
  child: ParentChild;
  today: ParentAttendanceItem[];
  monthPresent: number;
  monthMarked: number;
  upcomingClasses: ParentClassItem[];
  upcomingExams: ParentExamItem[];
  openInvoiceCount: number;
  outstandingAmount: number;
  currency: string;
  latestMessage: ParentMessage | null;
};

export type ParentCheckout = {
  orderId: string;
  amount: number;
  currency: string;
  checkoutUrl: string;
  formFields: Record<string, string>;
  isSandbox: boolean;
};

export type ParentAccount = {
  id: string;
  fullName: string;
  phone: string;
  email: string | null;
  isActive: boolean;
  invitePending: boolean;
  students: { id: string; fullName: string; studentCode: string }[];
};

export function readSelectedChild(ids: string[], stored: string | null) {
  if (stored && ids.includes(stored)) return stored;
  return ids[0] ?? "";
}

export function formatMinutes(minutes: number) {
  const hour = Math.floor(minutes / 60);
  const minute = minutes % 60;
  const date = new Date(2000, 0, 1, hour, minute);
  return date.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
}

export function weekdayName(weekday: number) {
  return ["", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"][weekday] ?? "";
}

export function formatMoney(amount: number, currency = "LKR") {
  return new Intl.NumberFormat("en-LK", { style: "currency", currency, maximumFractionDigits: 2 }).format(amount);
}
