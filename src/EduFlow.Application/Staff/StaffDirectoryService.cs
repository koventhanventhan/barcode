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
        var (roleType, roleId) = await StaffRoleResolver.ResolveAsync(
            db,
            request.TemplateKey,
            request.RoleType,
            request.Permissions,
            request.RoleName,
            request.FullName,
            cancellationToken);
        return await tenants.CreateUserAsync(
            new CreateTenantUserRequest(
                request.Email,
                request.FullName,
                request.Password,
                roleType,
                request.Phone,
                roleId,
                request.BranchId,
                request.BranchIds),
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
}
