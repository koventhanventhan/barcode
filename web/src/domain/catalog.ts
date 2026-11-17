export const FEATURE_KEYS = [
  "ParentPortal",
  "AttendanceSms",
  "ClassSessionSms",
  "FeeReminderSms",
  "PaymentConfirmationSms",
  "ExamNotifications",
  "EmailNotifications",
  "WhatsAppMessaging",
  "OnlinePayments",
  "AutoBackup",
  "Timetable",
  "StaffLeave",
  "OnlineAttendance",
  "BroadcastMessages",
  "DataExport",
  "BarcodeGenerator",
  "PerformanceReports",
  "MultiBranch",
  "BatchGroups",
  "ReportCleanup",
  "PrioritySupport",
  "RemoveProductBranding",
] as const;

export const NOTIFICATION_FEATURES = new Set<string>([
  "AttendanceSms",
  "ClassSessionSms",
  "FeeReminderSms",
  "PaymentConfirmationSms",
  "ExamNotifications",
  "EmailNotifications",
  "WhatsAppMessaging",
  "BroadcastMessages",
]);

export const LIMIT_KEYS = [
  "Students",
  "EducationLevels",
  "Batches",
  "Subjects",
  "StaffAccounts",
  "AdminAccounts",
  "SmsSegmentsPerMonth",
  "CloudStorageGb",
] as const;

export const INSTITUTE_ROLES = ["InstituteAdmin", "Staff", "Teacher"] as const;
