namespace EduFlow.Application.Common.Interfaces;

public sealed record DatabaseBackupExecutionResult(
    string DatabaseProvider,
    string SqlCommandSummary,
    byte[]? NativeBackupBytes);

public interface IDatabaseBackupExecutor
{
    Task<DatabaseBackupExecutionResult> ExecutePlatformDatabaseBackupAsync(CancellationToken cancellationToken = default);
}
