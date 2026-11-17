using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Staff;

public interface IStaffDirectoryService
{
    IReadOnlyList<StaffRoleTemplateDto> GetTemplates();
    Task<TenantUserDto> CreateAsync(CreateStaffRequest request, CancellationToken cancellationToken = default);
    Task<TenantUserDto> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);
}

public sealed class StaffDirectoryService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ITenantManagementService tenants) : IStaffDirectoryService
{
    public IReadOnlyList<StaffRoleTemplateDto> GetTemplates() =>
        StaffRoleTemplates.All
            .Select(template => new StaffRoleTemplateDto(
                template.Key,
                template.Name,
                template.RoleType.ToString(),
                template.Permissions))
            .ToList();

    public async Task<TenantUserDto> CreateAsync(CreateStaffRequest request, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var (roleType, roleId) = await ResolveRoleAsync(request, cancellationToken);
        return await tenants.CreateUserAsync(
            new CreateTenantUserRequest(
                request.Email,
                request.FullName,
                request.Password,
                roleType,
                request.Phone,
                roleId,
                request.BranchId),
            cancellationToken);
    }

    public async Task<TenantUserDto> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var actorId = tenantContext.UserId;
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(User), userId.ToString());

        if (!isActive && actorId == user.Id)
        {
            throw new DomainValidationException("CANNOT_DISABLE_SELF", "You cannot disable your own account.");
        }

        if (!isActive && user.RoleType == RoleType.InstituteAdmin)
        {
            var otherAdmins = await db.Users.CountAsync(
                item => item.IsActive && item.Id != user.Id && item.RoleType == RoleType.InstituteAdmin,
                cancellationToken);
            if (otherAdmins == 0)
            {
                throw new DomainValidationException("LAST_ADMIN", "Keep at least one active institute admin.");
            }
        }

        user.IsActive = isActive;
        await db.SaveChangesAsync(cancellationToken);
        return await tenants.GetUserByIdAsync(user.Id, cancellationToken);
    }

    private async Task<(RoleType RoleType, Guid RoleId)> ResolveRoleAsync(CreateStaffRequest request, CancellationToken cancellationToken)
    {
        var template = StaffRoleTemplates.Find(request.TemplateKey);
        if (template is not null)
        {
            var existing = await db.Roles.FirstOrDefaultAsync(role => role.Name == template.Name, cancellationToken);
            if (existing is null)
            {
                existing = new Role
                {
                    Name = template.Name,
                    Description = $"{template.Name} role template",
                    IsSystemRole = true,
                    Permissions = template.Permissions.Select(key => new RolePermission { PermissionKey = key }).ToList()
                };
                db.Roles.Add(existing);
                await db.SaveChangesAsync(cancellationToken);
            }

            return (template.RoleType, existing.Id);
        }

        if (!Enum.TryParse<RoleType>(request.RoleType, true, out var roleType) || roleType is not (RoleType.Staff or RoleType.Teacher))
        {
            throw new DomainValidationException("INVALID_ROLE", "Choose a role template, or set the role to Staff or Teacher.");
        }

        var permissions = request.Permissions?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        if (permissions.Count == 0)
        {
            throw new DomainValidationException("VALIDATION", "Choose at least one permission or a role template.");
        }

        var valid = Permissions.All.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = permissions.Where(key => !valid.Contains(key)).ToList();
        if (invalid.Count > 0)
        {
            throw new DomainValidationException("INVALID_PERMISSION", $"Unknown permission keys: {string.Join(", ", invalid)}");
        }

        var roleName = string.IsNullOrWhiteSpace(request.RoleName)
            ? $"{request.FullName.Trim()} access"
            : request.RoleName.Trim();
        if (roleName.Length > 100)
        {
            roleName = roleName[..100];
        }

        var duplicate = await db.Roles.AnyAsync(role => role.Name.ToLower() == roleName.ToLower(), cancellationToken);
        if (duplicate)
        {
            roleName = $"{roleName} {Guid.NewGuid().ToString("N")[..4]}";
        }

        var role = new Role
        {
            Name = roleName,
            Description = "Custom staff access",
            IsSystemRole = false,
            Permissions = permissions.Select(key => new RolePermission { PermissionKey = key }).ToList()
        };
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        return (roleType, role.Id);
    }
}
