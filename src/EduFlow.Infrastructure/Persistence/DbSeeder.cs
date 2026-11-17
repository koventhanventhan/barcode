using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Domain;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EduFlow.Infrastructure.Persistence;

public sealed class DbSeeder(
    AppDbContext db,
    ITenantContext tenantContext,
    IConfiguration configuration)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope())
        {
            await SeedPermissionsAsync(cancellationToken);
            await SeedPlansAsync(cancellationToken);
            await SeedSuperAdminAsync(cancellationToken);
            await SeedDemoTenantAsync(cancellationToken);
            await EnsureEnterpriseRemoveBrandingAsync(cancellationToken);
            await EnsurePlatformBrandingAsync(cancellationToken);
        }
    }

    private async Task SeedPermissionsAsync(CancellationToken cancellationToken)
    {
        var existingKeys = await db.Permissions
            .IgnoreQueryFilters()
            .Select(p => p.Key)
            .ToListAsync(cancellationToken);

        var existingSet = existingKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var def in Permissions.All)
        {
            if (!existingSet.Contains(def.Key))
            {
                db.Permissions.Add(new Permission
                {
                    Key = def.Key,
                    Name = def.Name,
                    Group = def.Group,
                    Description = def.Description
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedPlansAsync(CancellationToken cancellationToken)
    {
        var existingCodes = await db.Plans
            .IgnoreQueryFilters()
            .Select(p => p.Code)
            .ToListAsync(cancellationToken);

        var existingSet = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var baseFeatures = new[]
        {
            FeatureKey.ParentPortal,
            FeatureKey.AttendanceSms,
            FeatureKey.ClassSessionSms,
            FeatureKey.FeeReminderSms,
            FeatureKey.PaymentConfirmationSms,
            FeatureKey.ExamNotifications,
            FeatureKey.EmailNotifications,
            FeatureKey.AutoBackup,
            FeatureKey.BroadcastMessages
        };

        var growthFeatures = baseFeatures
            .Concat([FeatureKey.DataExport, FeatureKey.BarcodeGenerator])
            .ToArray();

        var proFeatures = growthFeatures
            .Concat([
                FeatureKey.PerformanceReports,
                FeatureKey.MultiBranch,
                FeatureKey.BatchGroups,
                FeatureKey.ReportCleanup,
                FeatureKey.PrioritySupport
            ])
            .ToArray();

        var enterpriseFeatures = proFeatures
            .Concat([
                FeatureKey.OnlinePayments,
                FeatureKey.StaffLeave,
                FeatureKey.Timetable,
                FeatureKey.WhatsAppMessaging,
                FeatureKey.OnlineAttendance,
                FeatureKey.RemoveProductBranding
            ])
            .ToArray();

        if (!existingSet.Contains("Starter"))
        {
            db.Plans.Add(BuildPlan(
                code: "Starter",
                name: "Starter",
                description: "For growing tuition classes and small institutes.",
                sortOrder: 1,
                features: baseFeatures,
                limits: new Dictionary<LimitKey, int?>
                {
                    [LimitKey.Students] = 250,
                    [LimitKey.EducationLevels] = 2,
                    [LimitKey.Batches] = 10,
                    [LimitKey.Subjects] = 5,
                    [LimitKey.StaffAccounts] = 3,
                    [LimitKey.AdminAccounts] = 1,
                    [LimitKey.SmsSegmentsPerMonth] = 300,
                    [LimitKey.CloudStorageGb] = 1
                }));
        }

        if (!existingSet.Contains("Growth"))
        {
            db.Plans.Add(BuildPlan(
                code: "Growth",
                name: "Growth",
                description: "For established institutes needing barcode ID cards and exports.",
                sortOrder: 2,
                features: growthFeatures,
                limits: new Dictionary<LimitKey, int?>
                {
                    [LimitKey.Students] = 500,
                    [LimitKey.EducationLevels] = 3,
                    [LimitKey.Batches] = 15,
                    [LimitKey.Subjects] = 10,
                    [LimitKey.StaffAccounts] = 8,
                    [LimitKey.AdminAccounts] = 2,
                    [LimitKey.SmsSegmentsPerMonth] = 600,
                    [LimitKey.CloudStorageGb] = 5
                }));
        }

        if (!existingSet.Contains("Pro"))
        {
            db.Plans.Add(BuildPlan(
                code: "Pro",
                name: "Pro",
                description: "For multi-branch academies with report cards and batch groups.",
                sortOrder: 3,
                features: proFeatures,
                limits: new Dictionary<LimitKey, int?>
                {
                    [LimitKey.Students] = 1500,
                    [LimitKey.EducationLevels] = 4,
                    [LimitKey.Batches] = 25,
                    [LimitKey.Subjects] = 60,
                    [LimitKey.StaffAccounts] = 40,
                    [LimitKey.AdminAccounts] = 3,
                    [LimitKey.SmsSegmentsPerMonth] = 1500,
                    [LimitKey.CloudStorageGb] = 10
                }));
        }

        if (!existingSet.Contains("Enterprise"))
        {
            db.Plans.Add(BuildPlan(
                code: "Enterprise",
                name: "Enterprise",
                description: "For large schools and island-wide institutes with online payments and WhatsApp.",
                sortOrder: 4,
                features: enterpriseFeatures,
                limits: new Dictionary<LimitKey, int?>
                {
                    [LimitKey.Students] = 2500,
                    [LimitKey.EducationLevels] = 5,
                    [LimitKey.Batches] = null,
                    [LimitKey.Subjects] = null,
                    [LimitKey.StaffAccounts] = null,
                    [LimitKey.AdminAccounts] = 5,
                    [LimitKey.SmsSegmentsPerMonth] = 2500,
                    [LimitKey.CloudStorageGb] = 50
                }));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedSuperAdminAsync(CancellationToken cancellationToken)
    {
        var email = configuration["Seed:SuperAdmin:Email"]
            ?? Environment.GetEnvironmentVariable("SUPERADMIN_EMAIL")
            ?? "superadmin@eduflow.lk";

        var password = configuration["Seed:SuperAdmin:Password"]
            ?? Environment.GetEnvironmentVariable("SUPERADMIN_PASSWORD")
            ?? "SuperAdmin@123!";

        var fullName = configuration["Seed:SuperAdmin:FullName"]
            ?? "EduFlow Super Admin";

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var exists = await db.Users
            .IgnoreQueryFilters()
            .AnyAsync(u => !u.IsDeleted && u.RoleType == RoleType.SuperAdmin && u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (!exists)
        {
            db.Users.Add(new User
            {
                TenantId = Guid.Empty,
                Email = normalizedEmail,
                FullName = fullName,
                PasswordHash = PasswordHasher.HashPassword(password),
                RoleType = RoleType.SuperAdmin,
                IsActive = true
            });

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task SeedDemoTenantAsync(CancellationToken cancellationToken)
    {
        const string demoSubdomain = "demo";
        var existingTenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Subdomain == demoSubdomain, cancellationToken);

        if (existingTenant is not null)
        {
            return;
        }

        var proPlan = await db.Plans
            .IgnoreQueryFilters()
            .Include(p => p.Features)
            .FirstAsync(p => !p.IsDeleted && p.Code == "Pro", cancellationToken);

        var demoTenant = new Tenant
        {
            Name = "Royal Vidyalaya Higher Education Institute",
            Subdomain = demoSubdomain,
            ContactEmail = "admin@demo.eduflow.lk",
            ContactPhone = "+94 11 234 5678",
            Address = "No. 42, Galle Road, Colombo 03",
            TimeZone = "Asia/Colombo",
            IsActive = true,
            IsDemo = true
        };

        db.Tenants.Add(demoTenant);
        await db.SaveChangesAsync(cancellationToken);

        using (tenantContext.BeginSystemScope(demoTenant.Id))
        {
            var mainBranch = new Branch
            {
                TenantId = demoTenant.Id,
                Name = "Colombo Main Campus",
                Code = "CMB",
                Address = "No. 42, Galle Road, Colombo 03",
                Phone = "+94 11 234 5678",
                IsMain = true,
                IsActive = true
            };
            db.Branches.Add(mainBranch);

            var staffRole = new Role
            {
                TenantId = demoTenant.Id,
                Name = "Staff",
                Description = "Default staff role",
                IsSystemRole = true,
                Permissions = Permissions.DefaultStaffPermissions
                    .Select(k => new RolePermission { PermissionKey = k })
                    .ToList()
            };
            var teacherRole = new Role
            {
                TenantId = demoTenant.Id,
                Name = "Teacher",
                Description = "Default teacher role",
                IsSystemRole = true,
                Permissions = Permissions.DefaultTeacherPermissions
                    .Select(k => new RolePermission { PermissionKey = k })
                    .ToList()
            };
            db.Roles.AddRange(staffRole, teacherRole);

            var demoAdminPassword = configuration["Seed:DemoAdmin:Password"] ?? "DemoAdmin@123!";
            db.Users.Add(new User
            {
                TenantId = demoTenant.Id,
                Email = "admin@demo.eduflow.lk",
                FullName = "Kasun Perera",
                Phone = "+94 77 123 4567",
                PasswordHash = PasswordHasher.HashPassword(demoAdminPassword),
                RoleType = RoleType.InstituteAdmin,
                BranchId = mainBranch.Id,
                IsActive = true
            });

            var now = DateTimeOffset.UtcNow;
            db.Subscriptions.Add(new Subscription
            {
                TenantId = demoTenant.Id,
                PlanId = proPlan.Id,
                Status = SubscriptionStatus.Active,
                StartDate = now,
                EndDate = now.AddYears(1)
            });

            foreach (var pf in proPlan.Features.Where(f => f.IsIncluded))
            {
                db.TenantFeatureSettings.Add(new TenantFeatureSetting
                {
                    TenantId = demoTenant.Id,
                    FeatureKey = pf.FeatureKey,
                    IsEnabled = true,
                    UpdatedAt = now
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureEnterpriseRemoveBrandingAsync(CancellationToken cancellationToken)
    {
        var planId = await db.Plans
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && p.Code == "Enterprise")
            .Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (planId == Guid.Empty)
        {
            return;
        }

        var exists = await db.PlanFeatures
            .IgnoreQueryFilters()
            .AnyAsync(
                f => !f.IsDeleted && f.PlanId == planId && f.FeatureKey == FeatureKey.RemoveProductBranding,
                cancellationToken);

        if (exists)
        {
            return;
        }

        db.PlanFeatures.Add(new PlanFeature
        {
            PlanId = planId,
            FeatureKey = FeatureKey.RemoveProductBranding,
            IsIncluded = true
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsurePlatformBrandingAsync(CancellationToken cancellationToken)
    {
        var exists = await db.BrandingProfiles
            .IgnoreQueryFilters()
            .AnyAsync(b => !b.IsDeleted && b.TenantId == Guid.Empty, cancellationToken);

        if (exists)
        {
            return;
        }

        db.BrandingProfiles.Add(new BrandingProfile
        {
            TenantId = Guid.Empty,
            InstituteDisplayName = ProductName.Value,
            Tagline = "Student records, attendance, and fees for Sri Lankan institutes."
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static Plan BuildPlan(
        string code,
        string name,
        string description,
        int sortOrder,
        IEnumerable<FeatureKey> features,
        IReadOnlyDictionary<LimitKey, int?> limits)
    {
        return new Plan
        {
            Code = code,
            Name = name,
            Description = description,
            PriceMonthly = null,
            Currency = "LKR",
            IsActive = true,
            SortOrder = sortOrder,
            Features = features
                .Distinct()
                .Select(fk => new PlanFeature { FeatureKey = fk, IsIncluded = true })
                .ToList(),
            Limits = limits
                .Select(kv => new PlanLimit { LimitKey = kv.Key, MaxValue = kv.Value })
                .ToList()
        };
    }
}
