using EduFlow.Domain.Enums;

namespace EduFlow.Application.Common.Interfaces;

public sealed record LimitCheckResult(
    LimitKey Limit,
    bool IsAllowed,
    int CurrentCount,
    int? MaxAllowed);

public sealed record LimitUsageDto(
    string LimitKey,
    int CurrentCount,
    int? MaxAllowed,
    bool IsUnlimited);

public sealed record StorageUsageDto(
    long UsedBytes,
    long? QuotaBytes,
    int? MaxGigabytes,
    double UsagePercentage);

public interface ILimitService
{
    Task<LimitCheckResult> CheckAsync(
        Guid tenantId,
        LimitKey limit,
        int? currentCountOverride = null,
        int increment = 1,
        CancellationToken cancellationToken = default);

    Task EnsureWithinLimitAsync(
        Guid tenantId,
        LimitKey limit,
        int? currentCountOverride = null,
        int increment = 1,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LimitUsageDto>> GetAllLimitsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    Task<StorageUsageDto> GetStorageUsageAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task EnsureStorageAllowsAsync(
        Guid tenantId,
        long additionalBytes,
        long replacingBytes = 0,
        CancellationToken cancellationToken = default);
}
