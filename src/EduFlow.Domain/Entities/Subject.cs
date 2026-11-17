using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class Subject : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public Guid? EducationLevelId { get; set; }
    public bool IsActive { get; set; } = true;
}
