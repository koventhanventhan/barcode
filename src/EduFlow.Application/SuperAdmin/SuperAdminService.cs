using System.Text.RegularExpressions;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.SuperAdmin;

public interface ISuperAdminService
{
    Task<IReadOnlyList<TenantSummaryDto>> GetTenantsAsync(CancellationToken cancellationToken = default);
    Task<TenantDetailDto> GetTenantByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantDetailDto> CreateTenantAsync(CreateTenantRequest request, CancellationToken cancellationToken = default);
    Task<TenantDetailDto> UpdateTenantAsync(Guid tenantId, UpdateTenantRequest request, CancellationToken cancellationToken = default);
    Task DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlanDto>> GetPlansAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<PlanDto> GetPlanByIdAsync(Guid planId, CancellationToken cancellationToken = default);
    Task<PlanDto> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default);
    Task<PlanDto> UpdatePlanAsync(Guid planId, UpdatePlanRequest request, CancellationToken cancellationToken = default);
    Task DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default);

    Task<SubscriptionDto> AssignPlanAsync(Guid tenantId, AssignPlanRequest request, CancellationToken cancellationToken = default);
    Task<SubscriptionDto> ActivateSubscriptionAsync(Guid tenantId, ActivateSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<SubscriptionDto> SuspendSubscriptionAsync(Guid tenantId, SuspendSubscriptionRequest request, CancellationToken cancellationToken = default);
    Task<SubscriptionDto> ExtendSubscriptionAsync(Guid tenantId, ExtendSubscriptionRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FeatureOverrideDto>> GetFeatureOverridesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<FeatureOverrideDto> SetFeatureOverrideAsync(Guid tenantId, FeatureKey featureKey, SetFeatureOverrideRequest request, CancellationToken cancellationToken = default);
    Task RemoveFeatureOverrideAsync(Guid tenantId, FeatureKey featureKey, CancellationToken cancellationToken = default);

    Task<ImpersonationResponse> ImpersonateTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(Guid? tenantId = null, string? action = null, int take = 100, CancellationToken cancellationToken = default);
}

public sealed partial class SuperAdminService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    ITokenService tokenService,
    IAuditService auditService) : ISuperAdminService
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex SubdomainRegex();

    public async Task<IReadOnlyList<TenantSummaryDto>> GetTenantsAsync(CancellationToken cancellationToken = default)
    {
        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);

        var subscriptions = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => !s.IsDeleted)
            .ToListAsync(cancellationToken);

        var subByTenant = subscriptions
            .GroupBy(s => s.TenantId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

        return tenants.Select(t =>
        {
            subByTenant.TryGetValue(t.Id, out var sub);
            return new TenantSummaryDto(
                t.Id,
                t.Name,
                t.Subdomain,
                t.ContactEmail,
                t.ContactPhone,
                t.IsActive,
                t.IsDemo,
                sub?.Plan?.Code,
                sub?.Plan?.Name,
                sub?.Status.ToString(),
                sub?.EndDate ?? sub?.TrialEndsAt,
                t.CreatedAt);
        }).ToList();
    }

    public async Task<TenantDetailDto> GetTenantByIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());

        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => !s.IsDeleted && s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var overrides = await db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => !o.IsDeleted && o.TenantId == tenantId)
            .OrderBy(o => o.FeatureKey)
            .Select(o => new FeatureOverrideDto(
                o.Id,
                o.FeatureKey.ToString(),
                o.IsEnabled,
                o.Reason,
                o.GrantedByUserId,
                o.CreatedAt))
            .ToListAsync(cancellationToken);

        var usersCount = await db.Users
            .IgnoreQueryFilters()
            .CountAsync(u => !u.IsDeleted && u.TenantId == tenantId, cancellationToken);

        var branchesCount = await db.Branches
            .IgnoreQueryFilters()
            .CountAsync(b => !b.IsDeleted && b.TenantId == tenantId, cancellationToken);

        SubscriptionDto? subDto = subscription is null
            ? null
            : MapSubscription(subscription, subscription.Plan?.Code ?? string.Empty, subscription.Plan?.Name ?? string.Empty);

        return new TenantDetailDto(
            tenant.Id,
            tenant.Name,
            tenant.Subdomain,
            tenant.ContactEmail,
            tenant.ContactPhone,
            tenant.Address,
            tenant.TimeZone,
            tenant.IsActive,
            tenant.IsDemo,
            subDto,
            overrides,
            usersCount,
            branchesCount,
            tenant.CreatedAt);
    }

    public async Task<TenantDetailDto> CreateTenantAsync(
        CreateTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var subdomain = request.Subdomain.Trim().ToLowerInvariant();
        if (!SubdomainRegex().IsMatch(subdomain))
        {
            throw new DomainValidationException(
                "INVALID_SUBDOMAIN",
                "Subdomain must contain only lowercase letters, numbers, and hyphens.");
        }

        PasswordPolicy.ValidateOrThrow(request.AdminPassword);

        var exists = await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => !t.IsDeleted && t.Subdomain == subdomain, cancellationToken);

        if (exists)
        {
            throw new DomainValidationException(
                "SUBDOMAIN_TAKEN",
                $"Subdomain '{subdomain}' is already in use.",
                409);
        }

        var planCode = string.IsNullOrWhiteSpace(request.PlanCode) ? "Starter" : request.PlanCode.Trim();
        var plan = await db.Plans
            .IgnoreQueryFilters()
            .Include(p => p.Features)
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.Code.ToLower() == planCode.ToLower(), cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Plan), planCode);

        var now = DateTimeOffset.UtcNow;
        var tenant = new Tenant
        {
            Name = request.Name.Trim(),
            Subdomain = subdomain,
            ContactEmail = request.ContactEmail ?? request.AdminEmail.Trim(),
            ContactPhone = request.ContactPhone,
            Address = request.Address,
            TimeZone = "Asia/Colombo",
            IsActive = true
        };

        db.Tenants.Add(tenant);

        using (tenantContext.BeginSystemScope(tenant.Id))
        {
            var mainBranch = new Branch
            {
                TenantId = tenant.Id,
                Name = "Main Branch",
                Code = "MAIN",
                Address = request.Address,
                Phone = request.ContactPhone,
                IsMain = true,
                IsActive = true
            };
            db.Branches.Add(mainBranch);

            var staffRole = new Role
            {
                TenantId = tenant.Id,
                Name = "Staff",
                Description = "Default staff role",
                IsSystemRole = true,
                Permissions = Permissions.DefaultStaffPermissions
                    .Select(k => new RolePermission { PermissionKey = k })
                    .ToList()
            };
            var teacherRole = new Role
            {
                TenantId = tenant.Id,
                Name = "Teacher",
                Description = "Default teacher role",
                IsSystemRole = true,
                Permissions = Permissions.DefaultTeacherPermissions
                    .Select(k => new RolePermission { PermissionKey = k })
                    .ToList()
            };
            db.Roles.AddRange(staffRole, teacherRole);

            var adminUser = new User
            {
                TenantId = tenant.Id,
                Email = request.AdminEmail.Trim().ToLowerInvariant(),
                FullName = request.AdminFullName.Trim(),
                PasswordHash = PasswordHasher.HashPassword(request.AdminPassword),
                RoleType = RoleType.InstituteAdmin,
                BranchId = mainBranch.Id,
                IsActive = true
            };
            db.Users.Add(adminUser);

            var trialEndsAt = now.AddDays(14);
            var subscription = new Subscription
            {
                TenantId = tenant.Id,
                PlanId = plan.Id,
                Status = SubscriptionStatus.Trial,
                StartDate = now,
                TrialEndsAt = trialEndsAt,
                EndDate = trialEndsAt
            };
            db.Subscriptions.Add(subscription);

            foreach (var pf in plan.Features.Where(f => !f.IsDeleted && f.IsIncluded))
            {
                db.TenantFeatureSettings.Add(new TenantFeatureSetting
                {
                    TenantId = tenant.Id,
                    FeatureKey = pf.FeatureKey,
                    IsEnabled = true,
                    UpdatedBy = tenantContext.UserId,
                    UpdatedAt = now
                });
            }

            await auditService.LogAsync(
                "SuperAdmin.TenantCreated",
                nameof(Tenant),
                tenant.Id.ToString(),
                new { tenant.Name, tenant.Subdomain, Plan = plan.Code, AdminEmail = adminUser.Email },
                tenantIdOverride: tenant.Id,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateTenantCache(tenant.Id);
        return await GetTenantByIdAsync(tenant.Id, cancellationToken);
    }

    public async Task<TenantDetailDto> UpdateTenantAsync(
        Guid tenantId,
        UpdateTenantRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());

        var subdomain = request.Subdomain.Trim().ToLowerInvariant();
        if (!SubdomainRegex().IsMatch(subdomain))
        {
            throw new DomainValidationException(
                "INVALID_SUBDOMAIN",
                "Subdomain must contain only lowercase letters, numbers, and hyphens.");
        }

        var duplicate = await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => !t.IsDeleted && t.Id != tenantId && t.Subdomain == subdomain, cancellationToken);

        if (duplicate)
        {
            throw new DomainValidationException(
                "SUBDOMAIN_TAKEN",
                $"Subdomain '{subdomain}' is already in use.",
                409);
        }

        using (tenantContext.BeginSystemScope(tenant.Id))
        {
            tenant.Name = request.Name.Trim();
            tenant.Subdomain = subdomain;
            tenant.ContactEmail = request.ContactEmail;
            tenant.ContactPhone = request.ContactPhone;
            tenant.Address = request.Address;
            tenant.IsActive = request.IsActive;

            await auditService.LogAsync(
                "SuperAdmin.TenantUpdated",
                nameof(Tenant),
                tenant.Id.ToString(),
                new { tenant.Name, tenant.Subdomain, tenant.IsActive },
                tenantIdOverride: tenant.Id,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetTenantByIdAsync(tenantId, cancellationToken);
    }

    public async Task DeleteTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());

        using (tenantContext.BeginSystemScope(tenant.Id))
        {
            db.Tenants.Remove(tenant);

            await auditService.LogAsync(
                "SuperAdmin.TenantDeleted",
                nameof(Tenant),
                tenant.Id.ToString(),
                new { tenant.Name, tenant.Subdomain },
                tenantIdOverride: tenant.Id,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<PlanDto>> GetPlansAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var query = db.Plans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(p => p.Features)
            .Include(p => p.Limits)
            .Where(p => !p.IsDeleted);

        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        var plans = await query.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        return plans.Select(MapPlan).ToList();
    }

    public async Task<PlanDto> GetPlanByIdAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await db.Plans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(p => p.Features)
            .Include(p => p.Limits)
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == planId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Plan), planId.ToString());

        return MapPlan(plan);
    }

    public async Task<PlanDto> CreatePlanAsync(CreatePlanRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Code.Trim();
        var duplicate = await db.Plans
            .IgnoreQueryFilters()
            .AnyAsync(p => !p.IsDeleted && p.Code.ToLower() == code.ToLower(), cancellationToken);

        if (duplicate)
        {
            throw new DomainValidationException("PLAN_CODE_EXISTS", $"Plan with code '{code}' already exists.", 409);
        }

        var plan = new Plan
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description,
            PriceMonthly = request.PriceMonthly,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "LKR" : request.Currency.Trim(),
            SortOrder = request.SortOrder,
            IsActive = true,
            Features = request.Features
                .Distinct()
                .Select(fk => new PlanFeature { FeatureKey = fk, IsIncluded = true })
                .ToList(),
            Limits = request.Limits
                .GroupBy(l => l.LimitKey)
                .Select(g => new PlanLimit { LimitKey = g.Key, MaxValue = g.Last().MaxValue })
                .ToList()
        };

        using (tenantContext.BeginSystemScope())
        {
            db.Plans.Add(plan);

            await auditService.LogAsync(
                "SuperAdmin.PlanCreated",
                nameof(Plan),
                plan.Id.ToString(),
                new { plan.Code, plan.Name, plan.PriceMonthly },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateAllCache();
        return MapPlan(plan);
    }

    public async Task<PlanDto> UpdatePlanAsync(
        Guid planId,
        UpdatePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var plan = await db.Plans
            .IgnoreQueryFilters()
            .Include(p => p.Features)
            .Include(p => p.Limits)
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == planId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Plan), planId.ToString());

        using (tenantContext.BeginSystemScope())
        {
            plan.Name = request.Name.Trim();
            plan.Description = request.Description;
            plan.PriceMonthly = request.PriceMonthly;
            plan.IsActive = request.IsActive;
            plan.SortOrder = request.SortOrder;

            foreach (var existingFeature in plan.Features.Where(f => !f.IsDeleted))
            {
                existingFeature.IsDeleted = true;
            }

            foreach (var fk in request.Features.Distinct())
            {
                db.PlanFeatures.Add(new PlanFeature
                {
                    PlanId = plan.Id,
                    FeatureKey = fk,
                    IsIncluded = true
                });
            }

            foreach (var existingLimit in plan.Limits.Where(l => !l.IsDeleted))
            {
                existingLimit.IsDeleted = true;
            }

            foreach (var grp in request.Limits.GroupBy(l => l.LimitKey))
            {
                db.PlanLimits.Add(new PlanLimit
                {
                    PlanId = plan.Id,
                    LimitKey = grp.Key,
                    MaxValue = grp.Last().MaxValue
                });
            }

            await auditService.LogAsync(
                "SuperAdmin.PlanUpdated",
                nameof(Plan),
                plan.Id.ToString(),
                new { plan.Code, plan.Name, plan.PriceMonthly, plan.IsActive },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateAllCache();
        return await GetPlanByIdAsync(planId, cancellationToken);
    }

    public async Task DeletePlanAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        var plan = await db.Plans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == planId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Plan), planId.ToString());

        var inUse = await db.Subscriptions
            .IgnoreQueryFilters()
            .AnyAsync(s => !s.IsDeleted && s.PlanId == planId, cancellationToken);

        if (inUse)
        {
            throw new DomainValidationException("PLAN_IN_USE", "Cannot delete a plan that has tenant subscriptions.", 409);
        }

        using (tenantContext.BeginSystemScope())
        {
            db.Plans.Remove(plan);

            await auditService.LogAsync(
                "SuperAdmin.PlanDeleted",
                nameof(Plan),
                plan.Id.ToString(),
                new { plan.Code },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateAllCache();
    }

    public async Task<SubscriptionDto> AssignPlanAsync(
        Guid tenantId,
        AssignPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetOrCreateSubscriptionEntityAsync(tenantId, cancellationToken);

        Plan? plan = null;
        if (request.PlanId.HasValue)
        {
            plan = await db.Plans
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => !p.IsDeleted && p.Id == request.PlanId.Value, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.PlanCode))
        {
            var code = request.PlanCode.Trim().ToLowerInvariant();
            plan = await db.Plans
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => !p.IsDeleted && p.Code.ToLower() == code, cancellationToken);
        }

        if (plan is null)
        {
            throw new EntityNotFoundException(nameof(Plan), request.PlanId?.ToString() ?? request.PlanCode);
        }

        using (tenantContext.BeginSystemScope(tenantId))
        {
            subscription.PlanId = plan.Id;
            if (request.ExtraStudentsAllowed.HasValue)
            {
                subscription.ExtraStudentsAllowed = request.ExtraStudentsAllowed.Value;
            }

            await auditService.LogAsync(
                "SuperAdmin.PlanAssigned",
                nameof(Subscription),
                subscription.Id.ToString(),
                new { PlanCode = plan.Code, subscription.ExtraStudentsAllowed },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateTenantCache(tenantId);
        return MapSubscription(subscription, plan.Code, plan.Name);
    }

    public async Task<SubscriptionDto> ActivateSubscriptionAsync(
        Guid tenantId,
        ActivateSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetOrCreateSubscriptionEntityAsync(tenantId, cancellationToken);
        var plan = await db.Plans
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == subscription.PlanId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var endDate = request.EndDate ?? now.AddMonths(Math.Max(1, request.DurationMonths));

        using (tenantContext.BeginSystemScope(tenantId))
        {
            subscription.Status = SubscriptionStatus.Active;
            subscription.EndDate = endDate;
            subscription.SuspendedReason = null;
            subscription.Notes = request.Notes ?? subscription.Notes;

            await auditService.LogAsync(
                "SuperAdmin.SubscriptionActivated",
                nameof(Subscription),
                subscription.Id.ToString(),
                new { subscription.Status, subscription.EndDate },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateTenantCache(tenantId);
        return MapSubscription(subscription, plan.Code, plan.Name);
    }

    public async Task<SubscriptionDto> SuspendSubscriptionAsync(
        Guid tenantId,
        SuspendSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetOrCreateSubscriptionEntityAsync(tenantId, cancellationToken);
        var plan = await db.Plans
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == subscription.PlanId, cancellationToken);

        using (tenantContext.BeginSystemScope(tenantId))
        {
            subscription.Status = SubscriptionStatus.Suspended;
            subscription.SuspendedReason = request.Reason;

            await auditService.LogAsync(
                "SuperAdmin.SubscriptionSuspended",
                nameof(Subscription),
                subscription.Id.ToString(),
                new { request.Reason },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateTenantCache(tenantId);
        return MapSubscription(subscription, plan.Code, plan.Name);
    }

    public async Task<SubscriptionDto> ExtendSubscriptionAsync(
        Guid tenantId,
        ExtendSubscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var subscription = await GetOrCreateSubscriptionEntityAsync(tenantId, cancellationToken);
        var plan = await db.Plans
            .IgnoreQueryFilters()
            .FirstAsync(p => p.Id == subscription.PlanId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var baseDate = subscription.EndDate.HasValue && subscription.EndDate.Value > now
            ? subscription.EndDate.Value
            : now;
        var newEnd = request.NewEndDate ?? baseDate.AddDays(request.DaysToAdd);

        using (tenantContext.BeginSystemScope(tenantId))
        {
            subscription.EndDate = newEnd;
            if (subscription.Status == SubscriptionStatus.Trial)
            {
                subscription.TrialEndsAt = newEnd;
            }
            else if (subscription.Status == SubscriptionStatus.Expired && newEnd > now)
            {
                subscription.Status = SubscriptionStatus.Active;
            }

            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                subscription.Notes = request.Notes;
            }

            await auditService.LogAsync(
                "SuperAdmin.SubscriptionExtended",
                nameof(Subscription),
                subscription.Id.ToString(),
                new { NewEndDate = newEnd, subscription.Status },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateTenantCache(tenantId);
        return MapSubscription(subscription, plan.Code, plan.Name);
    }

    public async Task<IReadOnlyList<FeatureOverrideDto>> GetFeatureOverridesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        return await db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => !o.IsDeleted && o.TenantId == tenantId)
            .OrderBy(o => o.FeatureKey)
            .Select(o => new FeatureOverrideDto(
                o.Id,
                o.FeatureKey.ToString(),
                o.IsEnabled,
                o.Reason,
                o.GrantedByUserId,
                o.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<FeatureOverrideDto> SetFeatureOverrideAsync(
        Guid tenantId,
        FeatureKey featureKey,
        SetFeatureOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantExists = await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);

        if (!tenantExists)
        {
            throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());
        }

        TenantFeatureOverride? overrideEntity;
        using (tenantContext.BeginSystemScope(tenantId))
        {
            overrideEntity = await db.TenantFeatureOverrides
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    o => !o.IsDeleted && o.TenantId == tenantId && o.FeatureKey == featureKey,
                    cancellationToken);

            if (overrideEntity is null)
            {
                overrideEntity = new TenantFeatureOverride
                {
                    TenantId = tenantId,
                    FeatureKey = featureKey,
                    IsEnabled = request.IsEnabled,
                    Reason = request.Reason,
                    GrantedByUserId = tenantContext.UserId
                };
                db.TenantFeatureOverrides.Add(overrideEntity);
            }
            else
            {
                overrideEntity.IsEnabled = request.IsEnabled;
                overrideEntity.Reason = request.Reason;
                overrideEntity.GrantedByUserId = tenantContext.UserId;
            }

            if (request.IsEnabled)
            {
                var setting = await db.TenantFeatureSettings
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        s => !s.IsDeleted && s.TenantId == tenantId && s.FeatureKey == featureKey,
                        cancellationToken);

                if (setting is null)
                {
                    db.TenantFeatureSettings.Add(new TenantFeatureSetting
                    {
                        TenantId = tenantId,
                        FeatureKey = featureKey,
                        IsEnabled = true,
                        UpdatedBy = tenantContext.UserId
                    });
                }
            }

            await auditService.LogAsync(
                "SuperAdmin.FeatureOverrideSet",
                nameof(TenantFeatureOverride),
                overrideEntity.Id.ToString(),
                new { FeatureKey = featureKey.ToString(), request.IsEnabled, request.Reason },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateTenantCache(tenantId);

        return new FeatureOverrideDto(
            overrideEntity.Id,
            overrideEntity.FeatureKey.ToString(),
            overrideEntity.IsEnabled,
            overrideEntity.Reason,
            overrideEntity.GrantedByUserId,
            overrideEntity.CreatedAt);
    }

    public async Task RemoveFeatureOverrideAsync(
        Guid tenantId,
        FeatureKey featureKey,
        CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope(tenantId))
        {
            var overrideEntity = await db.TenantFeatureOverrides
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    o => !o.IsDeleted && o.TenantId == tenantId && o.FeatureKey == featureKey,
                    cancellationToken)
                ?? throw new EntityNotFoundException(nameof(TenantFeatureOverride), featureKey.ToString());

            db.TenantFeatureOverrides.Remove(overrideEntity);

            await auditService.LogAsync(
                "SuperAdmin.FeatureOverrideRemoved",
                nameof(TenantFeatureOverride),
                overrideEntity.Id.ToString(),
                new { FeatureKey = featureKey.ToString() },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        featureService.InvalidateTenantCache(tenantId);
    }

    public async Task<ImpersonationResponse> ImpersonateTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());

        var actorId = tenantContext.UserId
            ?? throw new DomainValidationException("UNAUTHORIZED", "Super Admin user required.", 401);

        var superAdmin = await db.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => !u.IsDeleted && u.Id == actorId && u.RoleType == RoleType.SuperAdmin, cancellationToken)
            ?? throw new DomainValidationException("FORBIDDEN", "Only Super Admin can impersonate a tenant.", 403);

        var generated = tokenService.GenerateReadOnlyImpersonationToken(superAdmin, tenant);

        using (tenantContext.BeginSystemScope(tenant.Id))
        {
            await auditService.LogAsync(
                "SuperAdmin.ImpersonationStarted",
                nameof(Tenant),
                tenant.Id.ToString(),
                new { SuperAdminId = superAdmin.Id, SuperAdminEmail = superAdmin.Email, TenantSubdomain = tenant.Subdomain, ReadOnly = true },
                tenantIdOverride: tenant.Id,
                actorUserIdOverride: superAdmin.Id,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        return new ImpersonationResponse(
            generated.Token,
            generated.ExpiresAt,
            tenant.Id,
            tenant.Subdomain,
            IsReadOnly: true);
    }

    public async Task<IReadOnlyList<AuditLogDto>> GetAuditLogsAsync(
        Guid? tenantId = null,
        string? action = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => !a.IsDeleted);

        if (tenantId.HasValue)
        {
            query = query.Where(a => a.TenantId == tenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        return await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(a => new AuditLogDto(
                a.Id,
                a.TenantId,
                a.ActorUserId,
                a.ActorEmail,
                a.IsImpersonated,
                a.Action,
                a.EntityName,
                a.EntityId,
                a.DetailsJson,
                a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<Subscription> GetOrCreateSubscriptionEntityAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenantExists = await db.Tenants
            .IgnoreQueryFilters()
            .AnyAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);

        if (!tenantExists)
        {
            throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());
        }

        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (subscription is not null)
        {
            return subscription;
        }

        var starterPlan = await db.Plans
            .IgnoreQueryFilters()
            .OrderBy(p => p.SortOrder)
            .FirstAsync(p => !p.IsDeleted, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        subscription = new Subscription
        {
            TenantId = tenantId,
            PlanId = starterPlan.Id,
            Status = SubscriptionStatus.Trial,
            StartDate = now,
            TrialEndsAt = now.AddDays(14),
            EndDate = now.AddDays(14)
        };

        using (tenantContext.BeginSystemScope(tenantId))
        {
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync(cancellationToken);
        }

        return subscription;
    }

    private static PlanDto MapPlan(Plan plan)
    {
        var features = plan.Features
            .Where(f => !f.IsDeleted && f.IsIncluded)
            .Select(f => f.FeatureKey.ToString())
            .OrderBy(f => f)
            .ToList();

        var limits = plan.Limits
            .Where(l => !l.IsDeleted)
            .GroupBy(l => l.LimitKey)
            .ToDictionary(g => g.Key.ToString(), g => g.Last().MaxValue);

        return new PlanDto(
            plan.Id,
            plan.Code,
            plan.Name,
            plan.Description,
            plan.PriceMonthly,
            plan.Currency,
            plan.IsActive,
            plan.SortOrder,
            features,
            limits);
    }

    private static SubscriptionDto MapSubscription(Subscription s, string planCode, string planName) =>
        new(
            s.Id,
            s.TenantId,
            s.PlanId,
            planCode,
            planName,
            s.Status.ToString(),
            s.StartDate,
            s.EndDate,
            s.TrialEndsAt,
            s.SuspendedReason,
            s.ExtraStudentsAllowed);
}
