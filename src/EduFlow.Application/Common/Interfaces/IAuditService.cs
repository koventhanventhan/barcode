namespace EduFlow.Application.Common.Interfaces;

public interface IAuditService
{
    Task LogAsync(
        string action,
        string entityName,
        string? entityId = null,
        object? details = null,
        Guid? tenantIdOverride = null,
        Guid? actorUserIdOverride = null,
        CancellationToken cancellationToken = default);
}
