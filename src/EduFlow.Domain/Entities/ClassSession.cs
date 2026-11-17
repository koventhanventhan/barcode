using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class ClassSession : TenantEntity
{
    public Guid ClassScheduleId { get; set; }
    public DateOnly SessionDate { get; set; }
    public ClassSessionStatus Status { get; set; } = ClassSessionStatus.Scheduled;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? StartedByUserId { get; set; }
    public string? JoinCode { get; set; }
    public string? CancelReason { get; set; }
    public bool StartedNotified { get; set; }
    public bool StoppedNotified { get; set; }
    public bool CancelledNotified { get; set; }
}
