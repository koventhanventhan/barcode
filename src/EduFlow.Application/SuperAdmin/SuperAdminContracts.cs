using EduFlow.Domain.Enums;

namespace EduFlow.Application.SuperAdmin;

public sealed record TenantSummaryDto(
    Guid Id,
    string Name,
    string Subdomain,
    string? ContactEmail,
    string? ContactPhone,
    bool IsActive,
    bool IsDemo,
    string? PlanCode,
    string? PlanName,
    string? SubscriptionStatus,
    DateTimeOffset? SubscriptionEndDate,
    DateTimeOffset CreatedAt);

public sealed record TenantDetailDto(
    Guid Id,
    string Name,
    string Subdomain,
    string? ContactEmail,
    string? ContactPhone,
    string? Address,
    string TimeZone,
    bool IsActive,
    bool IsDemo,
    SubscriptionDto? Subscription,
    IReadOnlyList<FeatureOverrideDto> FeatureOverrides,
    int UsersCount,
    int BranchesCount,
    DateTimeOffset CreatedAt);

public sealed record CreateTenantRequest(
    string Name,
    string Subdomain,
    string AdminEmail,
    string AdminFullName,
    string AdminPassword,
    string? PlanCode = "Starter",
    string? ContactEmail = null,
    string? ContactPhone = null,
    string? Address = null);

public sealed record UpdateTenantRequest(
    string Name,
    string Subdomain,
    string? ContactEmail,
    string? ContactPhone,
    string? Address,
    bool IsActive);

public sealed record PlanLimitInput(LimitKey LimitKey, int? MaxValue);

public sealed record PlanDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    decimal? PriceMonthly,
    string Currency,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<string> Features,
    IReadOnlyDictionary<string, int?> Limits);

public sealed record CreatePlanRequest(
    string Code,
    string Name,
    string? Description,
    decimal? PriceMonthly,
    string Currency,
    int SortOrder,
    IReadOnlyList<FeatureKey> Features,
    IReadOnlyList<PlanLimitInput> Limits);

public sealed record UpdatePlanRequest(
    string Name,
    string? Description,
    decimal? PriceMonthly,
    bool IsActive,
    int SortOrder,
    IReadOnlyList<FeatureKey> Features,
    IReadOnlyList<PlanLimitInput> Limits);

public sealed record SubscriptionDto(
    Guid Id,
    Guid TenantId,
    Guid PlanId,
    string PlanCode,
    string PlanName,
    string Status,
    DateTimeOffset StartDate,
    DateTimeOffset? EndDate,
    DateTimeOffset? TrialEndsAt,
    string? SuspendedReason,
    int ExtraStudentsAllowed);

public sealed record AssignPlanRequest(
    Guid? PlanId = null,
    string? PlanCode = null,
    int? ExtraStudentsAllowed = null);

public sealed record ActivateSubscriptionRequest(
    DateTimeOffset? EndDate = null,
    int DurationMonths = 1,
    string? Notes = null);

public sealed record SuspendSubscriptionRequest(string Reason);

public sealed record ExtendSubscriptionRequest(
    int DaysToAdd = 30,
    DateTimeOffset? NewEndDate = null,
    string? Notes = null);

public sealed record FeatureOverrideDto(
    Guid Id,
    string FeatureKey,
    bool IsEnabled,
    string? Reason,
    Guid? GrantedByUserId,
    DateTimeOffset CreatedAt);

public sealed record SetFeatureOverrideRequest(
    bool IsEnabled,
    string? Reason = null);

public sealed record ImpersonationResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid TenantId,
    string TenantSubdomain,
    bool IsReadOnly);

public sealed record AuditLogDto(
    Guid Id,
    Guid? TenantId,
    Guid? ActorUserId,
    string? ActorEmail,
    bool IsImpersonated,
    string Action,
    string EntityName,
    string? EntityId,
    string? DetailsJson,
    DateTimeOffset CreatedAt);

public sealed record TenantsCountSummaryDto(
    int Total,
    int Active,
    int Trial,
    int Expired,
    int Suspended,
    int Inactive);

public sealed record ExpiringTrialDto(
    Guid TenantId,
    string TenantName,
    string Subdomain,
    string PlanName,
    DateTimeOffset? TrialEndsAt,
    int DaysRemaining,
    string? ContactEmail);

public sealed record TenantSmsUsageDto(
    Guid TenantId,
    string TenantName,
    string Subdomain,
    int UsedSegmentsThisMonth,
    int? MonthlyQuota,
    int ExtraSegmentsThisMonth,
    double UsagePercentage);

public sealed record TenantStorageUsageDto(
    Guid TenantId,
    string TenantName,
    string Subdomain,
    long UsedBytes,
    int? LimitGb,
    double UsagePercentage);

public sealed record FailedJobItemDto(
    Guid Id,
    string Category,
    string? TenantName,
    string Target,
    string Error,
    DateTimeOffset FailedAt);

public sealed record FailedJobsSummaryDto(
    int DeadLetterNotificationsCount,
    int FailedNotificationsCount,
    int FailedBackupsCount,
    IReadOnlyList<FailedJobItemDto> RecentFailures);

public sealed record RecentErrorDto(
    Guid Id,
    string Source,
    string Message,
    string? TenantName,
    DateTimeOffset OccurredAt);

public sealed record MaintenanceModeDto(
    bool IsMaintenanceMode,
    string? Message,
    DateTimeOffset? UpdatedAt);

public sealed record UpdateMaintenanceModeRequest(
    bool IsMaintenanceMode,
    string? Message = null);

public sealed record PlatformAnnouncementDto(
    Guid Id,
    string Title,
    string Body,
    string Severity,
    bool IsActive,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record CreatePlatformAnnouncementRequest(
    string Title,
    string Body,
    AnnouncementSeverity Severity = AnnouncementSeverity.Info,
    DateTimeOffset? ExpiresAt = null);

public sealed record SetAnnouncementActiveRequest(
    bool IsActive);

public sealed record SuperAdminDashboardDto(
    TenantsCountSummaryDto Tenants,
    decimal Mrr,
    decimal CollectedThisMonth,
    string Currency,
    IReadOnlyList<ExpiringTrialDto> TrialsExpiring,
    IReadOnlyList<TenantSmsUsageDto> SmsUsageByTenant,
    IReadOnlyList<TenantStorageUsageDto> StorageByTenant,
    FailedJobsSummaryDto FailedJobs,
    IReadOnlyList<RecentErrorDto> RecentErrors,
    MaintenanceModeDto Maintenance,
    IReadOnlyList<PlatformAnnouncementDto> ActiveAnnouncements);

