using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Fees;
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
    IPaymentGateway paymentGateway,
    IConfiguration configuration)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        using (tenantContext.BeginSystemScope())
        {
            await SeedPermissionsAsync(cancellationToken);
            await SeedPlansAsync(cancellationToken);
            await SeedEducationLevelsAsync(cancellationToken);
            await SeedSuperAdminAsync(cancellationToken);
            await SeedDemoTenantAsync(cancellationToken);
            await EnsureEducationLevelSelectionsAsync(cancellationToken);
            await EnsureDemoAcademicsAsync(cancellationToken);
            await EnsureDefaultRolePermissionsAsync(cancellationToken);
            await EnsureEnterpriseRemoveBrandingAsync(cancellationToken);
            await EnsureParentPortalSubFeaturesAsync(cancellationToken);
            await EnsurePlatformBrandingAsync(cancellationToken);
            await EnsureDemoNotificationsAsync(cancellationToken);
            await EnsureDemoFeesAsync(cancellationToken);
            await EnsureDemoHistoryAsync(cancellationToken);
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
            FeatureKey.ParentCanPayOnline,
            FeatureKey.ParentCanSeeMarks,
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

    private async Task SeedEducationLevelsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.EducationLevels
            .IgnoreQueryFilters()
            .Where(level => !level.IsDeleted)
            .Select(level => level.Code)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in EducationLevelCatalog.All)
        {
            if (!existingSet.Contains(item.Code))
            {
                db.EducationLevels.Add(new EducationLevel
                {
                    Code = item.Code,
                    Name = item.Name,
                    Description = item.Description,
                    SortOrder = item.SortOrder
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureEducationLevelSelectionsAsync(CancellationToken cancellationToken)
    {
        var tenants = await db.Tenants
            .IgnoreQueryFilters()
            .Where(tenant => !tenant.IsDeleted)
            .Select(tenant => tenant.Id)
            .ToListAsync(cancellationToken);

        foreach (var tenantId in tenants)
        {
            var planId = await db.Subscriptions
                .IgnoreQueryFilters()
                .Where(subscription => !subscription.IsDeleted && subscription.TenantId == tenantId)
                .OrderByDescending(subscription => subscription.CreatedAt)
                .Select(subscription => (Guid?)subscription.PlanId)
                .FirstOrDefaultAsync(cancellationToken);

            int? maxEnabled = null;
            if (planId.HasValue)
            {
                maxEnabled = await db.PlanLimits
                    .IgnoreQueryFilters()
                    .Where(limit => !limit.IsDeleted && limit.PlanId == planId.Value && limit.LimitKey == LimitKey.EducationLevels)
                    .Select(limit => limit.MaxValue)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            await EducationLevelProvisioning.EnsureSelectionsAsync(db, tenantId, maxEnabled, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDemoAcademicsAsync(CancellationToken cancellationToken)
    {
        var demo = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(tenant => !tenant.IsDeleted && tenant.Subdomain == "demo", cancellationToken);
        if (demo is null)
        {
            return;
        }

        var hasBatch = await db.AcademicBatches
            .IgnoreQueryFilters()
            .AnyAsync(batch => !batch.IsDeleted && batch.TenantId == demo.Id, cancellationToken);
        if (hasBatch)
        {
            return;
        }

        var level = await db.EducationLevels
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Code == "OL", cancellationToken);
        var branch = await db.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.TenantId == demo.Id && item.IsMain, cancellationToken);
        if (level is null || branch is null)
        {
            return;
        }

        var teacher = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => !user.IsDeleted && user.TenantId == demo.Id && user.Email == "teacher@demo.eduflow.lk", cancellationToken);
        if (teacher is null)
        {
            teacher = new User
            {
                TenantId = demo.Id,
                Email = "teacher@demo.eduflow.lk",
                FullName = "Amali Fernando",
                Phone = "+94 77 555 0142",
                PasswordHash = PasswordHasher.HashPassword(configuration["Seed:DemoTeacher:Password"] ?? "DemoTeacher@123!"),
                RoleType = RoleType.Teacher,
                BranchId = branch.Id,
                IsActive = true
            };
            db.Users.Add(teacher);
        }

        var group = new BatchGroup
        {
            TenantId = demo.Id,
            Name = "Colombo weekday",
            Code = "CMB-WD",
            IsActive = true
        };
        var batch = new AcademicBatch
        {
            TenantId = demo.Id,
            Name = "2026 O/L Combined Maths",
            Code = "OL2026",
            EducationLevelId = level.Id,
            BranchId = branch.Id,
            BatchGroupId = group.Id,
            AcademicYear = "2026",
            IsActive = true
        };
        var maths = new Subject
        {
            TenantId = demo.Id,
            Name = "Combined Mathematics",
            Code = "MATH",
            EducationLevelId = level.Id,
            IsActive = true
        };
        var science = new Subject
        {
            TenantId = demo.Id,
            Name = "Science",
            Code = "SCI",
            EducationLevelId = level.Id,
            IsActive = true
        };
        db.AddRange(group, batch, maths, science);
        await db.SaveChangesAsync(cancellationToken);

        db.ClassSchedules.Add(new ClassSchedule
        {
            TenantId = demo.Id,
            Name = "Combined Mathematics · 2026 O/L",
            BatchId = batch.Id,
            SubjectId = maths.Id,
            TeacherUserId = teacher.Id,
            Weekday = 1,
            StartMinutes = 16 * 60,
            EndMinutes = 18 * 60,
            Room = "Hall 2",
            IsActive = true
        });

        var nimal = new Student
        {
            TenantId = demo.Id,
            StudentCode = "S000001",
            FullName = "Nimal Perera",
            GuardianName = "Sunil Perera",
            GuardianPhone = "0771234567",
            Address = "12 Temple Road, Colombo 03",
            City = "Colombo",
            EducationLevelId = level.Id,
            BranchId = branch.Id,
            Gender = StudentGender.Male,
            Status = StudentStatus.Active,
            AdmissionDate = new DateOnly(2026, 1, 6)
        };
        var amaya = new Student
        {
            TenantId = demo.Id,
            StudentCode = "S000002",
            FullName = "Amaya Silva",
            GuardianName = "Kumari Silva",
            GuardianPhone = "0719876543",
            Address = "48 Galle Road, Dehiwala",
            City = "Dehiwala",
            EducationLevelId = level.Id,
            BranchId = branch.Id,
            Gender = StudentGender.Female,
            Status = StudentStatus.Active,
            AdmissionDate = new DateOnly(2026, 1, 6)
        };
        db.Students.AddRange(nimal, amaya);
        await db.SaveChangesAsync(cancellationToken);

        db.StudentEnrollments.AddRange(
            new StudentEnrollment
            {
                TenantId = demo.Id,
                StudentId = nimal.Id,
                BatchId = batch.Id,
                EnrolledOn = nimal.AdmissionDate,
                IsActive = true
            },
            new StudentEnrollment
            {
                TenantId = demo.Id,
                StudentId = amaya.Id,
                BatchId = batch.Id,
                EnrolledOn = amaya.AdmissionDate,
                IsActive = true
            });
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDefaultRolePermissionsAsync(CancellationToken cancellationToken)
    {
        var roles = await db.Roles
            .IgnoreQueryFilters()
            .Include(role => role.Permissions)
            .Where(role => !role.IsDeleted && role.IsSystemRole && (role.Name == "Staff" || role.Name == "Teacher"))
            .ToListAsync(cancellationToken);

        foreach (var role in roles)
        {
            var expected = role.Name == "Teacher"
                ? Permissions.DefaultTeacherPermissions
                : Permissions.DefaultStaffPermissions;
            var have = role.Permissions.Where(permission => !permission.IsDeleted).Select(permission => permission.PermissionKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var key in expected)
            {
                if (!have.Contains(key))
                {
                    db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = key });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureParentPortalSubFeaturesAsync(CancellationToken cancellationToken)
    {
        var keys = new[] { FeatureKey.ParentPortal, FeatureKey.ParentCanPayOnline, FeatureKey.ParentCanSeeMarks };
        var planIds = await db.Plans
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var planId in planIds)
        {
            foreach (var key in keys)
            {
                var exists = await db.PlanFeatures
                    .IgnoreQueryFilters()
                    .AnyAsync(f => !f.IsDeleted && f.PlanId == planId && f.FeatureKey == key, cancellationToken);
                if (!exists)
                {
                    db.PlanFeatures.Add(new PlanFeature
                    {
                        PlanId = planId,
                        FeatureKey = key,
                        IsIncluded = true
                    });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
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

    private async Task EnsureDemoNotificationsAsync(CancellationToken cancellationToken)
    {
        var demo = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Subdomain == "demo", cancellationToken);

        if (demo is null)
        {
            return;
        }

        var hasConfig = await db.TenantNotificationConfigs
            .IgnoreQueryFilters()
            .AnyAsync(c => !c.IsDeleted && c.TenantId == demo.Id, cancellationToken);

        if (!hasConfig)
        {
            db.TenantNotificationConfigs.Add(new TenantNotificationConfig
            {
                TenantId = demo.Id,
                QuietHoursEnabled = true,
                QuietHoursStartMinutes = 21 * 60,
                QuietHoursEndMinutes = 6 * 60,
                FeeReminderDaysBefore = 3,
                FeeReminderOnDueDate = true,
                FeeReminderDaysAfterOverdue = 3,
                ExamReminderDaysBefore = 1,
                ExamReminderHoursBefore = 0,
                SmsOverQuotaPolicy = SmsOverQuotaPolicy.AllowAndRecordExtra,
                DefaultLanguage = "en"
            });
        }

        var existingTypes = await db.TenantNotificationTypeSettings
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.TenantId == demo.Id)
            .Select(s => s.MessageType)
            .ToListAsync(cancellationToken);

        var existingSet = existingTypes.ToHashSet();
        foreach (var def in NotificationCatalog.All)
        {
            if (!existingSet.Contains(def.MessageType))
            {
                db.TenantNotificationTypeSettings.Add(new TenantNotificationTypeSetting
                {
                    TenantId = demo.Id,
                    MessageType = def.MessageType,
                    IsEnabled = true,
                    SmsEnabled = def.DefaultSmsEnabled,
                    EmailEnabled = def.DefaultEmailEnabled,
                    WhatsAppEnabled = def.DefaultWhatsAppEnabled
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureDemoFeesAsync(CancellationToken cancellationToken)
    {
        var demo = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Subdomain == "demo", cancellationToken);

        if (demo is null)
        {
            return;
        }

        var hasStructure = await db.FeeStructures
            .IgnoreQueryFilters()
            .AnyAsync(f => !f.IsDeleted && f.TenantId == demo.Id, cancellationToken);

        if (hasStructure)
        {
            return;
        }

        var batch = await db.AcademicBatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.TenantId == demo.Id, cancellationToken);

        if (batch is null)
        {
            return;
        }

        var students = await db.Students
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.TenantId == demo.Id)
            .OrderBy(s => s.StudentCode)
            .ToListAsync(cancellationToken);

        var structure = new FeeStructure
        {
            TenantId = demo.Id,
            Name = "2026 O/L Monthly Tuition Fee",
            Frequency = FeeFrequency.Monthly,
            Amount = 3500m,
            Currency = "LKR",
            EducationLevelId = batch.EducationLevelId,
            BatchId = batch.Id,
            DueDayOfMonth = 10,
            Description = "Standard monthly tuition fee for 2026 O/L Combined Maths batch.",
            IsActive = true
        };
        db.FeeStructures.Add(structure);

        var hasOnlineOverride = await db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .AnyAsync(o => !o.IsDeleted && o.TenantId == demo.Id && o.FeatureKey == FeatureKey.OnlinePayments, cancellationToken);
        if (!hasOnlineOverride)
        {
            db.TenantFeatureOverrides.Add(new TenantFeatureOverride
            {
                TenantId = demo.Id,
                FeatureKey = FeatureKey.OnlinePayments,
                IsEnabled = true,
                Reason = "Demo PayHere sandbox enabled"
            });
        }

        var hasOnlineSetting = await db.TenantFeatureSettings
            .IgnoreQueryFilters()
            .AnyAsync(s => !s.IsDeleted && s.TenantId == demo.Id && s.FeatureKey == FeatureKey.OnlinePayments, cancellationToken);
        if (!hasOnlineSetting)
        {
            db.TenantFeatureSettings.Add(new TenantFeatureSetting
            {
                TenantId = demo.Id,
                FeatureKey = FeatureKey.OnlinePayments,
                IsEnabled = true
            });
        }

        var hasGateway = await db.TenantPaymentGatewayConfigs
            .IgnoreQueryFilters()
            .AnyAsync(g => !g.IsDeleted && g.TenantId == demo.Id, cancellationToken);
        if (!hasGateway)
        {
            db.TenantPaymentGatewayConfigs.Add(new TenantPaymentGatewayConfig
            {
                TenantId = demo.Id,
                MerchantId = "1211149",
                EncryptedMerchantSecret = paymentGateway.ProtectSecret("SANDBOX_DEMO_SECRET_2026"),
                IsSandbox = true,
                IsEnabled = true
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        if (students.Count >= 2)
        {
            var nimal = students[0];
            var amaya = students[1];
            var today = ColomboClock.Today();
            var period = FeeRules.NormalizeMonthlyPeriod(null, today);

            var concession = new StudentConcession
            {
                TenantId = demo.Id,
                StudentId = amaya.Id,
                FeeStructureId = structure.Id,
                Name = "Merit Scholarship 10%",
                DiscountType = DiscountType.Percentage,
                Value = 10m,
                Reason = "Top term test score",
                IsActive = true
            };
            db.StudentConcessions.Add(concession);
            await db.SaveChangesAsync(cancellationToken);

            var inv1 = new FeeInvoice
            {
                TenantId = demo.Id,
                InvoiceNumber = "INV-000001",
                StudentId = nimal.Id,
                FeeStructureId = structure.Id,
                BatchId = batch.Id,
                EducationLevelId = batch.EducationLevelId,
                BillingPeriod = period,
                Title = $"{structure.Name} ({period})",
                GrossAmount = 3500m,
                DiscountAmount = 0m,
                NetAmount = 3500m,
                PaidAmount = 3500m,
                BalanceAmount = 0m,
                Currency = "LKR",
                IssueDate = today.AddDays(-5),
                DueDate = today.AddDays(5),
                Status = FeeInvoiceStatus.Paid,
                PaidAt = DateTimeOffset.UtcNow.AddDays(-2)
            };

            var inv2 = new FeeInvoice
            {
                TenantId = demo.Id,
                InvoiceNumber = "INV-000002",
                StudentId = amaya.Id,
                FeeStructureId = structure.Id,
                BatchId = batch.Id,
                EducationLevelId = batch.EducationLevelId,
                BillingPeriod = period,
                Title = $"{structure.Name} ({period})",
                GrossAmount = 3500m,
                DiscountAmount = 350m,
                ConcessionId = concession.Id,
                ConcessionDescription = "Merit Scholarship 10% (10%)",
                NetAmount = 3150m,
                PaidAmount = 0m,
                BalanceAmount = 3150m,
                Currency = "LKR",
                IssueDate = today.AddDays(-5),
                DueDate = today.AddDays(5),
                Status = FeeInvoiceStatus.Pending
            };

            db.FeeInvoices.AddRange(inv1, inv2);
            await db.SaveChangesAsync(cancellationToken);

            db.FeePayments.Add(new FeePayment
            {
                TenantId = demo.Id,
                InvoiceId = inv1.Id,
                StudentId = nimal.Id,
                ReceiptSequence = 1,
                ReceiptNumber = "RCP-000001",
                Amount = 3500m,
                RefundedAmount = 0m,
                Currency = "LKR",
                Method = PaymentMethod.Cash,
                Status = PaymentRecordStatus.Completed,
                PaidAt = DateTimeOffset.UtcNow.AddDays(-2),
                Notes = "Cash payment at main office",
                ParentNotified = true,
                AdminNotified = true
            });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task EnsureDemoHistoryAsync(CancellationToken cancellationToken)
    {
        var demo = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.IsDemo && t.Subdomain == "demo", cancellationToken);
        if (demo is null)
        {
            return;
        }

        var hasSessions = await db.ClassSessions
            .IgnoreQueryFilters()
            .AnyAsync(s => !s.IsDeleted && s.TenantId == demo.Id, cancellationToken);
        if (hasSessions)
        {
            return;
        }

        var level = await db.EducationLevels
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.Code == "OL", cancellationToken);
        var branch = await db.Branches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.TenantId == demo.Id && item.IsMain, cancellationToken);
        var batch = await db.AcademicBatches
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.TenantId == demo.Id, cancellationToken);
        var schedule = await db.ClassSchedules
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.TenantId == demo.Id, cancellationToken);
        var structure = await db.FeeStructures
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.TenantId == demo.Id, cancellationToken);
        if (level is null || branch is null || batch is null || schedule is null || structure is null)
        {
            return;
        }

        var extras = new (string Code, string Name, string Guardian, string Phone, string City, StudentGender Gender)[]
        {
            ("S000003", "කසුන් ජයවර්ධන", "නිමල් ජයවර්ධන", "0772001003", "Kandy", StudentGender.Male),
            ("S000004", "නිමාශි පෙරේරා", "චන්ද්‍රා පෙරේරා", "0712001004", "Galle", StudentGender.Female),
            ("S000005", "அருண் குமார்", "குமார் ராஜா", "0772001005", "Jaffna", StudentGender.Male),
            ("S000006", "மீனா ராஜேந்திரன்", "ராஜேந்திரன்", "0762001006", "Batticaloa", StudentGender.Female),
            ("S000007", "Fathima Rizvi", "Mohamed Rizvi", "0752001007", "Colombo", StudentGender.Female),
            ("S000008", "Dinesh Bandara", "Anura Bandara", "0702001008", "Kurunegala", StudentGender.Male)
        };

        var existingCodes = await db.Students
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.TenantId == demo.Id)
            .Select(s => s.StudentCode)
            .ToListAsync(cancellationToken);
        var known = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var extra in extras)
        {
            if (known.Contains(extra.Code))
            {
                continue;
            }

            var student = new Student
            {
                TenantId = demo.Id,
                StudentCode = extra.Code,
                FullName = extra.Name,
                GuardianName = extra.Guardian,
                GuardianPhone = extra.Phone,
                Address = $"12 School Lane, {extra.City}",
                City = extra.City,
                EducationLevelId = level.Id,
                BranchId = branch.Id,
                Gender = extra.Gender,
                Status = StudentStatus.Active,
                AdmissionDate = new DateOnly(2025, 6, 2)
            };
            db.Students.Add(student);
            await db.SaveChangesAsync(cancellationToken);
            db.StudentEnrollments.Add(new StudentEnrollment
            {
                TenantId = demo.Id,
                StudentId = student.Id,
                BatchId = batch.Id,
                EnrolledOn = student.AdmissionDate,
                IsActive = true
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var students = await db.Students
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.TenantId == demo.Id)
            .OrderBy(s => s.StudentCode)
            .ToListAsync(cancellationToken);

        var today = ColomboClock.Today();
        var mondays = new List<DateOnly>();
        var cursor = today;
        while (mondays.Count < 26)
        {
            cursor = cursor.AddDays(-1);
            if (cursor.DayOfWeek == DayOfWeek.Monday)
            {
                mondays.Add(cursor);
            }
        }

        mondays.Reverse();
        var week = 0;
        foreach (var sessionDate in mondays)
        {
            var session = new ClassSession
            {
                TenantId = demo.Id,
                ClassScheduleId = schedule.Id,
                SessionDate = sessionDate,
                Status = ClassSessionStatus.Stopped,
                StartedAt = new DateTimeOffset(sessionDate.ToDateTime(new TimeOnly(16, 0)), TimeSpan.FromHours(5.5)),
                StoppedAt = new DateTimeOffset(sessionDate.ToDateTime(new TimeOnly(18, 0)), TimeSpan.FromHours(5.5)),
                StartedByUserId = schedule.TeacherUserId,
                StartedNotified = true,
                StoppedNotified = true
            };
            db.ClassSessions.Add(session);
            await db.SaveChangesAsync(cancellationToken);

            for (var index = 0; index < students.Count; index++)
            {
                var roll = (index + week) % 10;
                var status = roll == 0
                    ? AttendanceMarkStatus.Absent
                    : roll == 1 ? AttendanceMarkStatus.Late : AttendanceMarkStatus.Present;
                db.AttendanceMarks.Add(new AttendanceMark
                {
                    TenantId = demo.Id,
                    ClassSessionId = session.Id,
                    StudentId = students[index].Id,
                    Status = status,
                    Source = status == AttendanceMarkStatus.Absent ? AttendanceMarkSource.AutoAbsent : AttendanceMarkSource.Scan,
                    MarkedAt = session.StartedAt!.Value.AddMinutes(5 + index),
                    PresentNotified = status != AttendanceMarkStatus.Absent,
                    AbsentNotified = status == AttendanceMarkStatus.Absent
                });
            }

            week++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var existingInvoices = await db.FeeInvoices
            .IgnoreQueryFilters()
            .Where(i => !i.IsDeleted && i.TenantId == demo.Id)
            .Select(i => new { i.StudentId, i.BillingPeriod })
            .ToListAsync(cancellationToken);
        var invoiceKeys = existingInvoices
            .Select(i => $"{i.StudentId:N}:{i.BillingPeriod}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var receiptSequence = 100;
        var invoiceSequence = 100;
        for (var monthsAgo = 5; monthsAgo >= 0; monthsAgo--)
        {
            var periodDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-monthsAgo);
            var period = $"{periodDate.Year:D4}-{periodDate.Month:D2}";
            foreach (var student in students)
            {
                var key = $"{student.Id:N}:{period}";
                if (!invoiceKeys.Add(key))
                {
                    continue;
                }

                var paid = monthsAgo > 0;
                var invoice = new FeeInvoice
                {
                    TenantId = demo.Id,
                    InvoiceNumber = $"INV-{invoiceSequence:D6}",
                    StudentId = student.Id,
                    FeeStructureId = structure.Id,
                    BatchId = batch.Id,
                    EducationLevelId = level.Id,
                    BillingPeriod = period,
                    Title = $"{structure.Name} ({period})",
                    GrossAmount = structure.Amount,
                    DiscountAmount = 0m,
                    NetAmount = structure.Amount,
                    PaidAmount = paid ? structure.Amount : 0m,
                    BalanceAmount = paid ? 0m : structure.Amount,
                    Currency = "LKR",
                    IssueDate = periodDate,
                    DueDate = new DateOnly(periodDate.Year, periodDate.Month, 10),
                    Status = paid ? FeeInvoiceStatus.Paid : FeeInvoiceStatus.Pending,
                    PaidAt = paid ? new DateTimeOffset(periodDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.FromHours(5.5)) : null
                };
                invoiceSequence++;
                db.FeeInvoices.Add(invoice);
                await db.SaveChangesAsync(cancellationToken);
                if (!paid)
                {
                    continue;
                }

                db.FeePayments.Add(new FeePayment
                {
                    TenantId = demo.Id,
                    InvoiceId = invoice.Id,
                    StudentId = student.Id,
                    ReceiptSequence = receiptSequence,
                    ReceiptNumber = $"RCP-{receiptSequence:D6}",
                    Amount = structure.Amount,
                    Currency = "LKR",
                    Method = PaymentMethod.Cash,
                    Status = PaymentRecordStatus.Completed,
                    PaidAt = invoice.PaidAt ?? DateTimeOffset.UtcNow,
                    Notes = "Demo history payment",
                    ParentNotified = true,
                    AdminNotified = true
                });
                receiptSequence++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var hasExam = await db.Exams
            .IgnoreQueryFilters()
            .AnyAsync(e => !e.IsDeleted && e.TenantId == demo.Id, cancellationToken);
        if (hasExam)
        {
            return;
        }

        var maths = await db.Subjects
            .IgnoreQueryFilters()
            .FirstAsync(s => !s.IsDeleted && s.TenantId == demo.Id && s.Code == "MATH", cancellationToken);
        var scheme = new GradingScheme
        {
            TenantId = demo.Id,
            Name = "Sri Lankan grades",
            IsDefault = true
        };
        db.GradingSchemes.Add(scheme);
        await db.SaveChangesAsync(cancellationToken);
        db.GradeBoundaries.AddRange(
            Boundary(demo.Id, scheme.Id, "A", 75, 100, 1),
            Boundary(demo.Id, scheme.Id, "B", 65, 74, 2),
            Boundary(demo.Id, scheme.Id, "C", 55, 64, 3),
            Boundary(demo.Id, scheme.Id, "S", 40, 54, 4),
            Boundary(demo.Id, scheme.Id, "F", 0, 39, 5));
        var exam = new Exam
        {
            TenantId = demo.Id,
            Name = "First term test",
            Kind = ExamKind.Term,
            BatchId = batch.Id,
            EducationLevelId = level.Id,
            GradingSchemeId = scheme.Id,
            SeriesKey = "2026-T1",
            Status = ExamStatus.Published,
            ScheduledAt = DateTimeOffset.UtcNow.AddMonths(-2),
            Room = "Hall 2",
            IncludeMarksInNotification = false,
            PublishedAt = DateTimeOffset.UtcNow.AddMonths(-2).AddDays(7),
            LockedAt = DateTimeOffset.UtcNow.AddMonths(-2).AddDays(6)
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync(cancellationToken);
        var paper = new ExamSubject
        {
            TenantId = demo.Id,
            ExamId = exam.Id,
            SubjectId = maths.Id,
            MaxMarks = 100,
            ScheduledAt = exam.ScheduledAt,
            Room = "Hall 2",
            SortOrder = 1
        };
        db.ExamSubjects.Add(paper);
        await db.SaveChangesAsync(cancellationToken);

        var rank = 1;
        foreach (var student in students)
        {
            var marks = 90 - (rank * 4);
            var grade = marks >= 75 ? "A" : marks >= 65 ? "B" : marks >= 55 ? "C" : marks >= 40 ? "S" : "F";
            db.ExamMarks.Add(new ExamMark
            {
                TenantId = demo.Id,
                ExamId = exam.Id,
                ExamSubjectId = paper.Id,
                StudentId = student.Id,
                Marks = marks,
                Grade = grade,
                SubjectRank = rank
            });
            db.ExamResults.Add(new ExamResult
            {
                TenantId = demo.Id,
                ExamId = exam.Id,
                StudentId = student.Id,
                TotalMarks = marks,
                MaxMarks = 100,
                Percentage = marks,
                Grade = grade,
                BatchRank = rank,
                LevelRank = rank,
                AttendancePercent = 88,
                TeacherRemarks = "Steady work through the term.",
                MarksReleasedNotified = true
            });
            rank++;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static GradeBoundary Boundary(Guid tenantId, Guid schemeId, string grade, decimal min, decimal max, int sort) =>
        new()
        {
            TenantId = tenantId,
            GradingSchemeId = schemeId,
            Grade = grade,
            MinPercent = min,
            MaxPercent = max,
            SortOrder = sort
        };

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
