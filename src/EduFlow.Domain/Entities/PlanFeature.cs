using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class PlanFeature : BaseEntity
{
    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }
    public FeatureKey FeatureKey { get; set; }
    public bool IsIncluded { get; set; } = true;
}
