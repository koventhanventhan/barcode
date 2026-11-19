using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Enums;

namespace EduFlow.Application.Tenancy;

public sealed record BranchDto(
    Guid Id,
    string Name,
    string Code,
    string? Address,
    string? Phone,
    bool IsMain,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateBranchRequest(
    string Name,
    string Code,
    string? Address = null,
    string? Phone = null,
    bool IsMain = false);

public sealed record UpdateBranchRequest(
    string Name,
    string Code,
    string? Address = null,
    string? Phone = null,
    bool IsActive = true);

public sealed record RoleDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystemRole,
    IReadOnlyList<string> Permissions,
    DateTimeOffset CreatedAt);

public sealed record CreateRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(
    string Name,
    string? Description,
    IReadOnlyList<string> Permissions);

public sealed record TenantUserDto(
    Guid Id,
    string Email,
    string FullName,
    string? Phone,
    string RoleType,
    Guid? CustomRoleId,
    string? CustomRoleName,
    Guid? BranchId,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record CreateTenantUserRequest(
    string Email,
    string FullName,
    string Password,
    RoleType RoleType,
    string? Phone = null,
    Guid? CustomRoleId = null,
    Guid? BranchId = null,
    IReadOnlyList<Guid>? BranchIds = null);

public sealed record CreateTenantUserFromHashRequest(
    string Email,
    string FullName,
    string PasswordHash,
    RoleType RoleType,
    string? Phone = null,
    Guid? CustomRoleId = null,
    Guid? BranchId = null,
    IReadOnlyList<Guid>? BranchIds = null);

public sealed record SetUserBranchesRequest(IReadOnlyList<Guid> BranchIds);

public sealed record BranchScopeDto(bool CanViewAll, IReadOnlyList<BranchDto> Branches);

public sealed record UpdateTenantUserRoleRequest(
    RoleType RoleType,
    Guid? CustomRoleId = null,
    bool IsActive = true);

public sealed record TenantSubscriptionSummaryDto(
    Guid SubscriptionId,
    string PlanCode,
    string PlanName,
    string Status,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate,
    DateTimeOffset? TrialEndsAt,
    bool IsReadOnly,
    IReadOnlyList<TenantFeatureStatusDto> Features,
    IReadOnlyList<LimitUsageDto> Limits);
