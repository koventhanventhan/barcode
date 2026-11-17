using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Marketing;

public interface ITrialNoticeService
{
    Task<int> SendDueNoticesAsync(DateTimeOffset? nowOverride = null, CancellationToken cancellationToken = default);
}

public sealed class TrialNoticeService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ITransactionalEmailService transactionalEmail) : ITrialNoticeService
{
    public async Task<int> SendDueNoticesAsync(DateTimeOffset? nowOverride = null, CancellationToken cancellationToken = default)
    {
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        var today = ColomboDate(now);
        var subscriptions = await db.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.Status == SubscriptionStatus.Trial && s.TrialEndsAt != null)
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var subscription in subscriptions)
        {
            var endsAt = subscription.TrialEndsAt!.Value;
            var endDate = ColomboDate(endsAt);
            var endingDue = endDate == today.AddDays(3) && subscription.TrialEndingNoticeSentAt is null;
            var expiredDue = endsAt <= now && subscription.TrialExpiredNoticeSentAt is null;
            if (!endingDue && !expiredDue)
            {
                continue;
            }

            var admin = await db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => !u.IsDeleted && u.IsActive && u.TenantId == subscription.TenantId && u.RoleType == RoleType.InstituteAdmin)
                .Select(u => new { u.Email, u.FullName })
                .FirstOrDefaultAsync(cancellationToken);
            if (admin is null || string.IsNullOrWhiteSpace(admin.Email))
            {
                continue;
            }

            using (tenantContext.BeginSystemScope(subscription.TenantId))
            {
                if (endingDue)
                {
                    await transactionalEmail.SendTrialEndingAsync(
                        subscription.TenantId,
                        admin.Email,
                        admin.FullName,
                        endsAt,
                        cancellationToken);
                    subscription.TrialEndingNoticeSentAt = now;
                    sent++;
                }

                if (expiredDue)
                {
                    await transactionalEmail.SendTrialExpiredAsync(
                        subscription.TenantId,
                        admin.Email,
                        admin.FullName,
                        endsAt,
                        cancellationToken);
                    subscription.TrialExpiredNoticeSentAt = now;
                    sent++;
                }

                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return sent;
    }

    internal static DateOnly ColomboDate(DateTimeOffset instant)
    {
        var utc = instant.UtcDateTime;
        foreach (var id in new[] { "Asia/Colombo", "Sri Lanka Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, zone));
            }
        }

        return DateOnly.FromDateTime(utc.AddHours(5.5));
    }
}
