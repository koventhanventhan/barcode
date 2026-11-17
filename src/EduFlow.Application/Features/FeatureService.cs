using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EduFlow.Application.Features;

public sealed class FeatureService(
    IAppDbContext db,
    IMemoryCache cache,
    ITenantContext tenantContext,
    IAuditService auditService) : IFeatureService
{
    private static int _cacheVersion = 1;

    public async Task<bool> IsEnabledAsync(Guid tenantId, FeatureKey key, CancellationToken cancellationToken = default)
    {
        var evaluation = await EvaluateAsync(tenantId, key, cancellationToken);
        return evaluation.IsEnabled;
    }

    public async Task<FeatureEvaluationResult> EvaluateAsync(
        Guid tenantId,
        FeatureKey key,
        CancellationToken cancellationToken = default)
    {
        var map = await GetEvaluationMapAsync(tenantId, cancellationToken);
        if (map.TryGetValue(key, out var result))
        {
            return result;
        }

        return new FeatureEvaluationResult(
            key,
            IsEnabled: false,
            IsInPlan: false,
            IsOverridden: false,
            IsTenantSwitchOn: false,
            DisabledCode: "FEATURE_NOT_IN_PLAN");
    }

    public async Task<IReadOnlyList<TenantFeatureStatusDto>> GetAllFeaturesAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var map = await GetEvaluationMapAsync(tenantId, cancellationToken);
        var settings = await db.TenantFeatureSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.TenantId == tenantId)
            .ToDictionaryAsync(s => s.FeatureKey, cancellationToken);

        var list = new List<TenantFeatureStatusDto>();
        foreach (var key in Enum.GetValues<FeatureKey>())
        {
            var eval = map[key];
            settings.TryGetValue(key, out var setting);
            list.Add(new TenantFeatureStatusDto(
                key.ToString(),
                eval.IsInPlan,
                eval.IsOverridden,
                eval.IsTenantSwitchOn,
                eval.IsEnabled,
                setting?.UpdatedAt ?? setting?.CreatedAt));
        }

        return list;
    }

    public async Task<TenantFeatureStatusDto> SetTenantFeatureAsync(
        Guid tenantId,
        FeatureKey key,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        var current = await EvaluateAsync(tenantId, key, cancellationToken);
        if (!current.IsInPlan)
        {
            throw new FeatureNotInPlanException(key);
        }

        TenantFeatureSetting? setting;
        using (tenantContext.BeginSystemScope(tenantId))
        {
            setting = await db.TenantFeatureSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    s => !s.IsDeleted && s.TenantId == tenantId && s.FeatureKey == key,
                    cancellationToken);

            var previousValue = setting?.IsEnabled ?? true;

            if (setting is null)
            {
                setting = new TenantFeatureSetting
                {
                    TenantId = tenantId,
                    FeatureKey = key,
                    IsEnabled = isEnabled,
                    UpdatedBy = tenantContext.UserId,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                db.TenantFeatureSettings.Add(setting);
            }
            else
            {
                setting.IsEnabled = isEnabled;
                setting.UpdatedBy = tenantContext.UserId;
                setting.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await auditService.LogAsync(
                action: "FeatureSwitch.Changed",
                entityName: nameof(TenantFeatureSetting),
                entityId: setting.Id.ToString(),
                details: new
                {
                    FeatureKey = key.ToString(),
                    PreviousEnabled = previousValue,
                    NewEnabled = isEnabled
                },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            InvalidateTenantCache(tenantId);
        }

        var updated = await EvaluateAsync(tenantId, key, cancellationToken);
        return new TenantFeatureStatusDto(
            key.ToString(),
            updated.IsInPlan,
            updated.IsOverridden,
            updated.IsTenantSwitchOn,
            updated.IsEnabled,
            setting.UpdatedAt ?? setting.CreatedAt);
    }

    public void InvalidateTenantCache(Guid tenantId)
    {
        cache.Remove(GetCacheKey(tenantId));
    }

    public void InvalidateAllCache()
    {
        Interlocked.Increment(ref _cacheVersion);
    }

    private static string GetCacheKey(Guid tenantId) =>
        $"tenant_features:v{Volatile.Read(ref _cacheVersion)}:{tenantId}";

    private async Task<IReadOnlyDictionary<FeatureKey, FeatureEvaluationResult>> GetEvaluationMapAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(tenantId);
        if (cache.TryGetValue<IReadOnlyDictionary<FeatureKey, FeatureEvaluationResult>>(cacheKey, out var cached)
            && cached is not null)
        {
            return cached;
        }

        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var planFeatures = new HashSet<FeatureKey>();
        if (subscription is not null)
        {
            var keys = await db.PlanFeatures
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(pf => !pf.IsDeleted && pf.PlanId == subscription.PlanId && pf.IsIncluded)
                .Select(pf => pf.FeatureKey)
                .ToListAsync(cancellationToken);

            planFeatures = keys.ToHashSet();
        }

        var overrides = await db.TenantFeatureOverrides
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => !o.IsDeleted && o.TenantId == tenantId)
            .ToDictionaryAsync(o => o.FeatureKey, cancellationToken);

        var settings = await db.TenantFeatureSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.TenantId == tenantId)
            .ToDictionaryAsync(s => s.FeatureKey, cancellationToken);

        var dict = new Dictionary<FeatureKey, FeatureEvaluationResult>();
        foreach (var key in Enum.GetValues<FeatureKey>())
        {
            var hasOverride = overrides.TryGetValue(key, out var overrideItem);
            var isInPlan = hasOverride ? overrideItem!.IsEnabled : planFeatures.Contains(key);
            var isTenantSwitchOn = settings.TryGetValue(key, out var settingItem) ? settingItem.IsEnabled : true;
            var isEffective = isInPlan && isTenantSwitchOn;
            string? disabledCode = !isInPlan
                ? "FEATURE_NOT_IN_PLAN"
                : !isTenantSwitchOn
                    ? "FEATURE_DISABLED"
                    : null;

            dict[key] = new FeatureEvaluationResult(
                key,
                IsEnabled: isEffective,
                IsInPlan: isInPlan,
                IsOverridden: hasOverride,
                IsTenantSwitchOn: isTenantSwitchOn,
                DisabledCode: disabledCode);
        }

        cache.Set(cacheKey, (IReadOnlyDictionary<FeatureKey, FeatureEvaluationResult>)dict, TimeSpan.FromMinutes(10));
        return dict;
    }
}
