using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class BatchGroup : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
