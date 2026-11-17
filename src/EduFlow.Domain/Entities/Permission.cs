using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class Permission : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
