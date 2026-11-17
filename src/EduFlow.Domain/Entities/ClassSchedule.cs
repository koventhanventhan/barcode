using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class ClassSchedule : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid BatchId { get; set; }
    public Guid SubjectId { get; set; }
    public Guid TeacherUserId { get; set; }
    public int Weekday { get; set; }
    public int StartMinutes { get; set; }
    public int EndMinutes { get; set; }
    public string? Room { get; set; }
    public bool IsActive { get; set; } = true;
}
