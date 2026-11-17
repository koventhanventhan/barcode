using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class TenantBackupPolicy : TenantEntity
{
    public bool IsScheduledEnabled { get; set; } = true;
    public int RetentionDays { get; set; } = 30;
    public int MaxBackupsToKeep { get; set; } = 10;
    public int PreferredHourUtc { get; set; } = 20;
}

public sealed class TenantBackupRecord : TenantEntity
{
    public BackupTrigger Trigger { get; set; } = BackupTrigger.Manual;
    public BackupStatus Status { get; set; } = BackupStatus.Completed;
    public string FileName { get; set; } = string.Empty;
    public string StorageUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int UploadedFilesCount { get; set; }
    public string SummaryJson { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TenantCleanupPolicy : TenantEntity
{
    public int AuditLogRetentionDays { get; set; } = 90;
    public int NotificationRetentionDays { get; set; } = 90;
    public int SoftDeletedRetentionDays { get; set; } = 30;
    public bool ArchiveBeforePurge { get; set; } = true;
    public bool IsAutoCleanupEnabled { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public int LastPurgedAuditLogs { get; set; }
    public int LastPurgedMessages { get; set; }
    public int LastPurgedSoftDeleted { get; set; }
    public string? LastArchiveUrl { get; set; }
}

public sealed class PlatformBackupRecord : BaseEntity
{
    public BackupTrigger Trigger { get; set; } = BackupTrigger.Manual;
    public BackupStatus Status { get; set; } = BackupStatus.Completed;
    public string FileName { get; set; } = string.Empty;
    public string StorageUrl { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string DatabaseProvider { get; set; } = string.Empty;
    public string SqlCommandSummary { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int RetentionDays { get; set; } = 14;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class PlatformAnnouncement : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public AnnouncementSeverity Severity { get; set; } = AnnouncementSeverity.Info;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? ExpiresAt { get; set; }
    public Guid? PublishedByUserId { get; set; }
}

public sealed class PlatformSetting : BaseEntity
{
    public string Key { get; set; } = "default";
    public bool IsMaintenanceMode { get; set; }
    public string? MaintenanceMessage { get; set; }
    public int PlatformBackupRetentionDays { get; set; } = 14;
    public Guid? UpdatedByUserId { get; set; }
}
