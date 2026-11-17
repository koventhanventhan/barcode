export type TenantBackupPolicy = {
  isScheduledEnabled: boolean;
  retentionDays: number;
  maxBackupsToKeep: number;
  preferredHourUtc: number;
};

export type TenantBackupRecord = {
  id: string;
  tenantId: string;
  trigger: string;
  status: string;
  fileName: string;
  storageUrl: string;
  sizeBytes: number;
  sha256: string;
  totalRecords: number;
  uploadedFilesCount: number;
  counts: Record<string, number>;
  errorMessage: string | null;
  createdAt: string;
  completedAt: string | null;
};

export type TenantBackupWorkspace = {
  policy: TenantBackupPolicy;
  history: TenantBackupRecord[];
};

export type BackupRestoreResult = {
  dryRun: boolean;
  isValid: boolean;
  tenantId: string;
  backupId: string;
  fileName: string;
  archiveCounts: Record<string, number>;
  restorableCounts: Record<string, number>;
  restoredTotal: number;
  warnings: string[];
};

export type PlatformBackupRecord = {
  id: string;
  trigger: string;
  status: string;
  fileName: string;
  storageUrl: string;
  sizeBytes: number;
  databaseProvider: string;
  sqlCommandSummary: string;
  sha256: string;
  retentionDays: number;
  errorMessage: string | null;
  createdAt: string;
  completedAt: string | null;
};

export type PlatformBackupWorkspace = {
  retentionDays: number;
  history: PlatformBackupRecord[];
};

export type TenantCleanupPolicy = {
  auditLogRetentionDays: number;
  notificationRetentionDays: number;
  softDeletedRetentionDays: number;
  archiveBeforePurge: boolean;
  isAutoCleanupEnabled: boolean;
  lastRunAt: string | null;
  lastPurgedAuditLogs: number;
  lastPurgedMessages: number;
  lastPurgedSoftDeleted: number;
  lastArchiveUrl: string | null;
};

export type ReportCleanupPreview = {
  eligibleAuditLogs: number;
  eligibleMessages: number;
  eligibleSoftDeletedRecords: number;
  totalEligible: number;
};

export type ReportCleanupWorkspace = {
  policy: TenantCleanupPolicy;
  preview: ReportCleanupPreview;
};

export type ReportCleanupResult = {
  dryRun: boolean;
  purgedAuditLogs: number;
  purgedMessages: number;
  purgedSoftDeletedRecords: number;
  totalPurged: number;
  archiveUrl: string | null;
  executedAt: string;
};

export type TimetableClash = {
  clashType: "Teacher" | "Batch" | "Room";
  conflictingScheduleId: string;
  conflictingScheduleName: string;
  weekday: number;
  startMinutes: number;
  endMinutes: number;
  message: string;
};

export type TimetableDayColumn = {
  weekday: number;
  slots: {
    id: string;
    name: string;
    batchId: string;
    batchName: string;
    subjectId: string;
    subjectName: string;
    teacherUserId: string;
    teacherName: string;
    weekday: number;
    startMinutes: number;
    endMinutes: string | number;
    room: string | null;
    isActive: boolean;
  }[];
};

export type WeeklyTimetable = {
  days: TimetableDayColumn[];
  clashes: TimetableClash[];
  rooms: string[];
};

export type StaffAttendanceEntry = {
  staffUserId: string;
  fullName: string;
  email: string;
  roleName: string;
  status: "Present" | "Absent" | "Late" | "HalfDay" | "OnLeave";
  checkInMinutes: number | null;
  checkOutMinutes: number | null;
  notes: string | null;
  hasApprovedLeave: boolean;
};

export type StaffAttendanceSheet = {
  date: string;
  presentCount: number;
  absentCount: number;
  lateCount: number;
  halfDayCount: number;
  onLeaveCount: number;
  entries: StaffAttendanceEntry[];
};

export type StaffLeaveRequestDto = {
  id: string;
  staffUserId: string;
  staffFullName: string;
  staffEmail: string;
  leaveType: "Casual" | "Medical" | "Annual" | "Unpaid" | "Other";
  startDate: string;
  endDate: string;
  daysCount: number;
  reason: string;
  status: "Pending" | "Approved" | "Rejected" | "Cancelled";
  reviewedByUserId: string | null;
  reviewedAt: string | null;
  reviewNotes: string | null;
  createdAt: string;
};

export type PlatformAnnouncement = {
  id: string;
  title: string;
  body: string;
  severity: "Info" | "Warning" | "Critical";
  isActive: boolean;
  expiresAt: string | null;
  createdAt: string;
};

export type MaintenanceMode = {
  isMaintenanceMode: boolean;
  message: string | null;
  updatedAt: string | null;
};

export type AuditLogItem = {
  id: string;
  tenantId: string | null;
  actorUserId: string | null;
  actorEmail: string | null;
  isImpersonated: boolean;
  action: string;
  entityName: string;
  entityId: string | null;
  detailsJson: string | null;
  createdAt: string;
};

export type SuperAdminDashboard = {
  tenants: {
    total: number;
    active: number;
    trial: number;
    expired: number;
    suspended: number;
    inactive: number;
  };
  mrr: number;
  collectedThisMonth: number;
  currency: string;
  trialsExpiring: {
    tenantId: string;
    tenantName: string;
    subdomain: string;
    planName: string;
    trialEndsAt: string | null;
    daysRemaining: number;
    contactEmail: string | null;
  }[];
  smsUsageByTenant: {
    tenantId: string;
    tenantName: string;
    subdomain: string;
    usedSegmentsThisMonth: number;
    monthlyQuota: number | null;
    extraSegmentsThisMonth: number;
    usagePercentage: number;
  }[];
  storageByTenant: {
    tenantId: string;
    tenantName: string;
    subdomain: string;
    usedBytes: number;
    limitGb: number | null;
    usagePercentage: number;
  }[];
  failedJobs: {
    deadLetterNotificationsCount: number;
    failedNotificationsCount: number;
    failedBackupsCount: number;
    recentFailures: {
      id: string;
      category: string;
      tenantName: string | null;
      target: string;
      error: string;
      failedAt: string;
    }[];
  };
  recentErrors: {
    id: string;
    source: string;
    message: string;
    tenantName: string | null;
    occurredAt: string;
  }[];
  maintenance: MaintenanceMode;
  activeAnnouncements: PlatformAnnouncement[];
};

export function formatBytes(bytes: number): string {
  if (bytes <= 0) return "0 B";
  const units = ["B", "KB", "MB", "GB"];
  let idx = 0;
  let value = bytes;
  while (value >= 1024 && idx < units.length - 1) {
    value /= 1024;
    idx++;
  }
  return `${value.toFixed(idx === 0 ? 0 : 1)} ${units[idx]}`;
}
