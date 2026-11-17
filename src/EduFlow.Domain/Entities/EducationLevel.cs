using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class EducationLevel : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
