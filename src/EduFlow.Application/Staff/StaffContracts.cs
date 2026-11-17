namespace EduFlow.Application.Staff;

public sealed record StaffRoleTemplateDto(
    string Key,
    string Name,
    string RoleType,
    IReadOnlyList<string> Permissions);

public sealed record CreateStaffRequest(
    string Email,
    string FullName,
    string Password,
    string? TemplateKey,
    string? Phone = null,
    Guid? BranchId = null,
    string? RoleType = null,
    IReadOnlyList<string>? Permissions = null,
    string? RoleName = null);

public sealed record SetUserActiveRequest(bool IsActive);
