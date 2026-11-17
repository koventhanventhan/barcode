using System.Linq.Expressions;
using System.Reflection;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Common;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduFlow.Infrastructure.Persistence;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext,
    IBranchScope? branchScope = null) : DbContext(options), IAppDbContext
{
    private readonly IBranchScope branches = branchScope ?? new BranchScope();
    public Guid? CurrentTenantId => tenantContext.TenantId;
    private bool BranchOpen => tenantContext.IsSystemScope || branches.Unrestricted;
    private Guid? ActiveBranchId => tenantContext.IsSystemScope ? null : branches.SelectedBranchId;
    private Guid MainBranchId => branches.MainBranchId;
    private List<Guid> AllowedBranchIds => branches.AllowedBranchIds;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<StaffBranchAssignment> StaffBranchAssignments => Set<StaffBranchAssignment>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<PlanLimit> PlanLimits => Set<PlanLimit>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<TenantFeatureSetting> TenantFeatureSettings => Set<TenantFeatureSetting>();
    public DbSet<TenantFeatureOverride> TenantFeatureOverrides => Set<TenantFeatureOverride>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<BrandingProfile> BrandingProfiles => Set<BrandingProfile>();
    public DbSet<EducationLevel> EducationLevels => Set<EducationLevel>();
    public DbSet<TenantEducationLevel> TenantEducationLevels => Set<TenantEducationLevel>();
    public DbSet<BatchGroup> BatchGroups => Set<BatchGroup>();
    public DbSet<AcademicBatch> AcademicBatches => Set<AcademicBatch>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<ClassSchedule> ClassSchedules => Set<ClassSchedule>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<TenantNotificationConfig> TenantNotificationConfigs => Set<TenantNotificationConfig>();
    public DbSet<TenantNotificationTypeSetting> TenantNotificationTypeSettings => Set<TenantNotificationTypeSetting>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationOutbox> NotificationOutboxes => Set<NotificationOutbox>();
    public DbSet<AttendancePolicy> AttendancePolicies => Set<AttendancePolicy>();
    public DbSet<ClassSession> ClassSessions => Set<ClassSession>();
    public DbSet<AttendanceMark> AttendanceMarks => Set<AttendanceMark>();
    public DbSet<FeeStructure> FeeStructures => Set<FeeStructure>();
    public DbSet<StudentConcession> StudentConcessions => Set<StudentConcession>();
    public DbSet<FeeInvoice> FeeInvoices => Set<FeeInvoice>();
    public DbSet<FeePayment> FeePayments => Set<FeePayment>();
    public DbSet<TenantPaymentGatewayConfig> TenantPaymentGatewayConfigs => Set<TenantPaymentGatewayConfig>();
    public DbSet<PayHereCheckoutSession> PayHereCheckoutSessions => Set<PayHereCheckoutSession>();
    public DbSet<PlatformSubscriptionInvoice> PlatformSubscriptionInvoices => Set<PlatformSubscriptionInvoice>();
    public DbSet<GradingScheme> GradingSchemes => Set<GradingScheme>();
    public DbSet<GradeBoundary> GradeBoundaries => Set<GradeBoundary>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamSubject> ExamSubjects => Set<ExamSubject>();
    public DbSet<ExamMark> ExamMarks => Set<ExamMark>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();
    public DbSet<ParentStudentLink> ParentStudentLinks => Set<ParentStudentLink>();
    public DbSet<TenantBackupPolicy> TenantBackupPolicies => Set<TenantBackupPolicy>();
    public DbSet<TenantBackupRecord> TenantBackupRecords => Set<TenantBackupRecord>();
    public DbSet<TenantCleanupPolicy> TenantCleanupPolicies => Set<TenantCleanupPolicy>();
    public DbSet<PlatformBackupRecord> PlatformBackupRecords => Set<PlatformBackupRecord>();
    public DbSet<PlatformAnnouncement> PlatformAnnouncements => Set<PlatformAnnouncement>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<StaffAttendanceRecord> StaffAttendanceRecords => Set<StaffAttendanceRecord>();
    public DbSet<StaffLeaveRequest> StaffLeaveRequests => Set<StaffLeaveRequest>();
    public DbSet<DemoRequest> DemoRequests => Set<DemoRequest>();
    public DbSet<TenantOnboarding> TenantOnboardings => Set<TenantOnboarding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureEntities(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            var clrType = entityType.ClrType;
            if (clrType == typeof(BrandingProfile))
            {
                ConfigureBrandingProfile(modelBuilder);
                continue;
            }

            if (typeof(IBranchOwned).IsAssignableFrom(clrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ConfigureBranchOwnedEntity), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, [modelBuilder]);
            }
            else if (typeof(ITenantEntity).IsAssignableFrom(clrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ConfigureTenantEntity), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(clrType);
                method.Invoke(this, [modelBuilder]);
            }
            else if (typeof(BaseEntity).IsAssignableFrom(clrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ConfigureBaseEntity), BindingFlags.NonPublic | BindingFlags. Static)!
                    .MakeGenericMethod(clrType);
                method.Invoke(null, [modelBuilder]);
            }
        }

        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var converter = new DateTimeOffsetToBinaryConverter();
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var properties = entityType.ClrType.GetProperties()
                    .Where(p => p.PropertyType == typeof(DateTimeOffset) || p.PropertyType == typeof(DateTimeOffset?));
                foreach (var property in properties)
                {
                    modelBuilder.Entity(entityType.Name)
                        .Property(property.Name)
                        .HasConversion(converter);
                }
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenancyAndAuditRules(allowHardDelete: false);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTenancyAndAuditRules(allowHardDelete: false);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public Task<int> SaveChangesHardDeleteAsync(CancellationToken cancellationToken = default)
    {
        ApplyTenancyAndAuditRules(allowHardDelete: true);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    private void ApplyTenancyAndAuditRules(bool allowHardDelete)
    {
        if (tenantContext.IsReadOnly && !tenantContext.IsSystemScope)
        {
            var hasChanges = ChangeTracker.Entries()
                .Any(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);

            if (hasChanges)
            {
                throw new ReadOnlyAccessException();
            }
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.Id == Guid.Empty)
                    {
                        entry.Entity.Id = Guid.NewGuid();
                    }

                    entry.Entity.CreatedAt = now;
                    EnsureBranchAccess(entry.Entity);
                    if (!entry.Entity.CreatedBy.HasValue && tenantContext.UserId.HasValue)
                    {
                        entry.Entity.CreatedBy = tenantContext.UserId;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    EnsureBranchAccess(entry.Entity);
                    break;

                case EntityState.Deleted:
                    if (!allowHardDelete)
                    {
                        entry.State = EntityState.Modified;
                        entry.Entity.IsDeleted = true;
                        entry.Entity.UpdatedAt = now;
                    }
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            if (tenantContext.IsSystemScope)
            {
                if (entry.State == EntityState.Added
                    && entry.Entity.TenantId == Guid.Empty
                    && tenantContext.TenantId.HasValue)
                {
                    entry.Entity.TenantId = tenantContext.TenantId.Value;
                }

                continue;
            }

            var activeTenantId = tenantContext.TenantId ?? Guid.Empty;
            if (activeTenantId == Guid.Empty)
            {
                throw new CrossTenantOperationException("Cannot write tenant-scoped entities without an active tenant context.");
            }

            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.TenantId = activeTenantId;
                }
                else if (entry.Entity.TenantId != activeTenantId)
                {
                    throw new CrossTenantOperationException("Cannot create an entity belonging to a different tenant.");
                }
            }
            else if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                var originalTenantId = entry.OriginalValues.GetValue<Guid>(nameof(ITenantEntity.TenantId));
                var currentTenantIdValue = entry.CurrentValues.GetValue<Guid>(nameof(ITenantEntity.TenantId));

                if (originalTenantId != activeTenantId || currentTenantIdValue != activeTenantId)
                {
                    throw new CrossTenantOperationException("Cross-tenant modification or deletion is forbidden.");
                }
            }
        }
    }

    private void EnsureBranchAccess(BaseEntity entity)
    {
        if (tenantContext.IsSystemScope || entity is not IBranchOwned owned)
        {
            return;
        }

        branches.EnsureCanUse(owned.BranchId);
    }

    private void ConfigureBranchOwnedEntity<TEntity>(ModelBuilder modelBuilder)
        where TEntity : BaseEntity, ITenantEntity, IBranchOwned
    {
        modelBuilder.Entity<TEntity>(builder =>
        {
            builder.HasIndex(e => e.TenantId);
            builder.HasIndex(e => e.BranchId);
            builder.HasQueryFilter(e => !e.IsDeleted
                && CurrentTenantId.HasValue
                && e.TenantId == CurrentTenantId.Value
                && (BranchOpen || AllowedBranchIds.Contains(e.BranchId ?? MainBranchId))
                && (ActiveBranchId == null || (e.BranchId ?? MainBranchId) == ActiveBranchId));
        });
    }

    private void ConfigureTenantEntity<TEntity>(ModelBuilder modelBuilder)
        where TEntity : BaseEntity, ITenantEntity
    {
        modelBuilder.Entity<TEntity>(builder =>
        {
            builder.HasIndex(e => e.TenantId);
            builder.HasQueryFilter(e => !e.IsDeleted && CurrentTenantId.HasValue && e.TenantId == CurrentTenantId.Value);
        });
    }

    private static void ConfigureBaseEntity<TEntity>(ModelBuilder modelBuilder)
        where TEntity : BaseEntity
    {
        modelBuilder.Entity<TEntity>(builder =>
        {
            builder.HasQueryFilter(e => !e.IsDeleted);
        });
    }

    private static void ConfigureEntities(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Subdomain).HasMaxLength(63).IsRequired();
            b.Property(x => x.ContactEmail).HasMaxLength(256);
            b.Property(x => x.ContactPhone).HasMaxLength(50);
            b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
            b.HasIndex(x => x.Subdomain).IsUnique();
        });

        modelBuilder.Entity<Branch>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Code).HasMaxLength(32).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code });
        });

        modelBuilder.Entity<StaffBranchAssignment>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.UserId, x.BranchId });
        });

        modelBuilder.Entity<User>(b =>
        {
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(32);
            b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Email });
            b.HasIndex(x => new { x.TenantId, x.Phone });
            b.HasOne(x => x.CustomRole)
                .WithMany()
                .HasForeignKey(x => x.CustomRoleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Role>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Name });
            b.HasMany(x => x.Permissions)
                .WithOne(x => x.Role)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermission>(b =>
        {
            b.Property(x => x.PermissionKey).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.RoleId, x.PermissionKey });
        });

        modelBuilder.Entity<Permission>(b =>
        {
            b.Property(x => x.Key).HasMaxLength(100).IsRequired();
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Group).HasMaxLength(100).IsRequired();
            b.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
            b.Property(x => x.IsDemoReadOnly).HasDefaultValue(false);
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasIndex(x => x.FamilyId);
            b.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Plan>(b =>
        {
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            b.Property(x => x.PriceMonthlyLkr).HasPrecision(18, 2);
            b.Property(x => x.PriceYearlyLkr).HasPrecision(18, 2);
            b.Property(x => x.SetupFeeLkr).HasPrecision(18, 2);
            b.Property(x => x.ExtraSmsPerSegmentLkr).HasPrecision(18, 2);
            b.Property(x => x.ExtraStoragePerGbLkr).HasPrecision(18, 2);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasMany(x => x.Features)
                .WithOne(x => x.Plan)
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Limits)
                .WithOne(x => x.Plan)
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlanFeature>(b =>
        {
            b.HasIndex(x => new { x.PlanId, x.FeatureKey });
        });

        modelBuilder.Entity<PlanLimit>(b =>
        {
            b.HasIndex(x => new { x.PlanId, x.LimitKey });
        });

        modelBuilder.Entity<Subscription>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasOne(x => x.Plan)
                .WithMany()
                .HasForeignKey(x => x.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantFeatureSetting>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.FeatureKey });
        });

        modelBuilder.Entity<TenantFeatureOverride>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.FeatureKey });
        });

        modelBuilder.Entity<AuditLog>(b =>
        {
            b.Property(x => x.Action).HasMaxLength(150).IsRequired();
            b.Property(x => x.EntityName).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<EducationLevel>(b =>
        {
            b.Property(x => x.Code).HasMaxLength(20).IsRequired();
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.Property(x => x.Description).HasMaxLength(300).IsRequired();
            b.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<TenantEducationLevel>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.EducationLevelId }).IsUnique();
            b.HasOne<EducationLevel>().WithMany().HasForeignKey(x => x.EducationLevelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BatchGroup>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Code).HasMaxLength(32).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        modelBuilder.Entity<AcademicBatch>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Code).HasMaxLength(32).IsRequired();
            b.Property(x => x.AcademicYear).HasMaxLength(20);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasOne<EducationLevel>().WithMany().HasForeignKey(x => x.EducationLevelId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<BatchGroup>().WithMany().HasForeignKey(x => x.BatchGroupId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Subject>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Code).HasMaxLength(32).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.HasOne<EducationLevel>().WithMany().HasForeignKey(x => x.EducationLevelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClassSchedule>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Room).HasMaxLength(50);
            b.HasIndex(x => new { x.TenantId, x.BatchId, x.IsActive });
            b.HasIndex(x => new { x.TenantId, x.Weekday, x.StartMinutes });
            b.HasOne<AcademicBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<User>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Student>(b =>
        {
            b.Property(x => x.StudentCode).HasMaxLength(32).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            b.Property(x => x.PhotoUrl).HasMaxLength(500);
            b.Property(x => x.GuardianName).HasMaxLength(200).IsRequired();
            b.Property(x => x.GuardianPhone).HasMaxLength(40).IsRequired();
            b.Property(x => x.GuardianPhoneSecondary).HasMaxLength(40);
            b.Property(x => x.Address).HasMaxLength(300).IsRequired();
            b.Property(x => x.City).HasMaxLength(80);
            b.Property(x => x.Notes).HasMaxLength(2000);
            b.HasIndex(x => new { x.TenantId, x.StudentCode }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status, x.EducationLevelId });
            b.HasIndex(x => new { x.TenantId, x.FullName });
            b.HasOne<EducationLevel>().WithMany().HasForeignKey(x => x.EducationLevelId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StudentEnrollment>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.BatchId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.BatchId, x.IsActive });
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendancePolicy>(b =>
        {
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<ClassSession>(b =>
        {
            b.Property(x => x.JoinCode).HasMaxLength(12);
            b.Property(x => x.CancelReason).HasMaxLength(300);
            b.HasIndex(x => new { x.TenantId, x.ClassScheduleId, x.SessionDate }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.SessionDate, x.Status });
            b.HasOne<ClassSchedule>().WithMany().HasForeignKey(x => x.ClassScheduleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendanceMark>(b =>
        {
            b.Property(x => x.Reason).HasMaxLength(300);
            b.Property(x => x.UndoReason).HasMaxLength(300);
            b.HasIndex(x => new { x.TenantId, x.ClassSessionId, x.StudentId }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.IsUndone, x.MarkedAt });
            b.HasOne<ClassSession>().WithMany().HasForeignKey(x => x.ClassSessionId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantNotificationConfig>(b =>
        {
            b.Property(x => x.DefaultLanguage).HasMaxLength(10).IsRequired();
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<TenantNotificationTypeSetting>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.MessageType }).IsUnique();
        });

        modelBuilder.Entity<NotificationTemplate>(b =>
        {
            b.Property(x => x.Language).HasMaxLength(10).IsRequired();
            b.Property(x => x.Subject).HasMaxLength(250).IsRequired();
            b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.MessageType, x.Language }).IsUnique();
        });

        modelBuilder.Entity<NotificationOutbox>(b =>
        {
            b.Property(x => x.Language).HasMaxLength(10).IsRequired();
            b.Property(x => x.Recipient).HasMaxLength(256).IsRequired();
            b.Property(x => x.RecipientName).HasMaxLength(200);
            b.Property(x => x.StudentName).HasMaxLength(200);
            b.Property(x => x.StudentCode).HasMaxLength(32);
            b.Property(x => x.Subject).HasMaxLength(250);
            b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            b.Property(x => x.IdempotencyKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.Provider).HasMaxLength(64);
            b.Property(x => x.ProviderMessageId).HasMaxLength(128);
            b.Property(x => x.StatusReason).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.Channel, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status, x.NextAttemptAt });
            b.HasIndex(x => new { x.TenantId, x.CreatedAt, x.MessageType, x.Status });
        });

        modelBuilder.Entity<FeeStructure>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.IsActive });
            b.HasOne<EducationLevel>().WithMany().HasForeignKey(x => x.EducationLevelId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StudentConcession>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.Value).HasPrecision(18, 2);
            b.Property(x => x.Reason).HasMaxLength(300);
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.IsActive });
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<FeeStructure>().WithMany().HasForeignKey(x => x.FeeStructureId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FeeInvoice>(b =>
        {
            b.Property(x => x.InvoiceNumber).HasMaxLength(40).IsRequired();
            b.Property(x => x.BillingPeriod).HasMaxLength(20).IsRequired();
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.GrossAmount).HasPrecision(18, 2);
            b.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            b.Property(x => x.ConcessionDescription).HasMaxLength(200);
            b.Property(x => x.NetAmount).HasPrecision(18, 2);
            b.Property(x => x.PaidAmount).HasPrecision(18, 2);
            b.Property(x => x.BalanceAmount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            b.Property(x => x.VoidReason).HasMaxLength(300);
            b.Property(x => x.Notes).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.InvoiceNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.FeeStructureId, x.BillingPeriod });
            b.HasIndex(x => new { x.TenantId, x.Status, x.DueDate });
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<FeeStructure>().WithMany().HasForeignKey(x => x.FeeStructureId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationLevel>().WithMany().HasForeignKey(x => x.EducationLevelId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FeePayment>(b =>
        {
            b.Property(x => x.ReceiptNumber).HasMaxLength(40).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.RefundedAmount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            b.Property(x => x.ReferenceNumber).HasMaxLength(120);
            b.Property(x => x.AttachmentUrl).HasMaxLength(500);
            b.Property(x => x.Notes).HasMaxLength(500);
            b.Property(x => x.VoidOrRefundReason).HasMaxLength(300);
            b.HasIndex(x => new { x.TenantId, x.ReceiptSequence }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.ReceiptNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.InvoiceId });
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.PaidAt });
            b.HasOne<FeeInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantPaymentGatewayConfig>(b =>
        {
            b.Property(x => x.MerchantId).HasMaxLength(120).IsRequired();
            b.Property(x => x.EncryptedMerchantSecret).HasMaxLength(1000).IsRequired();
            b.Property(x => x.NotifyUrlOverride).HasMaxLength(500);
            b.Property(x => x.ReturnUrlOverride).HasMaxLength(500);
            b.Property(x => x.CancelUrlOverride).HasMaxLength(500);
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<PayHereCheckoutSession>(b =>
        {
            b.Property(x => x.OrderId).HasMaxLength(80).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            b.Property(x => x.PayHerePaymentId).HasMaxLength(120);
            b.Property(x => x.StatusMessage).HasMaxLength(500);
            b.HasIndex(x => x.OrderId).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.InvoiceId });
            b.HasOne<FeeInvoice>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlatformSubscriptionInvoice>(b =>
        {
            b.Property(x => x.InvoiceNumber).HasMaxLength(40).IsRequired();
            b.Property(x => x.PlanName).HasMaxLength(100).IsRequired();
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.Currency).HasMaxLength(10).IsRequired();
            b.Property(x => x.PaymentReference).HasMaxLength(150);
            b.Property(x => x.Notes).HasMaxLength(500);
            b.HasIndex(x => x.InvoiceNumber).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status });
            b.HasOne<Plan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void ConfigureBrandingProfile(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GradingScheme>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Name });
        });

        modelBuilder.Entity<GradeBoundary>(b =>
        {
            b.Property(x => x.Grade).HasMaxLength(8).IsRequired();
            b.Property(x => x.MinPercent).HasPrecision(5, 2);
            b.Property(x => x.MaxPercent).HasPrecision(5, 2);
            b.Property(x => x.GradePoint).HasPrecision(4, 2);
            b.HasIndex(x => new { x.TenantId, x.GradingSchemeId, x.SortOrder });
            b.HasOne<GradingScheme>().WithMany().HasForeignKey(x => x.GradingSchemeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Exam>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(150).IsRequired();
            b.Property(x => x.SeriesKey).HasMaxLength(80).IsRequired();
            b.Property(x => x.Room).HasMaxLength(80);
            b.HasIndex(x => new { x.TenantId, x.BatchId, x.Status });
            b.HasIndex(x => new { x.TenantId, x.SeriesKey, x.EducationLevelId });
            b.HasOne<AcademicBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<EducationLevel>().WithMany().HasForeignKey(x => x.EducationLevelId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<GradingScheme>().WithMany().HasForeignKey(x => x.GradingSchemeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamSubject>(b =>
        {
            b.Property(x => x.MaxMarks).HasPrecision(8, 2);
            b.Property(x => x.Room).HasMaxLength(80);
            b.HasIndex(x => new { x.TenantId, x.ExamId, x.SubjectId });
            b.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamMark>(b =>
        {
            b.Property(x => x.Marks).HasPrecision(8, 2);
            b.Property(x => x.Grade).HasMaxLength(8);
            b.HasIndex(x => new { x.TenantId, x.ExamSubjectId, x.StudentId });
            b.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<ExamSubject>().WithMany().HasForeignKey(x => x.ExamSubjectId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamResult>(b =>
        {
            b.Property(x => x.TotalMarks).HasPrecision(8, 2);
            b.Property(x => x.MaxMarks).HasPrecision(8, 2);
            b.Property(x => x.Percentage).HasPrecision(5, 2);
            b.Property(x => x.Grade).HasMaxLength(8).IsRequired();
            b.Property(x => x.AttendancePercent).HasPrecision(5, 2);
            b.Property(x => x.TeacherRemarks).HasMaxLength(500);
            b.Property(x => x.PrincipalRemarks).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.ExamId, x.StudentId });
            b.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParentStudentLink>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.ParentUserId, x.StudentId }).IsUnique();
            b.HasOne<User>().WithMany().HasForeignKey(x => x.ParentUserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantBackupPolicy>(b =>
        {
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<TenantBackupRecord>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(200).IsRequired();
            b.Property(x => x.StorageUrl).HasMaxLength(500).IsRequired();
            b.Property(x => x.Sha256).HasMaxLength(128).IsRequired();
            b.Property(x => x.SummaryJson).HasMaxLength(4000).IsRequired();
            b.Property(x => x.ErrorMessage).HasMaxLength(1000);
            b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<TenantCleanupPolicy>(b =>
        {
            b.Property(x => x.LastArchiveUrl).HasMaxLength(500);
            b.HasIndex(x => x.TenantId).IsUnique();
        });

        modelBuilder.Entity<PlatformBackupRecord>(b =>
        {
            b.Property(x => x.FileName).HasMaxLength(200).IsRequired();
            b.Property(x => x.StorageUrl).HasMaxLength(500).IsRequired();
            b.Property(x => x.DatabaseProvider).HasMaxLength(100).IsRequired();
            b.Property(x => x.SqlCommandSummary).HasMaxLength(500).IsRequired();
            b.Property(x => x.Sha256).HasMaxLength(128).IsRequired();
            b.Property(x => x.ErrorMessage).HasMaxLength(1000);
            b.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<PlatformAnnouncement>(b =>
        {
            b.Property(x => x.Title).HasMaxLength(200).IsRequired();
            b.Property(x => x.Body).HasMaxLength(2000).IsRequired();
            b.HasIndex(x => new { x.IsActive, x.CreatedAt });
        });

        modelBuilder.Entity<PlatformSetting>(b =>
        {
            b.Property(x => x.Key).HasMaxLength(50).IsRequired();
            b.Property(x => x.MaintenanceMessage).HasMaxLength(500);
            b.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<StaffAttendanceRecord>(b =>
        {
            b.Property(x => x.Notes).HasMaxLength(300);
            b.HasIndex(x => new { x.TenantId, x.StaffUserId, x.Date }).IsUnique();
            b.HasOne<User>().WithMany().HasForeignKey(x => x.StaffUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StaffLeaveRequest>(b =>
        {
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.Property(x => x.ReviewNotes).HasMaxLength(500);
            b.HasIndex(x => new { x.TenantId, x.StaffUserId, x.Status });
            b.HasOne<User>().WithMany().HasForeignKey(x => x.StaffUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BrandingProfile>(b =>
        {
            b.Property(x => x.PrimaryColor).HasMaxLength(7).IsRequired();
            b.Property(x => x.AccentColor).HasMaxLength(7).IsRequired();
            b.Property(x => x.LogoUrl).HasMaxLength(500);
            b.Property(x => x.FaviconUrl).HasMaxLength(500);
            b.Property(x => x.LoginBackgroundUrl).HasMaxLength(500);
            b.Property(x => x.InstituteDisplayName).HasMaxLength(200).IsRequired();
            b.Property(x => x.Tagline).HasMaxLength(300);
            b.Property(x => x.FontPreference).HasMaxLength(20).IsRequired();
            b.Property(x => x.ThemeMode).HasMaxLength(20).IsRequired();
            b.HasIndex(x => x.TenantId).IsUnique();
            b.HasQueryFilter(x => !x.IsDeleted
                && ((!CurrentTenantId.HasValue && x.TenantId == Guid.Empty)
                    || (CurrentTenantId.HasValue && x.TenantId == CurrentTenantId.Value)));
        });

        modelBuilder.Entity<DemoRequest>(b =>
        {
            b.Property(x => x.InstituteName).HasMaxLength(120).IsRequired();
            b.Property(x => x.ContactName).HasMaxLength(120).IsRequired();
            b.Property(x => x.Email).HasMaxLength(200).IsRequired();
            b.Property(x => x.Phone).HasMaxLength(40);
            b.Property(x => x.City).HasMaxLength(80);
            b.Property(x => x.Message).HasMaxLength(2000);
            b.HasIndex(x => x.Email);
        });

        modelBuilder.Entity<TenantOnboarding>(b =>
        {
            b.HasIndex(x => x.TenantId).IsUnique();
        });
    }
}
