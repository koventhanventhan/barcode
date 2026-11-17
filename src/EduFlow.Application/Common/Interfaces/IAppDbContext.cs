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
    DbSet<EducationLevel> EducationLevels { get; }
    DbSet<TenantEducationLevel> TenantEducationLevels { get; }
    DbSet<BatchGroup> BatchGroups { get; }
    DbSet<AcademicBatch> AcademicBatches { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<ClassSchedule> ClassSchedules { get; }
    DbSet<Student> Students { get; }
    DbSet<StudentEnrollment> StudentEnrollments { get; }
    DbSet<TenantNotificationConfig> TenantNotificationConfigs { get; }
    DbSet<TenantNotificationTypeSetting> TenantNotificationTypeSettings { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<NotificationOutbox> NotificationOutboxes { get; }
    DbSet<AttendancePolicy> AttendancePolicies { get; }
    DbSet<ClassSession> ClassSessions { get; }
    DbSet<AttendanceMark> AttendanceMarks { get; }
    DbSet<FeeStructure> FeeStructures { get; }
    DbSet<StudentConcession> StudentConcessions { get; }
    DbSet<FeeInvoice> FeeInvoices { get; }
    DbSet<FeePayment> FeePayments { get; }
    DbSet<TenantPaymentGatewayConfig> TenantPaymentGatewayConfigs { get; }
    DbSet<PayHereCheckoutSession> PayHereCheckoutSessions { get; }
    DbSet<PlatformSubscriptionInvoice> PlatformSubscriptionInvoices { get; }
    DbSet<GradingScheme> GradingSchemes { get; }
    DbSet<GradeBoundary> GradeBoundaries { get; }
    DbSet<Exam> Exams { get; }
    DbSet<ExamSubject> ExamSubjects { get; }
    DbSet<ExamMark> ExamMarks { get; }
    DbSet<ExamResult> ExamResults { get; }
    DbSet<ParentStudentLink> ParentStudentLinks { get; }
    DbSet<TenantBackupPolicy> TenantBackupPolicies { get; }
    DbSet<TenantBackupRecord> TenantBackupRecords { get; }
    DbSet<TenantCleanupPolicy> TenantCleanupPolicies { get; }
    DbSet<PlatformBackupRecord> PlatformBackupRecords { get; }
    DbSet<PlatformAnnouncement> PlatformAnnouncements { get; }
    DbSet<PlatformSetting> PlatformSettings { get; }
    DbSet<StaffAttendanceRecord> StaffAttendanceRecords { get; }
    DbSet<StaffLeaveRequest> StaffLeaveRequests { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesHardDeleteAsync(CancellationToken cancellationToken = default);
}
