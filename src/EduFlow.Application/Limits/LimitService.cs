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
            if (maxAllowed.HasValue)
            {
                var addOnType = limit switch
                {
                    LimitKey.Students => TenantAddOnType.ExtraStudents,
                    LimitKey.SmsSegmentsPerMonth => TenantAddOnType.ExtraSmsSegments,
                    LimitKey.CloudStorageGb => TenantAddOnType.ExtraStorageGb,
                    _ => (TenantAddOnType?)null
                };
                var extra = addOnType.HasValue
                    ? await ActiveAddOnQuantityAsync(tenantId, addOnType.Value, cancellationToken)
                    : 0;
                if (limit == LimitKey.Students)
                {
                    extra += Math.Max(0, subscription.ExtraStudentsAllowed);
                }

                var raised = (long)maxAllowed.Value + extra;
                maxAllowed = raised > int.MaxValue ? int.MaxValue : (int)raised;
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

    public async Task<StorageUsageDto> GetStorageUsageAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var check = await CheckAsync(tenantId, LimitKey.CloudStorageGb, increment: 0, cancellationToken: cancellationToken);
        var used = await UsedStorageBytesAsync(tenantId, cancellationToken);
        long? quotaBytes = check.MaxAllowed.HasValue
            ? check.MaxAllowed.Value * 1024L * 1024L * 1024L
            : null;
        var percentage = quotaBytes is null or 0
            ? used > 0 && quotaBytes == 0 ? 100d : 0d
            : Math.Round(used * 100d / quotaBytes.Value, 1);
        return new StorageUsageDto(used, quotaBytes, check.MaxAllowed, percentage);
    }

    public async Task EnsureStorageAllowsAsync(
        Guid tenantId,
        long additionalBytes,
        long replacingBytes = 0,
        CancellationToken cancellationToken = default)
    {
        var usage = await GetStorageUsageAsync(tenantId, cancellationToken);
        if (usage.QuotaBytes is not long quota)
        {
            return;
        }

        var next = usage.UsedBytes - Math.Max(0, replacingBytes) + Math.Max(0, additionalBytes);
        if (next > quota && usage.MaxGigabytes.HasValue)
        {
            throw new PlanLimitReachedException(LimitKey.CloudStorageGb, usage.MaxGigabytes.Value);
        }
    }

    private async Task<int> ActiveAddOnQuantityAsync(
        Guid tenantId,
        TenantAddOnType type,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var quantities = await db.TenantAddOns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(addOn => !addOn.IsDeleted
                            && addOn.TenantId == tenantId
                            && addOn.Type == type
                            && addOn.ValidFrom <= now
                            && (addOn.ValidTo == null || addOn.ValidTo > now))
            .Select(addOn => addOn.Quantity)
            .ToListAsync(cancellationToken);

        long sum = 0;
        foreach (var quantity in quantities)
        {
            sum += quantity;
        }

        return sum > int.MaxValue ? int.MaxValue : (int)sum;
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
        var bytes = await UsedStorageBytesAsync(tenantId, cancellationToken);
        var gigabytes = bytes / (1024L * 1024L * 1024L);
        return gigabytes > int.MaxValue ? int.MaxValue : (int)gigabytes;
    }

    private async Task<long> UsedStorageBytesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var files = await db.StoredFileRecords
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file => !file.IsDeleted && file.TenantId == tenantId)
            .Select(file => new { file.Category, file.OwnerId, file.SizeBytes })
            .ToListAsync(cancellationToken);

        long fileBytes = 0;
        var photoOwners = new HashSet<Guid>();
        var paymentOwners = new HashSet<Guid>();
        foreach (var file in files)
        {
            fileBytes += file.SizeBytes;
            if (file.OwnerId is not Guid ownerId)
            {
                continue;
            }

            if (file.Category == StoredFileCategory.StudentPhoto)
            {
                photoOwners.Add(ownerId);
            }
            else if (file.Category == StoredFileCategory.PaymentAttachment)
            {
                paymentOwners.Add(ownerId);
            }
        }

        var photos = await db.Students
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(student => !student.IsDeleted && student.TenantId == tenantId && student.PhotoBytes > 0)
            .Select(student => new { student.Id, student.PhotoBytes })
            .ToListAsync(cancellationToken);
        var payments = await db.FeePayments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(payment => !payment.IsDeleted && payment.TenantId == tenantId && payment.AttachmentBytes > 0)
            .Select(payment => new { payment.Id, payment.AttachmentBytes })
            .ToListAsync(cancellationToken);

        long legacy = 0;
        foreach (var photo in photos)
        {
            if (!photoOwners.Contains(photo.Id))
            {
                legacy += photo.PhotoBytes;
            }
        }

        foreach (var payment in payments)
        {
            if (!paymentOwners.Contains(payment.Id))
            {
                legacy += payment.AttachmentBytes;
            }
        }

        return fileBytes + legacy;
    }
}
