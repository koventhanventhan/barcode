using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class ParentStudentLink : TenantEntity
{
    public Guid ParentUserId { get; set; }
    public Guid StudentId { get; set; }
}
