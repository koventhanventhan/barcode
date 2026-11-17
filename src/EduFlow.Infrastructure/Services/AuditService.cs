using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Domain.Entities;

namespace EduFlow.Infrastructure.Services;

public sealed class AuditService(IAppDbContext db, ITenantContext tenantContext) : IAuditService
{
    public Task LogAsync(
        string action,
        string entityName,
        string? entityId = null,
        object? details = null,
        Guid? tenantIdOverride = null,
        Guid? actorUserIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTenantId = tenantIdOverride ?? tenantContext.TenantId;
        if (effectiveTenantId == Guid.Empty)
        {
            effectiveTenantId = null;
        }

        var log = new AuditLog
        {
            TenantId = effectiveTenantId,
            ActorUserId = actorUserIdOverride ?? tenantContext.UserId,
            ActorEmail = tenantContext.UserEmail,
            IsImpersonated = tenantContext.IsImpersonating,
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            DetailsJson = PiiRedactor.SerializeAuditDetails(details),
            IpAddress = tenantContext.IpAddress
        };

        db.AuditLogs.Add(log);
        return Task.CompletedTask;
    }
}
