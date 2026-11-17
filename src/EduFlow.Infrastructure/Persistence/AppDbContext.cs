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
    ITenantContext tenantContext) : DbContext(options), IAppDbContext
{
    public Guid? CurrentTenantId => tenantContext.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
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

            if (typeof(ITenantEntity).IsAssignableFrom(clrType))
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
        ApplyTenancyAndAuditRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyTenancyAndAuditRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenancyAndAuditRules()
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
                    if (!entry.Entity.CreatedBy.HasValue && tenantContext.UserId.HasValue)
                    {
                        entry.Entity.CreatedBy = tenantContext.UserId;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.UpdatedAt = now;
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
            else if (entry.State == EntityState.Modified)
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

        modelBuilder.Entity<User>(b =>
        {
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            b.HasIndex(x => new { x.TenantId, x.Email });
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
            b.Property(x => x.PriceMonthly).HasPrecision(18, 2);
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
            b.HasOne<EducationLevel>().WithMany().HasForeignKey(x => x.EducationLevelId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<StudentEnrollment>(b =>
        {
            b.HasIndex(x => new { x.TenantId, x.StudentId, x.BatchId }).IsUnique();
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
            b.HasOne<ClassSchedule>().WithMany().HasForeignKey(x => x.ClassScheduleId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendanceMark>(b =>
        {
            b.Property(x => x.Reason).HasMaxLength(300);
            b.Property(x => x.UndoReason).HasMaxLength(300);
            b.HasIndex(x => new { x.TenantId, x.ClassSessionId, x.StudentId }).IsUnique();
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
        });
    }

    private void ConfigureBrandingProfile(ModelBuilder modelBuilder)
    {
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
    }
}
