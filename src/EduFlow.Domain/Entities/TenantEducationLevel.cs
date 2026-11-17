using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class TenantEducationLevel : TenantEntity
{
    public Guid EducationLevelId { get; set; }
    public bool IsEnabled { get; set; }
}
