using EduFlow.Application.Common.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Infrastructure.Persistence;

public sealed class DatabaseBackupExecutor(AppDbContext db) : IDatabaseBackupExecutor
{
    public async Task<DatabaseBackupExecutionResult> ExecutePlatformDatabaseBackupAsync(CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlServer())
        {
            var connString = db.Database.GetConnectionString() ?? string.Empty;
            var builder = new SqlConnectionStringBuilder(connString);
            var dbName = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "Admino" : builder.InitialCatalog;
            var safeDbName = dbName.Replace("]", "]]", StringComparison.Ordinal);
            var tempBakPath = Path.Combine(Path.GetTempPath(), $"admino-{Guid.NewGuid():N}.bak");
            var sqlCommand = $"BACKUP DATABASE [{safeDbName}] TO DISK = @p0 WITH INIT";

            try
            {
                await db.Database.ExecuteSqlRawAsync(sqlCommand, [tempBakPath], cancellationToken);
                byte[]? bytes = null;
                if (File.Exists(tempBakPath))
                {
                    bytes = await File.ReadAllBytesAsync(tempBakPath, cancellationToken);
                    File.Delete(tempBakPath);
                }

                return new DatabaseBackupExecutionResult(
                    "SqlServer",
                    $"BACKUP DATABASE [{dbName}] TO DISK = '{Path.GetFileName(tempBakPath)}' WITH INIT",
                    bytes);
            }
            catch
            {
                if (File.Exists(tempBakPath))
                {
                    File.Delete(tempBakPath);
                }

                return new DatabaseBackupExecutionResult(
                    "SqlServer",
                    $"BACKUP DATABASE [{dbName}] TO DISK = 'admino-platform.bak' WITH INIT",
                    null);
            }
        }

        return new DatabaseBackupExecutionResult(
            db.Database.ProviderName ?? "Relational",
            "BACKUP DATABASE [Admino] TO DISK = 'admino-platform.bak' WITH INIT",
            null);
    }
}
