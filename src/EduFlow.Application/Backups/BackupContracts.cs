using EduFlow.Domain.Enums;

namespace EduFlow.Application.Backups;

public sealed record TenantBackupPolicyDto(
    bool IsScheduledEnabled,
    int RetentionDays,
    int MaxBackupsToKeep,
    int PreferredHourUtc);

public sealed record UpdateTenantBackupPolicyRequest(
    bool IsScheduledEnabled,
    int RetentionDays,
    int MaxBackupsToKeep,
    int PreferredHourUtc = 20);

public sealed record TenantBackupRecordDto(
    Guid Id,
    Guid TenantId,
    string Trigger,
    string Status,
    string FileName,
    string StorageUrl,
    long SizeBytes,
    string Sha256,
    int TotalRecords,
    int UploadedFilesCount,
    IReadOnlyDictionary<string, int> Counts,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record TenantBackupWorkspaceDto(
    TenantBackupPolicyDto Policy,
    IReadOnlyList<TenantBackupRecordDto> History);

public sealed record UploadedFileManifestItem(
    string Category,
    Guid OwnerId,
    string Label,
    string Url,
    long SizeBytes);

public sealed record TenantBackupManifest(
    int FormatVersion,
    Guid TenantId,
    string TenantSubdomain,
    string TenantName,
    DateTimeOffset CreatedAtUtc,
    string Trigger,
    int TotalRecords,
    int UploadedFilesCount,
    IReadOnlyDictionary<string, int> Counts);

public sealed record RestoreTenantBackupRequest(
    bool DryRun = true);

public sealed record BackupRestoreResultDto(
    bool DryRun,
    bool IsValid,
    Guid TenantId,
    Guid BackupId,
    string FileName,
    IReadOnlyDictionary<string, int> ArchiveCounts,
    IReadOnlyDictionary<string, int> RestorableCounts,
    int RestoredTotal,
    IReadOnlyList<string> Warnings);

public sealed record ScheduledBackupBatchResultDto(
    int TenantsChecked,
    int BackupsCreated,
    int SkippedFeatureOffCount,
    int FailedCount);

public sealed record PlatformBackupRecordDto(
    Guid Id,
    string Trigger,
    string Status,
    string FileName,
    string StorageUrl,
    long SizeBytes,
    string DatabaseProvider,
    string SqlCommandSummary,
    string Sha256,
    int RetentionDays,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record PlatformBackupWorkspaceDto(
    int RetentionDays,
    IReadOnlyList<PlatformBackupRecordDto> History);

public sealed record RunPlatformBackupRequest(
    int? RetentionDays = null);

public sealed record UpdatePlatformBackupRetentionRequest(
    int RetentionDays);

public sealed record TenantCleanupPolicyDto(
    int AuditLogRetentionDays,
    int NotificationRetentionDays,
    int SoftDeletedRetentionDays,
    bool ArchiveBeforePurge,
    bool IsAutoCleanupEnabled,
    DateTimeOffset? LastRunAt,
    int LastPurgedAuditLogs,
    int LastPurgedMessages,
    int LastPurgedSoftDeleted,
    string? LastArchiveUrl);

public sealed record UpdateTenantCleanupPolicyRequest(
    int AuditLogRetentionDays,
    int NotificationRetentionDays,
    int SoftDeletedRetentionDays,
    bool ArchiveBeforePurge = true,
    bool IsAutoCleanupEnabled = false);

public sealed record ReportCleanupPreviewDto(
    int EligibleAuditLogs,
    int EligibleMessages,
    int EligibleSoftDeletedRecords,
    int TotalEligible);

public sealed record ReportCleanupWorkspaceDto(
    TenantCleanupPolicyDto Policy,
    ReportCleanupPreviewDto Preview);

public sealed record RunReportCleanupRequest(
    bool DryRun = false);

public sealed record ReportCleanupResultDto(
    bool DryRun,
    int PurgedAuditLogs,
    int PurgedMessages,
    int PurgedSoftDeletedRecords,
    int TotalPurged,
    string? ArchiveUrl,
    DateTimeOffset ExecutedAt);
