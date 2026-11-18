using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Notifications;
using EduFlow.Application.Parents;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Broadcasts;

public sealed class BroadcastService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService features,
    INotificationService notifications,
    ISmsSegmentCalculator segments,
    IAuditService audit,
    ISmsGatewayConfigProvider? smsGatewayConfig = null) : IBroadcastService
{
    private const int MaxAudience = 5000;

    public async Task<BroadcastSmsStatusDto> GetSmsStatusAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        if (smsGatewayConfig is null)
        {
            return new BroadcastSmsStatusDto(false, null);
        }

        var cfg = await smsGatewayConfig.GetEffectiveConfigAsync(cancellationToken);
        var configured = !string.IsNullOrWhiteSpace(cfg.PromotionalSenderId);
        return new BroadcastSmsStatusDto(configured, configured ? cfg.PromotionalSenderId : null);
    }

    public async Task<BroadcastPreviewDto> PreviewAsync(BroadcastComposeRequest request, CancellationToken cancellationToken = default)
    {
        var outcome = await ComposeAsync(request, send: false, cancellationToken);
        return outcome.ToPreview();
    }

    public async Task<BroadcastSendResultDto> SendAsync(BroadcastComposeRequest request, CancellationToken cancellationToken = default)
    {
        var preview = await ComposeAsync(request, send: true, cancellationToken);
        return new BroadcastSendResultDto(
            preview.CampaignId,
            preview.RecipientCount,
            preview.SkippedCount,
            preview.EstimatedSegments,
            preview.ScheduledAt,
            preview.Status);
    }

    public async Task<BroadcastHistoryPageDto> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var p = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 100);
        var query = db.BroadcastCampaigns.AsNoTracking().OrderByDescending(c => c.CreatedAt);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new BroadcastHistoryPageDto(rows.Select(MapCampaign).ToList(), total, p, size);
    }

    public async Task<BroadcastCampaignDetailDto> GetAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var campaign = await db.BroadcastCampaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(BroadcastCampaign), campaignId.ToString());
        var recipients = await db.BroadcastRecipients.AsNoTracking()
            .Where(r => r.CampaignId == campaignId)
            .OrderBy(r => r.StudentName)
            .ToListAsync(cancellationToken);
        var outboxIds = recipients.Where(r => r.OutboxId.HasValue).Select(r => r.OutboxId!.Value).ToList();
        var outboxes = outboxIds.Count == 0
            ? new Dictionary<Guid, NotificationOutbox>()
            : await db.NotificationOutboxes.AsNoTracking()
                .Where(o => outboxIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, cancellationToken);

        var mapped = recipients.Select(r =>
        {
            outboxes.TryGetValue(r.OutboxId ?? Guid.Empty, out var outbox);
            return new BroadcastRecipientDto(
                r.Id,
                r.StudentId,
                r.StudentName,
                r.Recipient,
                r.Channel.ToString(),
                (outbox?.Status ?? r.Status).ToString(),
                outbox?.SmsSegments ?? r.SmsSegments,
                outbox?.StatusReason ?? r.StatusReason);
        }).ToList();

        return new BroadcastCampaignDetailDto(MapCampaign(campaign), mapped);
    }

    private async Task<ComposeOutcome> ComposeAsync(
        BroadcastComposeRequest request,
        bool send,
        CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        await EnsureBroadcastEnabledAsync(tenantId, cancellationToken);
        await EnsureChannelAsync(tenantId, request.Channel, cancellationToken);

        var message = request.Message?.Trim() ?? string.Empty;
        if (message.Length is < 1 or > 1000)
        {
            throw new DomainValidationException("MESSAGE_REQUIRED", "Write a message between 1 and 1000 characters.");
        }

        var language = NotificationCatalog.NormalizeLanguage(request.Language);
        var subjectTemplate = string.IsNullOrWhiteSpace(request.Subject)
            ? "Announcement from {instituteName}"
            : request.Subject.Trim();
        var bodyTemplate = string.IsNullOrWhiteSpace(request.BodyTemplate)
            ? "{instituteName}: {message}"
            : request.BodyTemplate.Trim();
        if (subjectTemplate.Length > 250 || bodyTemplate.Length > 2000)
        {
            throw new DomainValidationException("TEMPLATE_TOO_LONG", "Keep the subject under 250 characters and the template under 2000.");
        }

        var scheduledAt = request.ScheduledAt ?? DateTimeOffset.UtcNow;
        if (scheduledAt < DateTimeOffset.UtcNow.AddMinutes(-5))
        {
            scheduledAt = DateTimeOffset.UtcNow;
        }

        if (scheduledAt > DateTimeOffset.UtcNow.AddDays(90))
        {
            throw new DomainValidationException("SCHEDULE_TOO_FAR", "Schedule the broadcast within the next 90 days.");
        }

        var members = await ResolveAudienceAsync(tenantId, request, cancellationToken);
        if (members.Count > MaxAudience)
        {
            throw new DomainValidationException("AUDIENCE_TOO_LARGE", $"A broadcast can include at most {MaxAudience} students.");
        }

        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(t => t.Id == tenantId, cancellationToken);
        var reachable = new List<(AudienceMember Member, string Body, int SegmentCount)>();
        var skipped = new List<BroadcastSkipDto>();
        foreach (var member in members)
        {
            if (member.SkipReason is not null)
            {
                skipped.Add(new BroadcastSkipDto(member.Student.Id, member.Student.FullName, member.SkipReason));
                continue;
            }

            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["studentName"] = member.Student.FullName,
                ["className"] = member.ClassName,
                ["message"] = message,
                ["instituteName"] = tenant.Name
            };
            var body = NotificationTemplateRenderer.Render(bodyTemplate, variables);
            if (body.Length > 2000)
            {
                throw new DomainValidationException("MESSAGE_TOO_LONG", "The rendered message is longer than 2000 characters.");
            }

            var calculation = request.Channel == NotificationChannel.Sms
                ? segments.Calculate(body)
                : segments.Calculate(string.Empty);
            reachable.Add((member, body, request.Channel == NotificationChannel.Sms ? calculation.Segments : 0));
        }

        var estimated = reachable.Sum(r => r.SegmentCount);
        var sampleBody = reachable.Count == 0
            ? (request.Channel == NotificationChannel.Sms ? message : string.Empty)
            : reachable[0].Body;
        var sampleCalc = segments.Calculate(sampleBody);
        var quota = await notifications.GetSmsQuotaAsync(tenantId, null, cancellationToken);
        var quiet = await QuietHoursAsync(tenantId, request.Channel, scheduledAt, cancellationToken);
        var promotionalConfigured = smsGatewayConfig is not null
            && !string.IsNullOrWhiteSpace((await smsGatewayConfig.GetEffectiveConfigAsync(cancellationToken)).PromotionalSenderId);

        if (!send)
        {
            return new ComposeOutcome(
                Guid.Empty,
                reachable.Count,
                skipped.Count,
                estimated,
                sampleCalc.Segments,
                sampleBody,
                sampleCalc.Encoding,
                sampleCalc.IsUnicode,
                sampleCalc.PerSegmentLimit,
                quota.UsedSegments,
                quota.MonthlyQuota,
                quota.RemainingSegments,
                quota.MonthlyQuota.HasValue && quota.UsedSegments + estimated > quota.MonthlyQuota.Value,
                quota.OverQuotaPolicy,
                quiet.Active,
                quiet.ResumeAt,
                skipped.Take(8).ToList(),
                scheduledAt,
                string.Empty,
                promotionalConfigured);
        }

        if (reachable.Count == 0)
        {
            throw new DomainValidationException("NO_RECIPIENTS", "Nobody in this audience can receive the selected channel.");
        }

        var status = scheduledAt > DateTimeOffset.UtcNow.AddSeconds(30)
            ? BroadcastCampaignStatus.Scheduled
            : BroadcastCampaignStatus.Queued;
        var campaign = new BroadcastCampaign
        {
            TenantId = tenantId,
            Title = message.Length <= 80 ? message : message[..80],
            AudienceKind = request.Audience,
            AudienceId = request.AudienceId,
            Channel = request.Channel,
            Language = language,
            Subject = subjectTemplate,
            BodyTemplate = bodyTemplate,
            Message = message,
            ScheduledAt = scheduledAt,
            Status = status,
            RecipientCount = reachable.Count,
            SkippedCount = skipped.Count,
            EstimatedSegments = estimated
        };
        db.BroadcastCampaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var item in reachable)
        {
            var result = await notifications.EnqueueAsync(
                new EnqueueNotificationRequest(
                    MessageType: NotificationMessageType.Broadcast,
                    RecipientPhone: item.Member.Phone,
                    RecipientEmail: item.Member.Email,
                    RecipientName: item.Member.Student.GuardianName,
                    StudentId: item.Member.Student.Id,
                    StudentName: item.Member.Student.FullName,
                    StudentCode: item.Member.Student.StudentCode,
                    Language: language,
                    IdempotencyKey: $"{campaign.Id:N}:{item.Member.Student.Id:N}",
                    Variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["message"] = message,
                        ["className"] = item.Member.ClassName,
                        ["studentName"] = item.Member.Student.FullName,
                        ["instituteName"] = tenant.Name
                    },
                    ChannelsOverride: [request.Channel],
                    ScheduledAtUtc: scheduledAt,
                    SubjectOverride: subjectTemplate,
                    BodyOverride: bodyTemplate),
                tenantId,
                cancellationToken);

            var outbox = result.Messages.FirstOrDefault();
            db.BroadcastRecipients.Add(new BroadcastRecipient
            {
                TenantId = tenantId,
                CampaignId = campaign.Id,
                StudentId = item.Member.Student.Id,
                OutboxId = outbox?.Id,
                StudentName = item.Member.Student.FullName,
                Recipient = request.Channel == NotificationChannel.Email
                    ? item.Member.Email ?? string.Empty
                    : item.Member.Phone ?? string.Empty,
                Channel = request.Channel,
                Status = outbox is null ? NotificationStatus.Skipped : NotificationStatus.Queued,
                SmsSegments = item.SegmentCount,
                StatusReason = outbox is null ? result.SkippedReason ?? "Skipped:FeatureOff" : null
            });
        }

        foreach (var skip in skipped)
        {
            db.BroadcastRecipients.Add(new BroadcastRecipient
            {
                TenantId = tenantId,
                CampaignId = campaign.Id,
                StudentId = skip.StudentId,
                StudentName = skip.StudentName,
                Recipient = string.Empty,
                Channel = request.Channel,
                Status = NotificationStatus.Skipped,
                StatusReason = skip.Reason
            });
        }

        await audit.LogAsync(
            "Broadcast.Queued",
            nameof(BroadcastCampaign),
            campaign.Id.ToString(),
            new { request.Audience, request.Channel, campaign.RecipientCount, campaign.EstimatedSegments, campaign.ScheduledAt },
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new ComposeOutcome(
            campaign.Id,
            reachable.Count,
            skipped.Count,
            estimated,
            sampleCalc.Segments,
            sampleBody,
            sampleCalc.Encoding,
            sampleCalc.IsUnicode,
            sampleCalc.PerSegmentLimit,
            quota.UsedSegments,
            quota.MonthlyQuota,
            quota.RemainingSegments,
            quota.MonthlyQuota.HasValue && quota.UsedSegments + estimated > quota.MonthlyQuota.Value,
            quota.OverQuotaPolicy,
            quiet.Active,
            quiet.ResumeAt,
            skipped.Take(8).ToList(),
            scheduledAt,
            status.ToString(),
            promotionalConfigured);
    }

    private async Task<List<AudienceMember>> ResolveAudienceAsync(
        Guid tenantId,
        BroadcastComposeRequest request,
        CancellationToken cancellationToken)
    {
        IQueryable<Student> query = db.Students.Where(s => s.Status == StudentStatus.Active);
        switch (request.Audience)
        {
            case BroadcastAudienceKind.All:
                break;
            case BroadcastAudienceKind.Level:
                var levelId = RequireAudienceId(request.AudienceId, "Choose an education level.");
                if (!await db.EducationLevels.AnyAsync(l => l.Id == levelId && !l.IsDeleted, cancellationToken))
                {
                    throw new EntityNotFoundException(nameof(EducationLevel), levelId.ToString());
                }

                query = query.Where(s => s.EducationLevelId == levelId);
                break;
            case BroadcastAudienceKind.Batch:
                var batchId = RequireAudienceId(request.AudienceId, "Choose a batch.");
                if (!await db.AcademicBatches.AnyAsync(b => b.Id == batchId, cancellationToken))
                {
                    throw new EntityNotFoundException(nameof(AcademicBatch), batchId.ToString());
                }

                var batchStudents = await db.StudentEnrollments
                    .Where(e => e.BatchId == batchId && e.IsActive)
                    .Select(e => e.StudentId)
                    .ToListAsync(cancellationToken);
                query = query.Where(s => batchStudents.Contains(s.Id));
                break;
            case BroadcastAudienceKind.BatchGroup:
                await EnsureFeatureAsync(tenantId, FeatureKey.BatchGroups, cancellationToken);
                var groupId = RequireAudienceId(request.AudienceId, "Choose a batch group.");
                if (!await db.BatchGroups.AnyAsync(g => g.Id == groupId, cancellationToken))
                {
                    throw new EntityNotFoundException(nameof(BatchGroup), groupId.ToString());
                }

                var groupBatches = await db.AcademicBatches
                    .Where(b => b.BatchGroupId == groupId)
                    .Select(b => b.Id)
                    .ToListAsync(cancellationToken);
                var groupStudents = await db.StudentEnrollments
                    .Where(e => groupBatches.Contains(e.BatchId) && e.IsActive)
                    .Select(e => e.StudentId)
                    .ToListAsync(cancellationToken);
                query = query.Where(s => groupStudents.Contains(s.Id));
                break;
            case BroadcastAudienceKind.SelectedStudents:
                var ids = (request.StudentIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
                if (ids.Count == 0)
                {
                    throw new DomainValidationException("AUDIENCE_REQUIRED", "Select at least one student.");
                }

                query = query.Where(s => ids.Contains(s.Id));
                var selected = await query.OrderBy(s => s.FullName).ToListAsync(cancellationToken);
                if (selected.Count != ids.Count)
                {
                    throw new DomainValidationException("MISSING_STUDENTS", "One or more selected students are not active in this institute.");
                }

                return await DecorateAsync(selected, request.Channel, cancellationToken);
            case BroadcastAudienceKind.FeeOverdue:
                var today = await ColomboTodayAsync(tenantId, cancellationToken);
                var overdueIds = await db.FeeInvoices
                    .Where(i => (i.Status == FeeInvoiceStatus.Pending || i.Status == FeeInvoiceStatus.PartiallyPaid)
                                && i.BalanceAmount > 0
                                && i.DueDate < today)
                    .Select(i => i.StudentId)
                    .Distinct()
                    .ToListAsync(cancellationToken);
                query = query.Where(s => overdueIds.Contains(s.Id));
                break;
            default:
                throw new DomainValidationException("AUDIENCE_INVALID", "Choose an audience.");
        }

        var students = await query.OrderBy(s => s.FullName).ToListAsync(cancellationToken);
        return await DecorateAsync(students, request.Channel, cancellationToken);
    }

    private async Task<List<AudienceMember>> DecorateAsync(
        List<Student> students,
        NotificationChannel channel,
        CancellationToken cancellationToken)
    {
        var studentIds = students.Select(s => s.Id).ToList();
        var levelIds = students.Select(s => s.EducationLevelId).Distinct().ToList();
        var levels = await db.EducationLevels.AsNoTracking()
            .Where(l => levelIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        var enrollments = await db.StudentEnrollments.AsNoTracking()
            .Where(e => studentIds.Contains(e.StudentId) && e.IsActive)
            .ToListAsync(cancellationToken);
        var batchIds = enrollments.Select(e => e.BatchId).Distinct().ToList();
        var batches = await db.AcademicBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        var classNames = enrollments
            .GroupBy(e => e.StudentId)
            .ToDictionary(
                g => g.Key,
                g => batches.TryGetValue(g.First().BatchId, out var name) ? name : string.Empty);

        Dictionary<Guid, string> emails = new();
        if (channel == NotificationChannel.Email)
        {
            var links = await db.ParentStudentLinks.AsNoTracking()
                .Where(l => studentIds.Contains(l.StudentId))
                .ToListAsync(cancellationToken);
            var parentIds = links.Select(l => l.ParentUserId).Distinct().ToList();
            var parents = await db.Users.AsNoTracking()
                .Where(u => parentIds.Contains(u.Id) && u.IsActive)
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(cancellationToken);
            var emailByParent = parents.ToDictionary(p => p.Id, p => p.Email);
            foreach (var link in links)
            {
                if (emails.ContainsKey(link.StudentId))
                {
                    continue;
                }

                if (emailByParent.TryGetValue(link.ParentUserId, out var email)
                    && !PhoneNumber.IsPlaceholderEmail(email)
                    && email.Contains('@'))
                {
                    emails[link.StudentId] = email;
                }
            }
        }

        return students.Select(student =>
        {
            var className = classNames.TryGetValue(student.Id, out var batchName) && !string.IsNullOrWhiteSpace(batchName)
                ? batchName
                : levels.GetValueOrDefault(student.EducationLevelId) ?? string.Empty;
            string? phone = string.IsNullOrWhiteSpace(student.GuardianPhone) ? null : student.GuardianPhone.Trim();
            emails.TryGetValue(student.Id, out var email);
            string? skip = channel switch
            {
                NotificationChannel.Email when string.IsNullOrWhiteSpace(email) => "NoEmail",
                NotificationChannel.Sms or NotificationChannel.WhatsApp when phone is null => "NoPhone",
                _ => null
            };
            return new AudienceMember(student, className, phone, email, skip);
        }).ToList();
    }

    private async Task EnsureBroadcastEnabledAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await EnsureFeatureAsync(tenantId, FeatureKey.BroadcastMessages, cancellationToken);
        var setting = await db.TenantNotificationTypeSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MessageType == NotificationMessageType.Broadcast, cancellationToken);
        if (setting is { IsEnabled: false })
        {
            throw new FeatureDisabledException(FeatureKey.BroadcastMessages);
        }
    }

    private async Task EnsureChannelAsync(Guid tenantId, NotificationChannel channel, CancellationToken cancellationToken)
    {
        if (channel is not (NotificationChannel.Sms or NotificationChannel.Email or NotificationChannel.WhatsApp))
        {
            throw new DomainValidationException("CHANNEL_INVALID", "Choose SMS, email, or WhatsApp.");
        }

        if (channel == NotificationChannel.WhatsApp)
        {
            await EnsureFeatureAsync(tenantId, FeatureKey.WhatsAppMessaging, cancellationToken);
        }
        else if (channel == NotificationChannel.Email)
        {
            await EnsureFeatureAsync(tenantId, FeatureKey.EmailNotifications, cancellationToken);
        }

        var setting = await db.TenantNotificationTypeSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.MessageType == NotificationMessageType.Broadcast, cancellationToken);
        var allowed = channel switch
        {
            NotificationChannel.Sms => setting?.SmsEnabled ?? true,
            NotificationChannel.Email => setting?.EmailEnabled ?? false,
            NotificationChannel.WhatsApp => setting?.WhatsAppEnabled ?? false,
            _ => false
        };
        if (!allowed)
        {
            throw new DomainValidationException(
                "CHANNEL_DISABLED",
                "Turn on this channel for broadcast messages under Features and notifications.");
        }
    }

    private async Task EnsureFeatureAsync(Guid tenantId, FeatureKey key, CancellationToken cancellationToken)
    {
        var evaluation = await features.EvaluateAsync(tenantId, key, cancellationToken);
        if (!evaluation.IsInPlan)
        {
            throw new FeatureNotInPlanException(key);
        }

        if (!evaluation.IsEnabled)
        {
            throw new FeatureDisabledException(key);
        }
    }

    private async Task<(bool Active, DateTimeOffset? ResumeAt)> QuietHoursAsync(
        Guid tenantId,
        NotificationChannel channel,
        DateTimeOffset scheduledAt,
        CancellationToken cancellationToken)
    {
        if (channel != NotificationChannel.Sms)
        {
            return (false, null);
        }

        var config = await db.TenantNotificationConfigs.AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.TimeZone)
            .FirstAsync(cancellationToken);
        var enabled = config?.QuietHoursEnabled ?? true;
        var start = config?.QuietHoursStartMinutes ?? 21 * 60;
        var end = config?.QuietHoursEndMinutes ?? 6 * 60;
        if (!enabled || !QuietHoursCalculator.IsWithinQuietHours(scheduledAt, tenant, start, end))
        {
            return (false, null);
        }

        return (true, QuietHoursCalculator.GetQuietHoursEndUtc(scheduledAt, tenant, start, end));
    }

    private async Task<DateOnly> ColomboTodayAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var zoneName = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.TimeZone)
            .FirstAsync(cancellationToken);
        var zone = QuietHoursCalculator.ResolveTimeZone(zoneName);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    private static Guid RequireAudienceId(Guid? id, string message)
    {
        if (id is null || id == Guid.Empty)
        {
            throw new DomainValidationException("AUDIENCE_REQUIRED", message);
        }

        return id.Value;
    }

    private static BroadcastCampaignDto MapCampaign(BroadcastCampaign campaign) =>
        new(
            campaign.Id,
            campaign.Title,
            campaign.AudienceKind.ToString(),
            campaign.Channel.ToString(),
            campaign.Language,
            campaign.Subject,
            campaign.BodyTemplate,
            campaign.Message,
            campaign.ScheduledAt,
            campaign.Status.ToString(),
            campaign.RecipientCount,
            campaign.SkippedCount,
            campaign.EstimatedSegments,
            campaign.CreatedAt);

    private sealed record AudienceMember(
        Student Student,
        string ClassName,
        string? Phone,
        string? Email,
        string? SkipReason);

    private sealed record ComposeOutcome(
        Guid CampaignId,
        int RecipientCount,
        int SkippedCount,
        int EstimatedSegments,
        int SampleSegments,
        string SampleBody,
        string Encoding,
        bool IsUnicode,
        int CharacterLimit,
        int UsedSegments,
        int? MonthlyQuota,
        int RemainingSegments,
        bool WouldExceedQuota,
        string OverQuotaPolicy,
        bool QuietHoursActive,
        DateTimeOffset? QuietHoursResumeAt,
        IReadOnlyList<BroadcastSkipDto> Skipped,
        DateTimeOffset ScheduledAt,
        string Status,
        bool PromotionalSenderIdConfigured)
    {
        public BroadcastPreviewDto ToPreview() => new(
            RecipientCount,
            SkippedCount,
            EstimatedSegments,
            SampleSegments,
            SampleBody,
            Encoding,
            IsUnicode,
            CharacterLimit,
            UsedSegments,
            MonthlyQuota,
            RemainingSegments,
            WouldExceedQuota,
            OverQuotaPolicy,
            QuietHoursActive,
            QuietHoursResumeAt,
            Skipped,
            PromotionalSenderIdConfigured);
    }
}
