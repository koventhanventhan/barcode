using EduFlow.Domain.Enums;

namespace EduFlow.Application.Common.Interfaces;

public sealed record FeatureEvaluationResult(
    FeatureKey Feature,
    bool IsEnabled,
    bool IsInPlan,
    bool IsOverridden,
    bool IsTenantSwitchOn,
    string? DisabledCode);

public sealed record TenantFeatureStatusDto(
    string FeatureKey,
    bool IsInPlan,
    bool IsOverridden,
    bool IsTenantEnabled,
    bool IsEffectiveEnabled,
    DateTimeOffset? UpdatedAt);

public interface IFeatureService
{
    Task<bool> IsEnabledAsync(Guid tenantId, FeatureKey key, CancellationToken cancellationToken = default);
    Task<FeatureEvaluationResult> EvaluateAsync(Guid tenantId, FeatureKey key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantFeatureStatusDto>> GetAllFeaturesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<TenantFeatureStatusDto> SetTenantFeatureAsync(Guid tenantId, FeatureKey key, bool isEnabled, CancellationToken cancellationToken = default);
    void InvalidateTenantCache(Guid tenantId);
    void InvalidateAllCache();
}
