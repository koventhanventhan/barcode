using System.Text.RegularExpressions;
using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Marketing;
using EduFlow.Application.Fees;
using EduFlow.Domain;
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

    Task<IReadOnlyList<PlatformSubscriptionInvoiceDto>> GetPlatformInvoicesAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    Task<PlatformSubscriptionInvoiceDto> CreatePlatformInvoiceAsync(Guid tenantId, CreatePlatformInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<PlatformSubscriptionInvoiceDto> MarkPlatformInvoicePaidAsync(Guid invoiceId, MarkPlatformInvoicePaidRequest request, CancellationToken cancellationToken = default);
    Task<StoredDownload> GetPlatformInvoicePdfAsync(Guid invoiceId, CancellationToken cancellationToken = default);

    Task<SuperAdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PlatformAnnouncementDto>> GetAnnouncementsAsync(bool activeOnly = false, CancellationToken cancellationToken = default);
    Task<PlatformAnnouncementDto> CreateAnnouncementAsync(CreatePlatformAnnouncementRequest request, CancellationToken cancellationToken = default);
    Task<PlatformAnnouncementDto> SetAnnouncementActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);
    Task DeleteAnnouncementAsync(Guid id, CancellationToken cancellationToken = default);
    Task<MaintenanceModeDto> GetMaintenanceModeAsync(CancellationToken cancellationToken = default);
    Task<MaintenanceModeDto> UpdateMaintenanceModeAsync(UpdateMaintenanceModeRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantAddOnDto>> GetAddOnsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantAddOnDto> GrantAddOnAsync(Guid tenantId, GrantTenantAddOnRequest request, CancellationToken cancellationToken = default);
    Task RevokeAddOnAsync(Guid tenantId, Guid addOnId, CancellationToken cancellationToken = default);
}

