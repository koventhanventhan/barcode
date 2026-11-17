using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class DemoRequest : BaseEntity
{
    public string InstituteName { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? City { get; set; }
    public int? ApproximateStudents { get; set; }
    public string? Message { get; set; }
}
