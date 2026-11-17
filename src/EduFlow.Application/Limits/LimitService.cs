using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Limits;

public sealed class LimitService(IAppDbContext db) : ILimitService
{
    public async Task<LimitCheckResult> CheckAsync(
        Guid tenantId,
        LimitKey limit,
        int? currentCountOverride = null,
        int increment = 1,
        CancellationToken cancellationToken = default)
    {
        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        int? maxAllowed = null;
        if (subscription is not null)
        {
            var planLimit = await db.PlanLimits
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    pl => !pl.IsDeleted && pl.PlanId == subscription.PlanId && pl.LimitKey == limit,
                    cancellationToken);

            maxAllowed = planLimit?.MaxValue;
            if (limit == LimitKey.Students && maxAllowed.HasValue && subscription.ExtraStudentsAllowed > 0)
            {
                maxAllowed += subscription.ExtraStudentsAllowed;
            }
        }

        var currentCount = currentCountOverride ?? await GetCurrentUsageCountAsync(tenantId, limit, cancellationToken);
        var isAllowed = !maxAllowed.HasValue || (currentCount + increment) <= maxAllowed.Value;

        return new LimitCheckResult(limit, isAllowed, currentCount, maxAllowed);
    }

    public async Task EnsureWithinLimitAsync(
        Guid tenantId,
        LimitKey limit,
        int? currentCountOverride = null,
        int increment = 1,
        CancellationToken cancellationToken = default)
    {
        var check = await CheckAsync(tenantId, limit, currentCountOverride, increment, cancellationToken);
        if (!check.IsAllowed && check.MaxAllowed.HasValue)
        {
            throw new PlanLimitReachedException(limit, check.MaxAllowed.Value);
        }
    }

    public async Task<IReadOnlyList<LimitUsageDto>> GetAllLimitsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var list = new List<LimitUsageDto>();
        foreach (var key in Enum.GetValues<LimitKey>())
        {
            var check = await CheckAsync(tenantId, key, increment: 0, cancellationToken: cancellationToken);
            list.Add(new LimitUsageDto(
                key.ToString(),
                check.CurrentCount,
                check.MaxAllowed,
                !check.MaxAllowed.HasValue));
        }

        return list;
    }

    private async Task<int> GetCurrentUsageCountAsync(
        Guid tenantId,
        LimitKey limit,
        CancellationToken cancellationToken)
    {
        return limit switch
        {
            LimitKey.AdminAccounts => await db.Users
                .IgnoreQueryFilters()
                .CountAsync(
                    u => !u.IsDeleted && u.TenantId == tenantId && u.RoleType == RoleType.InstituteAdmin,
                    cancellationToken),

            LimitKey.StaffAccounts => await db.Users
                .IgnoreQueryFilters()
                .CountAsync(
                    u => !u.IsDeleted && u.TenantId == tenantId && (u.RoleType == RoleType.Staff || u.RoleType == RoleType.Teacher),
                    cancellationToken),

            _ => 0
        };
    }
}
