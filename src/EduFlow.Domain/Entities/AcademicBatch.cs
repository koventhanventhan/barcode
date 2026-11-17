using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class AcademicBatch : TenantEntity, IBranchOwned
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public Guid EducationLevelId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? BatchGroupId { get; set; }
    public string? AcademicYear { get; set; }
    public bool IsActive { get; set; } = true;
}
