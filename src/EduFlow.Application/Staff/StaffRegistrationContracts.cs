using EduFlow.Application.Academics;
using EduFlow.Domain.Enums;

namespace EduFlow.Application.Staff;

public sealed record StaffRegistrationLinkDto(
    bool HasLink,
    bool IsEnabled,
    string? Token,
    string? RegistrationUrl,
    QrMatrixDto? Qr,
    Guid? DefaultBranchId,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateStaffRegistrationLinkRequest(
    bool IsEnabled,
    Guid? DefaultBranchId = null);

public sealed record StaffRegistrationRequestDto(
    Guid Id,
    string FullName,
    string Email,
    string? Phone,
    string RequestedRoleType,
    Guid? BranchId,
    string? BranchName,
    string? Notes,
    string Status,
    Guid? ReviewedByUserId,
    DateTimeOffset? ReviewedAt,
    string? ReviewNotes,
    Guid? CreatedUserId,
    DateTimeOffset CreatedAt);

public sealed record StaffRegistrationPageDto(
    IReadOnlyList<StaffRegistrationRequestDto> Items,
    int TotalCount,
    int PendingCount,
    int Page,
    int PageSize);

public sealed record ApproveStaffRegistrationRequest(
    string? TemplateKey = null,
    string? RoleType = null,
    IReadOnlyList<string>? Permissions = null,
    string? RoleName = null,
    Guid? BranchId = null,
    IReadOnlyList<Guid>? BranchIds = null,
    string? ReviewNotes = null);

public sealed record RejectStaffRegistrationRequest(
    string? ReviewNotes = null);

public sealed record PublicBranchOptionDto(Guid Id, string Name);

public sealed record PublicStaffRegistrationInfoDto(
    string InstituteName,
    string? Tagline,
    string? LogoUrl,
    string PrimaryColor,
    string AccentColor,
    Guid? DefaultBranchId,
    IReadOnlyList<PublicBranchOptionDto> Branches);

public sealed record SubmitStaffRegistrationRequest(
    string FullName,
    string Email,
    string Password,
    string? Phone = null,
    RoleType RequestedRoleType = RoleType.Staff,
    Guid? BranchId = null,
    string? Notes = null);

public sealed record PublicStaffRegistrationSubmittedDto(
    Guid RequestId,
    string Status,
    string Message);
