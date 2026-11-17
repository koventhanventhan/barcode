using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class TenantFeatureOverride : TenantEntity
{
    public FeatureKey FeatureKey { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Reason { get; set; }
    public Guid? GrantedByUserId { get; set; }
}
