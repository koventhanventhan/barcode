using EduFlow.Domain.Enums;

namespace EduFlow.Application.Auth;

public sealed record LoginRequest(string? Email, string Password, string? Phone = null);

public sealed record AuthUserDto(
    Guid Id,
    Guid? TenantId,
    string Email,
    string FullName,
    string Role,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    AuthUserDto User);

public sealed record RefreshRequest(string? RefreshToken = null);

public sealed record LogoutRequest(string? RefreshToken = null);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ForgotPasswordRequest(string? Email = null, string? Phone = null);

public sealed record ResetPasswordRequest(string? Email, string Token, string NewPassword, string? Phone = null);

public sealed record InviteUserRequest(
    string Email,
    string FullName,
    RoleType RoleType,
    Guid? CustomRoleId = null,
    Guid? BranchId = null);

public sealed record InviteUserResponse(
    Guid UserId,
    string Email,
    DateTimeOffset ExpiresAt);

public sealed record AcceptInviteRequest(
    string? Email,
    string Token,
    string Password,
    string? FullName = null,
    string? Phone = null);
