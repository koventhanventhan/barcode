using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class Subscription : TenantEntity
{
    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;
    public DateTimeOffset StartDate { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndDate { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public string? SuspendedReason { get; set; }
    public int ExtraStudentsAllowed { get; set; }
    public string? Notes { get; set; }

    public bool IsEffectivelyExpired(DateTimeOffset now)
    {
        if (Status == SubscriptionStatus.Expired)
        {
            return true;
        }

        if (Status == SubscriptionStatus.Trial && TrialEndsAt.HasValue && TrialEndsAt.Value < now)
        {
            return true;
        }

        if (EndDate.HasValue && EndDate.Value < now)
        {
            return true;
        }

        return false;
    }
}
