import { FEATURE_KEYS } from "@/domain/catalog";

export const COMPARISON_GROUPS = [
  {
    id: "core",
    features: [
      "ParentPortal",
      "BarcodeGenerator",
      "MultiBranch",
      "BatchGroups",
      "Timetable",
      "StaffLeave",
      "OnlineAttendance",
      "ParentCanSeeMarks",
    ],
  },
  {
    id: "messaging",
    features: [
      "AttendanceSms",
      "ClassSessionSms",
      "FeeReminderSms",
      "PaymentConfirmationSms",
      "ExamNotifications",
      "EmailNotifications",
      "BroadcastMessages",
      "WhatsAppMessaging",
    ],
  },
  {
    id: "reports",
    features: ["DataExport", "PerformanceReports", "ReportCleanup"],
  },
  {
    id: "payments",
    features: ["OnlinePayments", "ParentCanPayOnline"],
  },
  {
    id: "admin",
    features: ["AutoBackup", "PrioritySupport", "RemoveProductBranding"],
  },
] as const;

export function comparisonCoversEveryFeature(): boolean {
  const listed = COMPARISON_GROUPS.flatMap((group) => [...group.features]);
  return listed.length === new Set(listed).size && FEATURE_KEYS.every((key) => listed.includes(key));
}
