using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class StaffBranchAssignment : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid BranchId { get; set; }
}
