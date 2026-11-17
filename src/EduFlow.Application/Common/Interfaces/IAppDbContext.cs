using EduFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Common.Interfaces;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Branch> Branches { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Plan> Plans { get; }
    DbSet<PlanFeature> PlanFeatures { get; }
    DbSet<PlanLimit> PlanLimits { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<TenantFeatureSetting> TenantFeatureSettings { get; }
    DbSet<TenantFeatureOverride> TenantFeatureOverrides { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<BrandingProfile> BrandingProfiles { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
