using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class AttendancePolicy : TenantEntity
{
    public int LateAfterMinutes { get; set; } = 10;
    public bool MarkAbsentOnStop { get; set; } = true;
}
