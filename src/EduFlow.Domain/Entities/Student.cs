using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class Student : TenantEntity
{
    public string StudentCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateOnly? DateOfBirth { get; set; }
    public StudentGender Gender { get; set; } = StudentGender.Unspecified;
    public string? PhotoUrl { get; set; }
    public long PhotoBytes { get; set; }
    public string GuardianName { get; set; } = string.Empty;
    public string GuardianPhone { get; set; } = string.Empty;
    public string? GuardianPhoneSecondary { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public Guid EducationLevelId { get; set; }
    public Guid? BranchId { get; set; }
    public StudentStatus Status { get; set; } = StudentStatus.Active;
    public DateOnly AdmissionDate { get; set; }
    public string? Notes { get; set; }
}
