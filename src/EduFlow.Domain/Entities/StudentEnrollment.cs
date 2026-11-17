using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class StudentEnrollment : TenantEntity
{
    public Guid StudentId { get; set; }
    public Guid BatchId { get; set; }
    public DateOnly EnrolledOn { get; set; }
    public bool IsActive { get; set; } = true;
}
