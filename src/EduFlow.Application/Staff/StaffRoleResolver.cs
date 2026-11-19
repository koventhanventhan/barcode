using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Staff;

internal static class StaffRoleResolver
{
    public static async Task<(RoleType RoleType, Guid RoleId)> ResolveAsync(
        IAppDbContext db,
        string? templateKey,
        string? rawRoleType,
        IReadOnlyList<string>? requestedPermissions,
        string? customRoleName,
        string fallbackLabel,
        CancellationToken cancellationToken)
    {
        var template = StaffRoleTemplates.Find(templateKey);
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

        if (!Enum.TryParse<RoleType>(rawRoleType, true, out var roleType) || roleType is not (RoleType.Staff or RoleType.Teacher))
        {
            throw new DomainValidationException("INVALID_ROLE", "Choose a role template, or set the role to Staff or Teacher.");
        }

        var permissions = requestedPermissions?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
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

        var roleName = string.IsNullOrWhiteSpace(customRoleName)
            ? $"{fallbackLabel.Trim()} access"
            : customRoleName.Trim();
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