public sealed partial class SuperAdminService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    ITokenService tokenService,
    IAuditService auditService,
    IFeeDocumentService feeDocuments,
    ITransactionalEmailService transactionalEmail) : ISuperAdminService
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

            var levelCap = await db.PlanLimits
                .IgnoreQueryFilters()
                .Where(limit => !limit.IsDeleted && limit.PlanId == plan.Id && limit.LimitKey == LimitKey.EducationLevels)
                .Select(limit => limit.MaxValue)
                .FirstOrDefaultAsync(cancellationToken);
            await EducationLevelProvisioning.EnsureSelectionsAsync(db, tenant.Id, levelCap, cancellationToken);

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

        PlanPriceRules.EnsureValid(
            request.PriceMonthlyLkr,
            request.PriceYearlyLkr,
            request.SetupFeeLkr,
            request.ExtraSmsPerSegmentLkr,
            request.ExtraStoragePerGbLkr);

        var plan = new Plan
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = request.Description,
            PriceMonthlyLkr = request.PriceMonthlyLkr,
            PriceYearlyLkr = request.PriceYearlyLkr,
            SetupFeeLkr = request.SetupFeeLkr,
            ExtraSmsPerSegmentLkr = request.ExtraSmsPerSegmentLkr,
            ExtraStoragePerGbLkr = request.ExtraStoragePerGbLkr,
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
                new { plan.Code, plan.Name, plan.PriceMonthlyLkr, plan.PriceYearlyLkr },
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

        PlanPriceRules.EnsureValid(
            request.PriceMonthlyLkr,
            request.PriceYearlyLkr,
            request.SetupFeeLkr,
            request.ExtraSmsPerSegmentLkr,
            request.ExtraStoragePerGbLkr);

        using (tenantContext.BeginSystemScope())
        {
            plan.Name = request.Name.Trim();
            plan.Description = request.Description;
            plan.PriceMonthlyLkr = request.PriceMonthlyLkr;
            plan.PriceYearlyLkr = request.PriceYearlyLkr;
            plan.SetupFeeLkr = request.SetupFeeLkr;
            plan.ExtraSmsPerSegmentLkr = request.ExtraSmsPerSegmentLkr;
            plan.ExtraStoragePerGbLkr = request.ExtraStoragePerGbLkr;
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
                new { plan.Code, plan.Name, plan.PriceMonthlyLkr, plan.PriceYearlyLkr, plan.IsActive },
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
            var term = action.Trim();
            query = query.Where(a => a.Action.Contains(term));
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

    public async Task<IReadOnlyList<PlatformSubscriptionInvoiceDto>> GetPlatformInvoicesAsync(
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.PlatformSubscriptionInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => !i.IsDeleted);

        if (tenantId.HasValue)
        {
            query = query.Where(i => i.TenantId == tenantId.Value);
        }

        var invoices = await query
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return await MapPlatformInvoicesAsync(invoices, cancellationToken);
    }

    public async Task<PlatformSubscriptionInvoiceDto> CreatePlatformInvoiceAsync(
        Guid tenantId,
        CreatePlatformInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var amount = FeeRules.RoundMoney(request.Amount);
        if (amount <= 0m)
        {
            throw new DomainValidationException("INVALID_AMOUNT", "Subscription invoice amount must be greater than zero.");
        }

        var periodMonths = Math.Clamp(request.PeriodMonths, 1, 60);
        var daysToExtend = request.DaysToExtend > 0 ? request.DaysToExtend : periodMonths * 30;

        var subscription = await GetOrCreateSubscriptionEntityAsync(tenantId, cancellationToken);
        var plan = await db.Plans
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(p => p.Id == subscription.PlanId, cancellationToken);

        var existingCodes = await db.PlatformSubscriptionInvoices
            .IgnoreQueryFilters()
            .Select(i => i.InvoiceNumber)
            .ToListAsync(cancellationToken);
        var nextSeq = (existingCodes.Count == 0 ? 0 : existingCodes.Max(FeeRules.ParseSequence)) + 1;

        var today = ColomboClock.Today();
        var dueDate = request.DueDate ?? today.AddDays(14);
        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "LKR" : request.Currency.Trim().ToUpperInvariant();

        var invoice = new PlatformSubscriptionInvoice
        {
            TenantId = tenantId,
            InvoiceNumber = FeeRules.FormatPlatformInvoiceNumber(nextSeq),
            SubscriptionId = subscription.Id,
            PlanId = plan.Id,
            PlanName = plan.Name,
            Amount = amount,
            Currency = currency,
            PeriodMonths = periodMonths,
            DaysToExtend = daysToExtend,
            IssueDate = today,
            DueDate = dueDate,
            Status = PlatformInvoiceStatus.Pending,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        using (tenantContext.BeginSystemScope(tenantId))
        {
            db.PlatformSubscriptionInvoices.Add(invoice);
            await auditService.LogAsync(
                "SuperAdmin.PlatformInvoiceCreated",
                nameof(PlatformSubscriptionInvoice),
                invoice.Id.ToString(),
                new { invoice.InvoiceNumber, invoice.PlanName, invoice.Amount, invoice.DaysToExtend },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        await transactionalEmail.SendPlatformInvoiceAsync(
            tenantId,
            invoice.InvoiceNumber,
            invoice.Amount,
            invoice.Currency,
            invoice.DueDate,
            "Issued",
            cancellationToken);

        return (await MapPlatformInvoicesAsync([invoice], cancellationToken)).Single();
    }

    public async Task<PlatformSubscriptionInvoiceDto> MarkPlatformInvoicePaidAsync(
        Guid invoiceId,
        MarkPlatformInvoicePaidRequest request,
        CancellationToken cancellationToken = default)
    {
        var invoice = await db.PlatformSubscriptionInvoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => !i.IsDeleted && i.Id == invoiceId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PlatformSubscriptionInvoice), invoiceId.ToString());

        if (invoice.Status == PlatformInvoiceStatus.Paid)
        {
            return (await MapPlatformInvoicesAsync([invoice], cancellationToken)).Single();
        }

        if (invoice.Status == PlatformInvoiceStatus.Voided)
        {
            throw new DomainValidationException("INVOICE_VOIDED", "Cannot mark a voided platform invoice as paid.");
        }

        var subscription = await GetOrCreateSubscriptionEntityAsync(invoice.TenantId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var baseDate = subscription.EndDate.HasValue && subscription.EndDate.Value > now
            ? subscription.EndDate.Value
            : now;
        var newEndDate = baseDate.AddDays(Math.Max(1, invoice.DaysToExtend));

        using (tenantContext.BeginSystemScope(invoice.TenantId))
        {
            invoice.Status = PlatformInvoiceStatus.Paid;
            invoice.PaidAt = now;
            invoice.PaymentReference = string.IsNullOrWhiteSpace(request.PaymentReference)
                ? $"MANUAL-{now:yyyyMMdd}"
                : request.PaymentReference.Trim();
            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                invoice.Notes = request.Notes.Trim();
            }

            subscription.Status = SubscriptionStatus.Active;
            subscription.EndDate = newEndDate;
            subscription.SuspendedReason = null;

            await auditService.LogAsync(
                "SuperAdmin.PlatformInvoiceMarkedPaid",
                nameof(PlatformSubscriptionInvoice),
                invoice.Id.ToString(),
                new
                {
                    invoice.InvoiceNumber,
                    invoice.Amount,
                    invoice.PaymentReference,
                    NewSubscriptionEndDate = newEndDate
                },
                tenantIdOverride: invoice.TenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        await transactionalEmail.SendPlatformInvoiceAsync(
            invoice.TenantId,
            invoice.InvoiceNumber,
            invoice.Amount,
            invoice.Currency,
            invoice.DueDate,
            "Paid",
            cancellationToken);

        featureService.InvalidateTenantCache(invoice.TenantId);
        return (await MapPlatformInvoicesAsync([invoice], cancellationToken)).Single();
    }

    public async Task<StoredDownload> GetPlatformInvoicePdfAsync(
        Guid invoiceId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await db.PlatformSubscriptionInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => !i.IsDeleted && i.Id == invoiceId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(PlatformSubscriptionInvoice), invoiceId.ToString());

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(t => t.Id == invoice.TenantId, cancellationToken);

        var platformBrand = await db.BrandingProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.TenantId == Guid.Empty, cancellationToken);

        var content = new PlatformInvoicePdfContent(
            PlatformName: platformBrand?.InstituteDisplayName ?? ProductName.Value,
            PrimaryHex: platformBrand?.PrimaryColor ?? BrandingProfile.DefaultPrimaryColor,
            InvoiceNumber: invoice.InvoiceNumber,
            TenantName: tenant.Name,
            TenantSubdomain: tenant.Subdomain,
            TenantEmail: tenant.ContactEmail,
            PlanName: invoice.PlanName,
            PeriodMonths: invoice.PeriodMonths,
            DaysToExtend: invoice.DaysToExtend,
            Amount: invoice.Amount,
            Currency: invoice.Currency,
            IssueDate: invoice.IssueDate,
            DueDate: invoice.DueDate,
            Status: invoice.Status.ToString(),
            PaidAt: invoice.PaidAt,
            PaymentReference: invoice.PaymentReference,
            Notes: invoice.Notes);

        var pdf = feeDocuments.BuildPlatformInvoicePdf(content);
        return new StoredDownload(pdf, "application/pdf", $"{invoice.InvoiceNumber.ToLowerInvariant()}.pdf");
    }

    public async Task<SuperAdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthEnd = monthStart.AddMonths(1);

        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

        var tenantMap = tenants.ToDictionary(t => t.Id);

        var subscriptions = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Plan)
                .ThenInclude(p => p!.Limits)
            .Where(s => !s.IsDeleted)
            .ToListAsync(cancellationToken);

        var subByTenant = subscriptions
            .GroupBy(s => s.TenantId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt).First());

        var activeCount = 0;
        var trialCount = 0;
        var expiredCount = 0;
        var suspendedCount = 0;
        var inactiveCount = 0;
        var mrr = 0m;
        var expiringTrials = new List<ExpiringTrialDto>();

        foreach (var t in tenants)
        {
            if (!t.IsActive)
            {
                inactiveCount++;
            }

            subByTenant.TryGetValue(t.Id, out var sub);
            if (sub is null)
            {
                continue;
            }

            if (sub.Status == SubscriptionStatus.Suspended)
            {
                suspendedCount++;
            }
            else if (sub.IsEffectivelyExpired(now))
            {
                expiredCount++;
            }
            else if (sub.Status == SubscriptionStatus.Trial)
            {
                trialCount++;
                var trialEnd = sub.TrialEndsAt ?? sub.EndDate;
                if (trialEnd.HasValue)
                {
                    var daysRemaining = (int)Math.Ceiling((trialEnd.Value - now).TotalDays);
                    if (daysRemaining <= 14)
                    {
                        expiringTrials.Add(new ExpiringTrialDto(
                            t.Id,
                            t.Name,
                            t.Subdomain,
                            sub.Plan?.Name ?? "Starter",
                            trialEnd,
                            Math.Max(0, daysRemaining),
                            t.ContactEmail));
                    }
                }
            }
            else if (sub.Status == SubscriptionStatus.Active)
            {
                activeCount++;
                mrr += sub.Plan?.PriceMonthlyLkr ?? 0m;
            }
        }

        var paidInvoices = await db.PlatformSubscriptionInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => !i.IsDeleted && i.Status == PlatformInvoiceStatus.Paid && i.PaidAt.HasValue)
            .ToListAsync(cancellationToken);

        var collectedThisMonth = paidInvoices
            .Where(i => i.PaidAt!.Value >= monthStart && i.PaidAt!.Value < monthEnd)
            .Sum(i => i.Amount);

        var smsRows = await db.NotificationOutboxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => !o.IsDeleted
                        && o.Channel == NotificationChannel.Sms
                        && (o.Status == NotificationStatus.Sent || o.Status == NotificationStatus.Delivered))
            .Select(o => new { o.TenantId, o.SentAt, o.CreatedAt, o.SmsSegments, o.ExtraSegments })
            .ToListAsync(cancellationToken);

        var smsThisMonthByTenant = smsRows
            .Where(o =>
            {
                var ts = o.SentAt ?? o.CreatedAt;
                return ts >= monthStart && ts < monthEnd;
            })
            .GroupBy(o => o.TenantId)
            .ToDictionary(
                g => g.Key,
                g => (Used: g.Sum(x => x.SmsSegments), Extra: g.Sum(x => x.ExtraSegments)));

        var smsUsage = tenants.Select(t =>
        {
            subByTenant.TryGetValue(t.Id, out var sub);
            var quota = sub?.Plan?.Limits.FirstOrDefault(l => !l.IsDeleted && l.LimitKey == LimitKey.SmsSegmentsPerMonth)?.MaxValue;
            smsThisMonthByTenant.TryGetValue(t.Id, out var usage);
            var pct = quota is > 0 ? Math.Round(usage.Used * 100.0 / quota.Value, 1) : 0.0;
            return new TenantSmsUsageDto(t.Id, t.Name, t.Subdomain, usage.Used, quota, usage.Extra, pct);
        }).ToList();

        var studentPhotoBytes = await db.Students
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.PhotoBytes > 0)
            .GroupBy(s => s.TenantId)
            .Select(g => new { TenantId = g.Key, Bytes = g.Sum(x => x.PhotoBytes) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Bytes, cancellationToken);

        var paymentBytes = await db.FeePayments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.AttachmentBytes > 0)
            .GroupBy(p => p.TenantId)
            .Select(g => new { TenantId = g.Key, Bytes = g.Sum(x => x.AttachmentBytes) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Bytes, cancellationToken);

        var backupBytes = await db.TenantBackupRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => !b.IsDeleted && b.SizeBytes > 0)
            .GroupBy(b => b.TenantId)
            .Select(g => new { TenantId = g.Key, Bytes = g.Sum(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.TenantId, x => x.Bytes, cancellationToken);

        var storageUsage = tenants.Select(t =>
        {
            subByTenant.TryGetValue(t.Id, out var sub);
            var limitGb = sub?.Plan?.Limits.FirstOrDefault(l => !l.IsDeleted && l.LimitKey == LimitKey.CloudStorageGb)?.MaxValue;
            studentPhotoBytes.TryGetValue(t.Id, out var sb);
            paymentBytes.TryGetValue(t.Id, out var pb);
            backupBytes.TryGetValue(t.Id, out var bb);
            var totalBytes = sb + pb + bb;
            var limitBytes = limitGb.HasValue ? limitGb.Value * 1024L * 1024L * 1024L : 0L;
            var pct = limitBytes > 0 ? Math.Round(totalBytes * 100.0 / limitBytes, 2) : 0.0;
            return new TenantStorageUsageDto(t.Id, t.Name, t.Subdomain, totalBytes, limitGb, pct);
        }).ToList();

        var failedNotifications = await db.NotificationOutboxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => !o.IsDeleted && (o.Status == NotificationStatus.Failed || o.IsDeadLetter))
            .OrderByDescending(o => o.LastAttemptAt ?? o.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken);

        var failedBackups = await db.TenantBackupRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => !b.IsDeleted && b.Status == BackupStatus.Failed)
            .OrderByDescending(b => b.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var recentFailures = new List<FailedJobItemDto>();
        foreach (var fn in failedNotifications.Take(15))
        {
            tenantMap.TryGetValue(fn.TenantId, out var t);
            recentFailures.Add(new FailedJobItemDto(
                fn.Id,
                fn.IsDeadLetter ? "NotificationDeadLetter" : "NotificationOutbox",
                t?.Name,
                $"{fn.Channel}:{fn.Recipient}",
                fn.StatusReason ?? "Delivery failed",
                fn.LastAttemptAt ?? fn.CreatedAt));
        }

        foreach (var fb in failedBackups.Take(10))
        {
            tenantMap.TryGetValue(fb.TenantId, out var t);
            recentFailures.Add(new FailedJobItemDto(
                fb.Id,
                "TenantBackup",
                t?.Name,
                fb.FileName,
                fb.ErrorMessage ?? "Backup failed",
                fb.CreatedAt));
        }

        var recentErrors = recentFailures
            .OrderByDescending(f => f.FailedAt)
            .Take(20)
            .Select(f => new RecentErrorDto(f.Id, f.Category, f.Error, f.TenantName, f.FailedAt))
            .ToList();

        var maintenance = await GetMaintenanceModeAsync(cancellationToken);
        var activeAnnouncements = await GetAnnouncementsAsync(activeOnly: true, cancellationToken);

        return new SuperAdminDashboardDto(
            new TenantsCountSummaryDto(
                tenants.Count,
                activeCount,
                trialCount,
                expiredCount,
                suspendedCount,
                inactiveCount),
            FeeRules.RoundMoney(mrr),
            FeeRules.RoundMoney(collectedThisMonth),
            "LKR",
            expiringTrials.OrderBy(e => e.DaysRemaining).ToList(),
            smsUsage,
            storageUsage,
            new FailedJobsSummaryDto(
                failedNotifications.Count(f => f.IsDeadLetter),
                failedNotifications.Count,
                failedBackups.Count,
                recentFailures.OrderByDescending(f => f.FailedAt).Take(20).ToList()),
            recentErrors,
            maintenance,
            activeAnnouncements);
    }

    public async Task<IReadOnlyList<PlatformAnnouncementDto>> GetAnnouncementsAsync(
        bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var items = await db.PlatformAnnouncements
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => !a.IsDeleted)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);

        if (activeOnly)
        {
            items = items
                .Where(a => a.IsActive && (!a.ExpiresAt.HasValue || a.ExpiresAt.Value > now))
                .ToList();
        }

        return items.Select(MapAnnouncement).ToList();
    }

    public async Task<PlatformAnnouncementDto> CreateAnnouncementAsync(
        CreatePlatformAnnouncementRequest request,
        CancellationToken cancellationToken = default)
    {
        var title = request.Title?.Trim() ?? string.Empty;
        var body = request.Body?.Trim() ?? string.Empty;
        if (title.Length is < 2 or > 200)
        {
            throw new DomainValidationException("INVALID_TITLE", "Announcement title must be between 2 and 200 characters.");
        }

        if (body.Length is < 2 or > 2000)
        {
            throw new DomainValidationException("INVALID_BODY", "Announcement body must be between 2 and 2000 characters.");
        }

        var announcement = new PlatformAnnouncement
        {
            Title = title,
            Body = body,
            Severity = request.Severity,
            IsActive = true,
            ExpiresAt = request.ExpiresAt,
            PublishedByUserId = tenantContext.UserId
        };

        using (tenantContext.BeginSystemScope())
        {
            db.PlatformAnnouncements.Add(announcement);
            await auditService.LogAsync(
                "SuperAdmin.AnnouncementPublished",
                nameof(PlatformAnnouncement),
                announcement.Id.ToString(),
                new { announcement.Title, Severity = announcement.Severity.ToString(), announcement.ExpiresAt },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        return MapAnnouncement(announcement);
    }

    public async Task<PlatformAnnouncementDto> SetAnnouncementActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope())
        {
            var announcement = await db.PlatformAnnouncements
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => !a.IsDeleted && a.Id == id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(PlatformAnnouncement), id.ToString());

            announcement.IsActive = isActive;
            await auditService.LogAsync(
                "SuperAdmin.AnnouncementStatusChanged",
                nameof(PlatformAnnouncement),
                announcement.Id.ToString(),
                new { announcement.Title, announcement.IsActive },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return MapAnnouncement(announcement);
        }
    }

    public async Task DeleteAnnouncementAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope())
        {
            var announcement = await db.PlatformAnnouncements
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => !a.IsDeleted && a.Id == id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(PlatformAnnouncement), id.ToString());

            db.PlatformAnnouncements.Remove(announcement);
            await auditService.LogAsync(
                "SuperAdmin.AnnouncementDeleted",
                nameof(PlatformAnnouncement),
                announcement.Id.ToString(),
                new { announcement.Title },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<MaintenanceModeDto> GetMaintenanceModeAsync(CancellationToken cancellationToken = default)
    {
        var setting = await db.PlatformSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", cancellationToken);

        return new MaintenanceModeDto(
            setting?.IsMaintenanceMode ?? false,
            setting?.MaintenanceMessage,
            setting?.UpdatedAt ?? setting?.CreatedAt);
    }

    public async Task<MaintenanceModeDto> UpdateMaintenanceModeAsync(
        UpdateMaintenanceModeRequest request,
        CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope())
        {
            var setting = await db.PlatformSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", cancellationToken);

            if (setting is null)
            {
                setting = new PlatformSetting
                {
                    Key = "default",
                    PlatformBackupRetentionDays = 14
                };
                db.PlatformSettings.Add(setting);
            }

            setting.IsMaintenanceMode = request.IsMaintenanceMode;
            setting.MaintenanceMessage = string.IsNullOrWhiteSpace(request.Message)
                ? null
                : request.Message.Trim()[..Math.Min(500, request.Message.Trim().Length)];
            setting.UpdatedByUserId = tenantContext.UserId;

            await auditService.LogAsync(
                "SuperAdmin.MaintenanceModeUpdated",
                nameof(PlatformSetting),
                setting.Id.ToString(),
                new { setting.IsMaintenanceMode, setting.MaintenanceMessage },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            return new MaintenanceModeDto(
                setting.IsMaintenanceMode,
                setting.MaintenanceMessage,
                setting.UpdatedAt ?? setting.CreatedAt);
        }
    }

    private static PlatformAnnouncementDto MapAnnouncement(PlatformAnnouncement a) =>
        new(a.Id, a.Title, a.Body, a.Severity.ToString(), a.IsActive, a.ExpiresAt, a.CreatedAt);

    private async Task<IReadOnlyList<PlatformSubscriptionInvoiceDto>> MapPlatformInvoicesAsync(
        IReadOnlyList<PlatformSubscriptionInvoice> invoices,
        CancellationToken cancellationToken)
    {
        if (invoices.Count == 0)
        {
            return [];
        }

        var tenantIds = invoices.Select(i => i.TenantId).Distinct().ToList();
        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => tenantIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);

        return invoices.Select(i =>
        {
            tenants.TryGetValue(i.TenantId, out var tenant);
            return new PlatformSubscriptionInvoiceDto(
                i.Id,
                i.TenantId,
                tenant?.Name ?? "Institute",
                tenant?.Subdomain ?? string.Empty,
                i.InvoiceNumber,
                i.PlanId,
                i.PlanName,
                i.Amount,
                i.Currency,
                i.PeriodMonths,
                i.DaysToExtend,
                i.IssueDate,
                i.DueDate,
                i.Status.ToString(),
                i.PaidAt,
                i.PaymentReference,
                i.Notes,
                i.CreatedAt);
        }).ToList();
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
            plan.PriceMonthlyLkr,
            plan.Currency,
            plan.IsActive,
            plan.SortOrder,
            features,
            limits,
            plan.PriceYearlyLkr,
            plan.SetupFeeLkr,
            plan.ExtraSmsPerSegmentLkr,
            plan.ExtraStoragePerGbLkr);
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
