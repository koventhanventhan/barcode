using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Tenancy;

public interface ITenantManagementService
{
    Task<IReadOnlyList<BranchDto>> GetBranchesAsync(CancellationToken cancellationToken = default);
    Task<BranchDto> GetBranchByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BranchDto> CreateBranchAsync(CreateBranchRequest request, CancellationToken cancellationToken = default);
    Task<BranchDto> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionDefinition>> GetAvailablePermissionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<RoleDto> GetRoleByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);
    Task<RoleDto> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantUserDto>> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<TenantUserDto> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TenantUserDto> CreateUserAsync(CreateTenantUserRequest request, CancellationToken cancellationToken = default);
    Task<TenantUserDto> UpdateUserRoleAsync(Guid id, UpdateTenantUserRoleRequest request, CancellationToken cancellationToken = default);
    Task DeleteUserAsync(Guid id, CancellationToken cancellationToken = default);
    Task SetUserBranchesAsync(Guid id, IReadOnlyList<Guid> branchIds, CancellationToken cancellationToken = default);

    Task<TenantSubscriptionSummaryDto> GetSubscriptionSummaryAsync(CancellationToken cancellationToken = default);
}

public sealed class TenantManagementService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IBranchScope branchScope,
    IFeatureService featureService,
    ILimitService limitService,
    IAuditService auditService) : ITenantManagementService
{
    public async Task<IReadOnlyList<BranchDto>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        var query = db.Branches.AsNoTracking();
        if (!branchScope.Unrestricted)
        {
            var allowed = branchScope.AllowedBranchIds;
            query = query.Where(b => allowed.Contains(b.Id));
        }

        return await query
            .OrderByDescending(b => b.IsMain)
            .ThenBy(b => b.Name)
            .Select(b => new BranchDto(b.Id, b.Name, b.Code, b.Address, b.Phone, b.IsMain, b.IsActive, b.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<BranchDto> GetBranchByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!branchScope.Unrestricted && !branchScope.AllowedBranchIds.Contains(id))
        {
            throw new EntityNotFoundException(nameof(Branch), id.ToString());
        }

        var branch = await db.Branches
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Branch), id.ToString());

        return new BranchDto(branch.Id, branch.Name, branch.Code, branch.Address, branch.Phone, branch.IsMain, branch.IsActive, branch.CreatedAt);
    }

    public async Task<BranchDto> CreateBranchAsync(CreateBranchRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        var tenantId = tenantContext.TenantId
            ?? throw new DomainValidationException("TENANT_REQUIRED", "Tenant context is required.");
        if (await db.Branches.AnyAsync(cancellationToken))
        {
            await EnsureMultiBranchEnabledAsync(tenantId, cancellationToken);
        }

        var duplicate = await db.Branches.AnyAsync(b => b.Code == code, cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("BRANCH_CODE_EXISTS", $"Branch with code '{code}' already exists.", 409);
        }

        var branch = new Branch
        {
            Name = request.Name.Trim(),
            Code = code,
            Address = request.Address,
            Phone = request.Phone,
            IsMain = request.IsMain,
            IsActive = true
        };

        db.Branches.Add(branch);

        await auditService.LogAsync(
            "Branch.Created",
            nameof(Branch),
            branch.Id.ToString(),
            new { branch.Name, branch.Code },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new BranchDto(branch.Id, branch.Name, branch.Code, branch.Address, branch.Phone, branch.IsMain, branch.IsActive, branch.CreatedAt);
    }

    public async Task<BranchDto> UpdateBranchAsync(Guid id, UpdateBranchRequest request, CancellationToken cancellationToken = default)
    {
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Branch), id.ToString());

        branch.Name = request.Name.Trim();
        branch.Code = request.Code.Trim().ToUpperInvariant();
        branch.Address = request.Address;
        branch.Phone = request.Phone;
        branch.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return new BranchDto(branch.Id, branch.Name, branch.Code, branch.Address, branch.Phone, branch.IsMain, branch.IsActive, branch.CreatedAt);
    }

    public async Task DeleteBranchAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var branch = await db.Branches.FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Branch), id.ToString());

        if (branch.IsMain)
        {
            throw new DomainValidationException("CANNOT_DELETE_MAIN_BRANCH", "Main branch cannot be deleted.", 400);
        }

        db.Branches.Remove(branch);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<PermissionDefinition>> GetAvailablePermissionsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Permissions.All);

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .Include(r => r.Permissions)
            .OrderBy(r => r.Name)
            .ToListAsync(cancellationToken);

        return roles.Select(MapRole).ToList();
    }

    public async Task<RoleDto> GetRoleByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles
            .AsNoTracking()
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Role), id.ToString());

        return MapRole(role);
    }

    public async Task<RoleDto> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePermissions(request.Permissions);

        var name = request.Name.Trim();
        var duplicate = await db.Roles.AnyAsync(r => r.Name.ToLower() == name.ToLower(), cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("ROLE_NAME_EXISTS", $"Role '{name}' already exists.", 409);
        }

        var role = new Role
        {
            Name = name,
            Description = request.Description,
            IsSystemRole = false,
            Permissions = request.Permissions
                .Distinct()
                .Select(p => new RolePermission { PermissionKey = p })
                .ToList()
        };

        db.Roles.Add(role);

        await auditService.LogAsync(
            "Role.Created",
            nameof(Role),
            role.Id.ToString(),
            new { role.Name, Permissions = request.Permissions },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return MapRole(role);
    }

    public async Task<RoleDto> UpdateRoleAsync(Guid id, UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        ValidatePermissions(request.Permissions);

        var role = await db.Roles
            .Include(r => r.Permissions)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Role), id.ToString());

        role.Name = request.Name.Trim();
        role.Description = request.Description;

        foreach (var existing in role.Permissions.Where(p => !p.IsDeleted))
        {
            existing.IsDeleted = true;
        }

        foreach (var perm in request.Permissions.Distinct())
        {
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = role.Id,
                PermissionKey = perm
            });
        }

        await auditService.LogAsync(
            "Role.Updated",
            nameof(Role),
            role.Id.ToString(),
            new { role.Name, Permissions = request.Permissions },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return await GetRoleByIdAsync(id, cancellationToken);
    }

    public async Task DeleteRoleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Role), id.ToString());

        if (role.IsSystemRole)
        {
            throw new DomainValidationException("CANNOT_DELETE_SYSTEM_ROLE", "System roles cannot be deleted.", 400);
        }

        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TenantUserDto>> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        return await db.Users
            .AsNoTracking()
            .Include(u => u.CustomRole)
            .OrderBy(u => u.FullName)
            .Select(u => new TenantUserDto(
                u.Id,
                u.Email,
                u.FullName,
                u.Phone,
                u.RoleType.ToString(),
                u.CustomRoleId,
                u.CustomRole != null ? u.CustomRole.Name : null,
                u.BranchId,
                u.IsActive,
                u.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantUserDto> GetUserByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var u = await db.Users
            .AsNoTracking()
            .Include(x => x.CustomRole)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(User), id.ToString());

        return new TenantUserDto(
            u.Id,
            u.Email,
            u.FullName,
            u.Phone,
            u.RoleType.ToString(),
            u.CustomRoleId,
            u.CustomRole?.Name,
            u.BranchId,
            u.IsActive,
            u.CreatedAt);
    }

    public async Task<TenantUserDto> CreateUserAsync(
        CreateTenantUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new DomainValidationException("TENANT_REQUIRED", "Tenant context is required.");

        if (request.RoleType == RoleType.SuperAdmin)
        {
            throw new DomainValidationException("INVALID_ROLE", "Cannot create a SuperAdmin inside a tenant.");
        }

        PasswordPolicy.ValidateOrThrow(request.Password);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var duplicate = await db.Users.AnyAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException("EMAIL_ALREADY_EXISTS", "A user with this email already exists in this institute.", 409);
        }

        if (request.RoleType == RoleType.InstituteAdmin)
        {
            await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.AdminAccounts, cancellationToken: cancellationToken);
        }
        else if (request.RoleType is RoleType.Staff or RoleType.Teacher)
        {
            await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.StaffAccounts, cancellationToken: cancellationToken);
        }

        Role? customRole = null;
        if (request.CustomRoleId.HasValue)
        {
            customRole = await db.Roles.FirstOrDefaultAsync(r => r.Id == request.CustomRoleId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Role), request.CustomRoleId.Value.ToString());
        }

        var branchIds = await ResolveBranchIdsAsync(
            tenantId,
            request.RoleType,
            request.BranchIds is { Count: > 0 } ? request.BranchIds : request.BranchId is Guid one ? [one] : [],
            cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            Phone = request.Phone,
            PasswordHash = PasswordHasher.HashPassword(request.Password),
            RoleType = request.RoleType,
            CustomRoleId = request.CustomRoleId,
            BranchId = branchIds.FirstOrDefault() is Guid first && first != Guid.Empty ? first : request.BranchId,
            IsActive = true
        };
        if (branchIds.Count > 0)
        {
            user.BranchId = branchIds[0];
        }

        db.Users.Add(user);
        if (request.RoleType is RoleType.Staff or RoleType.Teacher)
        {
            foreach (var branchId in branchIds)
            {
                db.StaffBranchAssignments.Add(new StaffBranchAssignment
                {
                    UserId = user.Id,
                    BranchId = branchId
                });
            }
        }

        await auditService.LogAsync(
            "User.Created",
            nameof(User),
            user.Id.ToString(),
            new { user.Email, RoleType = user.RoleType.ToString(), user.CustomRoleId },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new TenantUserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Phone,
            user.RoleType.ToString(),
            user.CustomRoleId,
            customRole?.Name,
            user.BranchId,
            user.IsActive,
            user.CreatedAt);
    }

    public async Task<TenantUserDto> UpdateUserRoleAsync(
        Guid id,
        UpdateTenantUserRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RoleType == RoleType.SuperAdmin)
        {
            throw new DomainValidationException("INVALID_ROLE", "Cannot assign SuperAdmin role inside a tenant.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(User), id.ToString());

        var tenantId = tenantContext.TenantId!.Value;
        if (user.RoleType != request.RoleType)
        {
            if (request.RoleType == RoleType.InstituteAdmin)
            {
                await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.AdminAccounts, cancellationToken: cancellationToken);
            }
            else if (request.RoleType is RoleType.Staff or RoleType.Teacher
                     && user.RoleType is not (RoleType.Staff or RoleType.Teacher))
            {
                await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.StaffAccounts, cancellationToken: cancellationToken);
            }
        }

        if (request.CustomRoleId.HasValue)
        {
            var roleExists = await db.Roles.AnyAsync(r => r.Id == request.CustomRoleId.Value, cancellationToken);
            if (!roleExists)
            {
                throw new EntityNotFoundException(nameof(Role), request.CustomRoleId.Value.ToString());
            }
        }

        user.RoleType = request.RoleType;
        user.CustomRoleId = request.CustomRoleId;
        user.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);

        return await GetUserByIdAsync(id, cancellationToken);
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(User), id.ToString());

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TenantSubscriptionSummaryDto> GetSubscriptionSummaryAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new DomainValidationException("TENANT_REQUIRED", "Tenant context is required.");

        var subscription = await db.Subscriptions
            .Include(s => s.Plan)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Subscription), tenantId.ToString());

        var features = await featureService.GetAllFeaturesAsync(tenantId, cancellationToken);
        var limits = await limitService.GetAllLimitsAsync(tenantId, cancellationToken);

        return new TenantSubscriptionSummaryDto(
            subscription.Id,
            subscription.Plan?.Code ?? string.Empty,
            subscription.Plan?.Name ?? string.Empty,
            subscription.Status.ToString(),
            subscription.StartDate,
            subscription.EndDate,
            subscription.TrialEndsAt,
            subscription.IsEffectivelyExpired(DateTimeOffset.UtcNow) || tenantContext.IsReadOnly,
            features,
            limits);
    }

    private static void ValidatePermissions(IReadOnlyList<string> permissionKeys)
    {
        var validSet = Permissions.All.Select(p => p.Key).ToHashSet();
        var invalid = permissionKeys.Where(k => !validSet.Contains(k)).ToList();
        if (invalid.Count > 0)
        {
            throw new DomainValidationException(
                "INVALID_PERMISSION",
                $"Unknown permission keys: {string.Join(", ", invalid)}");
        }
    }

    private static RoleDto MapRole(Role role) =>
        new(
            role.Id,
            role.Name,
            role.Description,
            role.IsSystemRole,
            role.Permissions.Where(p => !p.IsDeleted).Select(p => p.PermissionKey).OrderBy(p => p).ToList(),
            role.CreatedAt);

    public async Task SetUserBranchesAsync(Guid id, IReadOnlyList<Guid> branchIds, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new DomainValidationException("TENANT_REQUIRED", "Tenant context is required.");
        await EnsureMultiBranchEnabledAsync(tenantId, cancellationToken);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(User), id.ToString());
        if (user.RoleType is not (RoleType.Staff or RoleType.Teacher))
        {
            throw new DomainValidationException("BRANCH_ROLE", "Only staff and teachers can be limited to branches.");
        }

        var resolved = await ResolveBranchIdsAsync(tenantId, user.RoleType, branchIds, cancellationToken);
        if (resolved.Count == 0)
        {
            throw new DomainValidationException("BRANCH_REQUIRED", "Choose at least one branch.");
        }

        var existing = await db.StaffBranchAssignments.Where(item => item.UserId == user.Id).ToListAsync(cancellationToken);
        foreach (var row in existing)
        {
            row.IsDeleted = !resolved.Contains(row.BranchId);
        }

        foreach (var branchId in resolved)
        {
            if (existing.All(row => row.BranchId != branchId))
            {
                db.StaffBranchAssignments.Add(new StaffBranchAssignment { UserId = user.Id, BranchId = branchId });
            }
        }

        user.BranchId = resolved[0];
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<Guid>> ResolveBranchIdsAsync(
        Guid tenantId,
        RoleType roleType,
        IReadOnlyList<Guid> branchIds,
        CancellationToken cancellationToken)
    {
        var distinct = branchIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return [];
        }

        if (distinct.Count > 1 && roleType is RoleType.Staff or RoleType.Teacher)
        {
            await EnsureMultiBranchEnabledAsync(tenantId, cancellationToken);
        }

        var found = await db.Branches.CountAsync(branch => distinct.Contains(branch.Id), cancellationToken);
        if (found != distinct.Count)
        {
            throw new EntityNotFoundException(nameof(Branch));
        }

        return distinct;
    }

    private async Task EnsureMultiBranchEnabledAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var evaluation = await featureService.EvaluateAsync(tenantId, FeatureKey.MultiBranch, cancellationToken);
        if (!evaluation.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.MultiBranch);
        }

        if (!evaluation.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.MultiBranch);
        }
    }
}
