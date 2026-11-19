using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class StaffRegistrationLink : TenantEntity
{
    public string Token { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public Guid? DefaultBranchId { get; set; }
}

public sealed class StaffRegistrationRequest : TenantEntity
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public RoleType RequestedRoleType { get; set; } = RoleType.Staff;
    public Guid? BranchId { get; set; }
    public string? Notes { get; set; }
    public StaffRegistrationStatus Status { get; set; } = StaffRegistrationStatus.Pending;
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
    public Guid? CreatedUserId { get; set; }
}
