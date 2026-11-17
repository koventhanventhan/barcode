using EduFlow.Application.Backups;
using EduFlow.Domain.Enums;

namespace EduFlow.Infrastructure.Backups;

public sealed class TenantAutoBackupHangfireJob(IBackupAndCleanupService backupService)
{
    public const string RecurringJobId = "tenant-auto-backup";

    public Task<ScheduledBackupBatchResultDto> ExecuteAsync(CancellationToken cancellationToken = default) =>
        backupService.RunScheduledTenantBackupsAsync(null, cancellationToken);
}

public sealed class PlatformDatabaseBackupHangfireJob(IBackupAndCleanupService backupService)
{
    public const string RecurringJobId = "platform-db-backup";

    public Task<PlatformBackupRecordDto> ExecuteAsync(CancellationToken cancellationToken = default) =>
        backupService.RunPlatformBackupAsync(BackupTrigger.Scheduled, null, cancellationToken);
}
