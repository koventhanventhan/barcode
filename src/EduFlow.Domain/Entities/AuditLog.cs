using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class AuditLog : BaseEntity
{
    public Guid? TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public bool IsImpersonated { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? DetailsJson { get; set; }
    public string? IpAddress { get; set; }
}
