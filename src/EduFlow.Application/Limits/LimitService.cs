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

            LimitKey.Students => await db.Students
                .IgnoreQueryFilters()
                .CountAsync(s => !s.IsDeleted && s.TenantId == tenantId, cancellationToken),

            LimitKey.EducationLevels => await db.TenantEducationLevels
                .IgnoreQueryFilters()
                .CountAsync(s => !s.IsDeleted && s.TenantId == tenantId && s.IsEnabled, cancellationToken),

            LimitKey.Batches => await db.AcademicBatches
                .IgnoreQueryFilters()
                .CountAsync(s => !s.IsDeleted && s.TenantId == tenantId, cancellationToken),

            LimitKey.Subjects => await db.Subjects
                .IgnoreQueryFilters()
                .CountAsync(s => !s.IsDeleted && s.TenantId == tenantId, cancellationToken),

            LimitKey.SmsSegmentsPerMonth => await SmsSegmentsThisMonthAsync(tenantId, cancellationToken),

            LimitKey.CloudStorageGb => await CloudGigabytesAsync(tenantId, cancellationToken),

            _ => 0
        };
    }

    private async Task<int> SmsSegmentsThisMonthAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthEnd = monthStart.AddMonths(1);

        var rows = await db.NotificationOutboxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => !o.IsDeleted
                        && o.TenantId == tenantId
                        && o.Channel == NotificationChannel.Sms
                        && (o.Status == NotificationStatus.Sent || o.Status == NotificationStatus.Delivered))
            .Select(o => new { o.SentAt, o.CreatedAt, o.SmsSegments })
            .ToListAsync(cancellationToken);

        return rows
            .Where(o =>
            {
                var ts = o.SentAt ?? o.CreatedAt;
                return ts >= monthStart && ts < monthEnd;
            })
            .Sum(o => o.SmsSegments);
    }

    private async Task<int> CloudGigabytesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var studentBytes = await db.Students
            .IgnoreQueryFilters()
            .Where(student => !student.IsDeleted && student.TenantId == tenantId)
            .SumAsync(student => (long?)student.PhotoBytes, cancellationToken) ?? 0;
        var paymentBytes = await db.FeePayments
            .IgnoreQueryFilters()
            .Where(payment => !payment.IsDeleted && payment.TenantId == tenantId)
            .SumAsync(payment => (long?)payment.AttachmentBytes, cancellationToken) ?? 0;
        var bytes = studentBytes + paymentBytes;
        return (int)(bytes / (1024L * 1024L * 1024L));
    }
}
