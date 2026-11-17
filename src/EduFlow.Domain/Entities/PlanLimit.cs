using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class PlanLimit : BaseEntity
{
    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }
    public LimitKey LimitKey { get; set; }
    public int? MaxValue { get; set; }
}
