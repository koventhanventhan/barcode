using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class AttendanceMark : TenantEntity
{
    public Guid ClassSessionId { get; set; }
    public Guid StudentId { get; set; }
    public AttendanceMarkStatus Status { get; set; }
    public AttendanceMarkSource Source { get; set; }
    public DateTimeOffset MarkedAt { get; set; }
    public Guid? MarkedByUserId { get; set; }
    public string? Reason { get; set; }
    public bool IsUndone { get; set; }
    public DateTimeOffset? UndoneAt { get; set; }
    public string? UndoReason { get; set; }
    public bool PresentNotified { get; set; }
    public bool AbsentNotified { get; set; }
}
