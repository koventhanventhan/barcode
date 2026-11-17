using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class TenantFeatureSetting : TenantEntity
{
    public FeatureKey FeatureKey { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Guid? UpdatedBy { get; set; }
}
