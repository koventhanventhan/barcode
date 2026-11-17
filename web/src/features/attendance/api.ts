import { getAccessToken, getTenantSlug } from "@/lib/session";
import * as signalR from "@microsoft/signalr";

export type ClassSession = {
  id: string;
  classScheduleId: string;
  className: string;
  batchId: string;
  batchName: string;
  sessionDate: string;
  status: "Scheduled" | "Started" | "Stopped" | "Cancelled";
  startedAt: string | null;
  joinCode: string | null;
  enrolledCount: number;
  presentCount: number;
  lateCount: number;
  absentCount: number;
};

export type ScanResult = {
  outcome: "Marked" | "AlreadyMarked";
  message: string;
  sessionId: string | null;
  studentName: string | null;
  studentCode: string | null;
  status: string | null;
  markedAtLabel: string | null;
};

export type BoardScan = {
  studentId: string;
  studentName: string;
  studentCode: string;
  status: string;
  markedAtLabel: string;
};

export type AttendanceBoard = {
  sessionId: string;
  enrolledCount: number;
  presentCount: number;
  lateCount: number;
  absentCount: number;
  lastScans: BoardScan[];
};

export type RegisterStudent = {
  studentId: string;
  studentCode: string;
  fullName: string;
  status: string | null;
  markedAt: string | null;
  reason: string | null;
};

export type RegisterSession = { session: ClassSession; students: RegisterStudent[] };

export type AttendanceReport = {
  present: number;
  late: number;
  absent: number;
  presentRate: number;
  byDay: { date: string; present: number; late: number; absent: number }[];
  byBatch: { batchId: string; batchName: string; present: number; late: number; absent: number; presentRate: number }[];
};

export function colomboToday(): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Colombo",
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(new Date());
}

export function connectAttendance(onBoard: (board: AttendanceBoard) => void) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/attendance", {
      accessTokenFactory: () => getAccessToken() ?? "",
      headers: { "X-Tenant": getTenantSlug() ?? "" },
    })
    .withAutomaticReconnect()
    .build();
  connection.on("boardUpdated", onBoard);
  return connection;
}

export function playScanCue(ok: boolean) {
  try {
    const context = new AudioContext();
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.frequency.value = ok ? 880 : 220;
    oscillator.connect(gain);
    gain.connect(context.destination);
    gain.gain.setValueAtTime(0.08, context.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + 0.2);
    oscillator.start();
    oscillator.stop(context.currentTime + 0.2);
  } catch {
    /* autoplay can be blocked until the first tap */
  }
  if (typeof navigator !== "undefined" && typeof navigator.vibrate === "function") {
    navigator.vibrate(ok ? 40 : [30, 40, 30]);
  }
}
