using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Notifications;

public sealed class NotificationService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    IAuditService auditService,
    ISmsSegmentCalculator segmentCalculator,
    ILimitService limitService,
    ISmsProvider smsProvider,
    IEmailSender emailSender,
    IWhatsAppProvider whatsAppProvider,
    ISmsGatewayConfigProvider? smsGatewayConfig = null) : INotificationService, INotificationDispatcher
{
    public async Task<NotificationSettingsWorkspaceDto> GetSettingsWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var configDto = await GetConfigDtoAsync(tenantId, cancellationToken);
        var quotaDto = await GetSmsQuotaAsync(tenantId, null, cancellationToken);
        var allowedChannels = await GetAllowedChannelsAsync(tenantId, cancellationToken);
        var messageTypes = await GetMessageTypeConfigsAsync(tenantId, cancellationToken);
        var templates = await GetTemplatesInternalAsync(tenantId, null, null, cancellationToken);
        var senderInfo = await GetTenantSmsSenderIdInfoAsync(tenantId, cancellationToken);
        var promotionalConfigured = smsGatewayConfig is not null
            && !string.IsNullOrWhiteSpace((await smsGatewayConfig.GetEffectiveConfigAsync(cancellationToken)).PromotionalSenderId);

        return new NotificationSettingsWorkspaceDto(
            configDto,
            quotaDto,
            allowedChannels,
            messageTypes,
            templates,
            NotificationCatalog.SampleVariables,
            senderInfo.SmsSenderId,
            senderInfo.CanCustomizeSmsSenderId,
            promotionalConfigured);
    }

    public async Task<TenantNotificationConfigDto> UpdateConfigAsync(
        UpdateNotificationConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);

        if (request.QuietHoursStartMinutes is < 0 or >= 1440 || request.QuietHoursEndMinutes is < 0 or >= 1440)
        {
            throw new DomainValidationException("INVALID_QUIET_HOURS", "Quiet hours must be between 00:00 and 23:59.");
        }

        if (request.FeeReminderDaysBefore is < 0 or > 60
            || request.FeeReminderDaysAfterOverdue is < 0 or > 90
            || request.ExamReminderDaysBefore is < 0 or > 60
            || request.ExamReminderHoursBefore is < 0 or > 23)
        {
            throw new DomainValidationException("INVALID_REMINDER_DAYS", "Reminder days must be within valid bounds.");
        }

        var normalizedLang = NotificationCatalog.NormalizeLanguage(request.DefaultLanguage);

        var config = await db.TenantNotificationConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null)
        {
            config = new TenantNotificationConfig
            {
                TenantId = tenantId
            };
            db.TenantNotificationConfigs.Add(config);
        }

        config.QuietHoursEnabled = request.QuietHoursEnabled;
        config.QuietHoursStartMinutes = request.QuietHoursStartMinutes;
        config.QuietHoursEndMinutes = request.QuietHoursEndMinutes;
        config.FeeReminderDaysBefore = request.FeeReminderDaysBefore;
        config.FeeReminderOnDueDate = request.FeeReminderOnDueDate;
        config.FeeReminderDaysAfterOverdue = request.FeeReminderDaysAfterOverdue;
        config.ExamReminderDaysBefore = request.ExamReminderDaysBefore;
        config.ExamReminderHoursBefore = request.ExamReminderHoursBefore;
        config.SmsOverQuotaPolicy = request.SmsOverQuotaPolicy;
        config.DefaultLanguage = normalizedLang;

        await auditService.LogAsync(
            action: "NotificationConfig.Updated",
            entityName: nameof(TenantNotificationConfig),
            entityId: config.Id.ToString(),
            details: new
            {
                request.QuietHoursEnabled,
                request.QuietHoursStartMinutes,
                request.QuietHoursEndMinutes,
                request.FeeReminderDaysBefore,
                request.FeeReminderOnDueDate,
                request.FeeReminderDaysAfterOverdue,
                request.ExamReminderDaysBefore,
                request.ExamReminderHoursBefore,
                SmsOverQuotaPolicy = request.SmsOverQuotaPolicy.ToString(),
                DefaultLanguage = normalizedLang
            },
            tenantIdOverride: tenantId,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetConfigDtoAsync(tenantId, cancellationToken);
    }

    public async Task<NotificationTypeConfigDto> UpdateTypeSettingAsync(
        NotificationMessageType messageType,
        UpdateNotificationTypeSettingRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var definition = NotificationCatalog.GetDefinition(messageType);

        if (definition.ParentFeatureKey.HasValue)
        {
            var parentEval = await featureService.EvaluateAsync(tenantId, definition.ParentFeatureKey.Value, cancellationToken);
            if (!parentEval.IsInPlan && request.IsEnabled)
            {
                throw new FeatureNotInPlanException(definition.ParentFeatureKey.Value);
            }
        }

        if (request.EmailEnabled)
        {
            var emailEval = await featureService.EvaluateAsync(tenantId, FeatureKey.EmailNotifications, cancellationToken);
            if (!emailEval.IsInPlan)
            {
                throw new FeatureNotInPlanException(FeatureKey.EmailNotifications);
            }
        }

        if (request.WhatsAppEnabled)
        {
            var whatsAppEval = await featureService.EvaluateAsync(tenantId, FeatureKey.WhatsAppMessaging, cancellationToken);
            if (!whatsAppEval.IsInPlan)
            {
                throw new FeatureNotInPlanException(FeatureKey.WhatsAppMessaging);
            }
        }

        var setting = await db.TenantNotificationTypeSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.MessageType == messageType, cancellationToken);

        var previousEnabled = setting?.IsEnabled ?? true;

        if (setting is null)
        {
            setting = new TenantNotificationTypeSetting
            {
                TenantId = tenantId,
                MessageType = messageType
            };
            db.TenantNotificationTypeSettings.Add(setting);
        }

        setting.IsEnabled = request.IsEnabled;
        setting.SmsEnabled = request.SmsEnabled;
        setting.EmailEnabled = request.EmailEnabled;
        setting.WhatsAppEnabled = request.WhatsAppEnabled;

        await auditService.LogAsync(
            action: "NotificationTypeSwitch.Changed",
            entityName: nameof(TenantNotificationTypeSetting),
            entityId: setting.Id.ToString(),
            details: new
            {
                MessageType = messageType.ToString(),
                PreviousEnabled = previousEnabled,
                NewEnabled = request.IsEnabled,
                request.SmsEnabled,
                request.EmailEnabled,
                request.WhatsAppEnabled
            },
            tenantIdOverride: tenantId,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var allConfigs = await GetMessageTypeConfigsAsync(tenantId, cancellationToken);
        return allConfigs.First(c => c.MessageType == messageType.ToString());
    }

    public async Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesAsync(
        NotificationMessageType? messageType = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        return await GetTemplatesInternalAsync(tenantId, messageType, language, cancellationToken);
    }

    public async Task<NotificationTemplateDto> UpdateTemplateAsync(
        NotificationMessageType messageType,
        string language,
        UpdateNotificationTemplateRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var normalizedLang = NotificationCatalog.NormalizeLanguage(language);

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            throw new DomainValidationException("TEMPLATE_BODY_REQUIRED", "Template body cannot be empty.");
        }

        var defaultTemplate = NotificationCatalog.GetDefaultTemplate(messageType, normalizedLang);
        var subject = string.IsNullOrWhiteSpace(request.Subject) ? defaultTemplate.Subject : request.Subject.Trim();
        var body = request.Body.Trim();

        var existing = await db.NotificationTemplates
            .FirstOrDefaultAsync(
                t => t.TenantId == tenantId && t.MessageType == messageType && t.Language == normalizedLang,
                cancellationToken);

        if (existing is null)
        {
            existing = new NotificationTemplate
            {
                TenantId = tenantId,
                MessageType = messageType,
                Language = normalizedLang,
                Subject = subject,
                Body = body,
                IsCustom = true
            };
            db.NotificationTemplates.Add(existing);
        }
        else
        {
            existing.Subject = subject;
            existing.Body = body;
            existing.IsCustom = true;
        }

        await auditService.LogAsync(
            action: "NotificationTemplate.Updated",
            entityName: nameof(NotificationTemplate),
            entityId: existing.Id.ToString(),
            details: new
            {
                MessageType = messageType.ToString(),
                Language = normalizedLang
            },
            tenantIdOverride: tenantId,
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        var templates = await GetTemplatesInternalAsync(tenantId, messageType, normalizedLang, cancellationToken);
        return templates.First();
    }

    public async Task<NotificationTemplateDto> ResetTemplateAsync(
        NotificationMessageType messageType,
        string language,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var normalizedLang = NotificationCatalog.NormalizeLanguage(language);

        var existing = await db.NotificationTemplates
            .FirstOrDefaultAsync(
                t => t.TenantId == tenantId && t.MessageType == messageType && t.Language == normalizedLang,
                cancellationToken);

        if (existing is not null)
        {
            var defaultTemplate = NotificationCatalog.GetDefaultTemplate(messageType, normalizedLang);
            existing.Subject = defaultTemplate.Subject;
            existing.Body = defaultTemplate.Body;
            existing.IsCustom = false;

            await auditService.LogAsync(
                action: "NotificationTemplate.Reset",
                entityName: nameof(NotificationTemplate),
                entityId: existing.Id.ToString(),
                details: new
                {
                    MessageType = messageType.ToString(),
                    Language = normalizedLang
                },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        var templates = await GetTemplatesInternalAsync(tenantId, messageType, normalizedLang, cancellationToken);
        return templates.First();
    }

    public PreviewTemplateResponse PreviewTemplate(PreviewTemplateRequest request)
    {
        var renderedSubject = NotificationTemplateRenderer.Render(
            request.Subject ?? string.Empty,
            request.Variables,
            fallbackToSample: true);

        var renderedBody = NotificationTemplateRenderer.Render(
            request.Body ?? string.Empty,
            request.Variables,
            fallbackToSample: true);

        var calc = segmentCalculator.Calculate(renderedBody);
        return new PreviewTemplateResponse(renderedSubject, renderedBody, calc);
    }

    public async Task<SmsQuotaSummaryDto> GetSmsQuotaAsync(
        Guid? tenantIdOverride = null,
        DateTimeOffset? nowOverride = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantIdOverride ?? TenantGuard.Require(tenantContext);
        var now = nowOverride ?? DateTimeOffset.UtcNow;

        var config = await db.TenantNotificationConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.TenantId == tenantId, cancellationToken);

        var policy = config?.SmsOverQuotaPolicy ?? SmsOverQuotaPolicy.AllowAndRecordExtra;
        var monthlyQuota = await GetMonthlySmsQuotaLimitAsync(tenantId, cancellationToken);
        var (usedSegments, extraSegments) = await GetMonthlySmsUsageCountersAsync(tenantId, now, cancellationToken);

        var remaining = monthlyQuota.HasValue ? Math.Max(0, monthlyQuota.Value - usedSegments) : 0;
        var pct = monthlyQuota is > 0
            ? Math.Round(usedSegments * 100.0 / monthlyQuota.Value, 1)
            : 0.0;

        return new SmsQuotaSummaryDto(
            UsedSegments: usedSegments,
            MonthlyQuota: monthlyQuota,
            RemainingSegments: remaining,
            ExtraSegments: extraSegments,
            UsagePercentage: pct,
            IsWarning80Percent: monthlyQuota.HasValue && pct >= 80.0,
            IsExceeded100Percent: monthlyQuota.HasValue && usedSegments >= monthlyQuota.Value,
            OverQuotaPolicy: policy.ToString());
    }

    public async Task<EnqueueNotificationResultDto> EnqueueAsync(
        EnqueueNotificationRequest request,
        Guid? tenantIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantIdOverride ?? TenantGuard.Require(tenantContext);
        var definition = NotificationCatalog.GetDefinition(request.MessageType);

        if (definition.ParentFeatureKey.HasValue)
        {
            var isParentEnabled = await featureService.IsEnabledAsync(
                tenantId,
                definition.ParentFeatureKey.Value,
                cancellationToken);

            if (!isParentEnabled)
            {
                return new EnqueueNotificationResultDto(0, WasDuplicate: false, SkippedReason: "Skipped:FeatureOff", Messages: []);
            }
        }

        var typeSetting = await db.TenantNotificationTypeSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => !s.IsDeleted && s.TenantId == tenantId && s.MessageType == request.MessageType,
                cancellationToken);

        var isTypeEnabled = typeSetting?.IsEnabled ?? true;
        if (!isTypeEnabled)
        {
            return new EnqueueNotificationResultDto(0, WasDuplicate: false, SkippedReason: "Skipped:FeatureOff", Messages: []);
        }

        var smsSelected = typeSetting?.SmsEnabled ?? definition.DefaultSmsEnabled;
        var emailSelected = typeSetting?.EmailEnabled ?? definition.DefaultEmailEnabled;
        var whatsAppSelected = typeSetting?.WhatsAppEnabled ?? definition.DefaultWhatsAppEnabled;

        var candidateChannels = new List<NotificationChannel>();
        if (request.ChannelsOverride is { Count: > 0 })
        {
            foreach (var ch in request.ChannelsOverride.Distinct())
            {
                var allowedBySetting = ch switch
                {
                    NotificationChannel.Sms => smsSelected,
                    NotificationChannel.Email => emailSelected,
                    NotificationChannel.WhatsApp => whatsAppSelected,
                    _ => false
                };
                if (allowedBySetting)
                {
                    candidateChannels.Add(ch);
                }
            }
        }
        else
        {
            if (smsSelected) candidateChannels.Add(NotificationChannel.Sms);
            if (emailSelected) candidateChannels.Add(NotificationChannel.Email);
            if (whatsAppSelected) candidateChannels.Add(NotificationChannel.WhatsApp);
        }

        var activeChannels = new List<NotificationChannel>();
        foreach (var ch in candidateChannels)
        {
            var channelFeature = NotificationCatalog.GetChannelFeatureKey(ch);
            if (channelFeature.HasValue)
            {
                var channelEnabled = await featureService.IsEnabledAsync(tenantId, channelFeature.Value, cancellationToken);
                if (!channelEnabled)
                {
                    continue;
                }
            }

            activeChannels.Add(ch);
        }

        if (activeChannels.Count == 0)
        {
            return new EnqueueNotificationResultDto(0, WasDuplicate: false, SkippedReason: "Skipped:FeatureOff", Messages: []);
        }

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);

        var config = await db.TenantNotificationConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.TenantId == tenantId, cancellationToken);

        Student? student = null;
        if (request.StudentId.HasValue)
        {
            student = await db.Students
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => !s.IsDeleted && s.TenantId == tenantId && s.Id == request.StudentId.Value, cancellationToken);
        }

        var language = NotificationCatalog.NormalizeLanguage(request.Language ?? config?.DefaultLanguage ?? "en");
        var customTemplate = await db.NotificationTemplates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                t => !t.IsDeleted && t.TenantId == tenantId && t.MessageType == request.MessageType && t.Language == language && t.IsCustom,
                cancellationToken);

        var defaultTemplate = NotificationCatalog.GetDefaultTemplate(request.MessageType, language);
        var rawSubject = !string.IsNullOrWhiteSpace(request.SubjectOverride)
            ? request.SubjectOverride.Trim()
            : customTemplate?.Subject ?? defaultTemplate.Subject;
        var rawBody = !string.IsNullOrWhiteSpace(request.BodyOverride)
            ? request.BodyOverride.Trim()
            : customTemplate?.Body ?? defaultTemplate.Body;

        var mergedVariables = new Dictionary<string, string>(NotificationCatalog.SampleVariables, StringComparer.OrdinalIgnoreCase)
        {
            ["instituteName"] = tenant?.Name ?? "Admino Institute",
            ["date"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"),
            ["time"] = DateTimeOffset.UtcNow.ToString("hh:mm tt")
        };

        if (student is not null)
        {
            mergedVariables["studentName"] = student.FullName;
            mergedVariables["studentCode"] = student.StudentCode;
        }
        else if (!string.IsNullOrWhiteSpace(request.StudentName))
        {
            mergedVariables["studentName"] = request.StudentName!;
        }

        if (!string.IsNullOrWhiteSpace(request.StudentCode))
        {
            mergedVariables["studentCode"] = request.StudentCode!;
        }

        if (request.Variables is not null)
        {
            foreach (var kv in request.Variables)
            {
                mergedVariables[kv.Key] = kv.Value;
            }
        }

        var renderedSubject = NotificationTemplateRenderer.Render(rawSubject, mergedVariables, fallbackToSample: true);
        var renderedBody = NotificationTemplateRenderer.Render(rawBody, mergedVariables, fallbackToSample: true);

        var phone = !string.IsNullOrWhiteSpace(request.RecipientPhone)
            ? request.RecipientPhone!.Trim()
            : student?.GuardianPhone ?? tenant?.ContactPhone ?? "+94771234567";

        var email = !string.IsNullOrWhiteSpace(request.RecipientEmail)
            ? request.RecipientEmail!.Trim()
            : tenant?.ContactEmail ?? "admin@demo.admino.lk";

        var recipientName = request.RecipientName ?? student?.GuardianName ?? tenant?.Name;
        var studentName = student?.FullName ?? request.StudentName;
        var studentCode = student?.StudentCode ?? request.StudentCode;
        var baseIdempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? Guid.NewGuid().ToString("N")
            : request.IdempotencyKey.Trim();

        var createdOrExisting = new List<NotificationOutbox>();
        var wasDuplicate = false;
        var enqueuedCount = 0;

        using (tenantContext.BeginSystemScope(tenantId))
        {
            foreach (var channel in activeChannels)
            {
                var existing = await db.NotificationOutboxes
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(
                        o => !o.IsDeleted
                             && o.TenantId == tenantId
                             && o.Channel == channel
                             && o.IdempotencyKey == baseIdempotencyKey,
                        cancellationToken);

                if (existing is not null)
                {
                    wasDuplicate = true;
                    createdOrExisting.Add(existing);
                    continue;
                }

                var recipient = channel == NotificationChannel.Email ? email : phone;
                var smsSegments = channel == NotificationChannel.Sms
                    ? segmentCalculator.Calculate(renderedBody).Segments
                    : 0;

                var outbox = new NotificationOutbox
                {
                    TenantId = tenantId,
                    MessageType = request.MessageType,
                    Channel = channel,
                    Status = NotificationStatus.Queued,
                    Language = language,
                    Recipient = recipient,
                    RecipientName = recipientName,
                    StudentId = student?.Id ?? request.StudentId,
                    StudentName = studentName,
                    StudentCode = studentCode,
                    Subject = renderedSubject,
                    Body = renderedBody,
                    IdempotencyKey = baseIdempotencyKey,
                    SmsSegments = smsSegments,
                    ExtraSegments = 0,
                    AttemptCount = 0,
                    MaxAttempts = 5,
                    IsDeadLetter = false,
                    NextAttemptAt = request.ScheduledAtUtc ?? DateTimeOffset.UtcNow
                };

                db.NotificationOutboxes.Add(outbox);
                createdOrExisting.Add(outbox);
                enqueuedCount++;
            }

            if (enqueuedCount > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return new EnqueueNotificationResultDto(
            EnqueuedCount: enqueuedCount,
            WasDuplicate: wasDuplicate,
            SkippedReason: null,
            Messages: createdOrExisting.Select(MapMessageDto).ToList());
    }

    public async Task<DispatchResultDto> DispatchPendingAsync(
        DateTimeOffset? nowOverride = null,
        Guid? tenantIdFilter = null,
        CancellationToken cancellationToken = default)
    {
        var now = nowOverride ?? DateTimeOffset.UtcNow;

        using (tenantContext.BeginSystemScope(tenantIdFilter))
        {
            var query = db.NotificationOutboxes
                .IgnoreQueryFilters()
                .Where(o => !o.IsDeleted
                            && !o.IsDeadLetter
                            && (o.Status == NotificationStatus.Queued || o.Status == NotificationStatus.Failed));

            if (tenantIdFilter.HasValue && tenantIdFilter.Value != Guid.Empty)
            {
                query = query.Where(o => o.TenantId == tenantIdFilter.Value);
            }

            var candidates = await query
                .OrderBy(o => o.CreatedAt)
                .ToListAsync(cancellationToken);

            var dueItems = candidates
                .Where(o => o.NextAttemptAt <= now)
                .ToList();

            var processedCount = 0;
            var sentCount = 0;
            var skippedCount = 0;
            var failedCount = 0;
            var deferredCount = 0;
            var deadLetterCount = 0;

            foreach (var tenantGroup in dueItems.GroupBy(o => o.TenantId))
            {
                var tenantId = tenantGroup.Key;
                var tenant = await db.Tenants
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);

                var timeZoneId = tenant?.TimeZone ?? QuietHoursCalculator.DefaultTimeZoneId;

                var config = await db.TenantNotificationConfigs
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(c => !c.IsDeleted && c.TenantId == tenantId, cancellationToken);

                var quietHoursEnabled = config?.QuietHoursEnabled ?? true;
                var quietStart = config?.QuietHoursStartMinutes ?? 21 * 60;
                var quietEnd = config?.QuietHoursEndMinutes ?? 6 * 60;
                var overQuotaPolicy = config?.SmsOverQuotaPolicy ?? SmsOverQuotaPolicy.AllowAndRecordExtra;

                var typeSettings = await db.TenantNotificationTypeSettings
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => !s.IsDeleted && s.TenantId == tenantId)
                    .ToDictionaryAsync(s => s.MessageType, cancellationToken);

                var monthlyQuota = await GetMonthlySmsQuotaLimitAsync(tenantId, cancellationToken);
                var (runningUsedSegments, _) = await GetMonthlySmsUsageCountersAsync(tenantId, now, cancellationToken);

                foreach (var item in tenantGroup)
                {
                    processedCount++;
                    var definition = NotificationCatalog.GetDefinition(item.MessageType);

                    var isFeatureAllowed = true;
                    if (definition.ParentFeatureKey.HasValue)
                    {
                        isFeatureAllowed = await featureService.IsEnabledAsync(
                            tenantId,
                            definition.ParentFeatureKey.Value,
                            cancellationToken);
                    }

                    if (isFeatureAllowed)
                    {
                        var channelFeature = NotificationCatalog.GetChannelFeatureKey(item.Channel);
                        if (channelFeature.HasValue)
                        {
                            isFeatureAllowed = await featureService.IsEnabledAsync(
                                tenantId,
                                channelFeature.Value,
                                cancellationToken);
                        }
                    }

                    if (isFeatureAllowed && typeSettings.TryGetValue(item.MessageType, out var ts))
                    {
                        if (!ts.IsEnabled)
                        {
                            isFeatureAllowed = false;
                        }
                        else
                        {
                            isFeatureAllowed = item.Channel switch
                            {
                                NotificationChannel.Sms => ts.SmsEnabled,
                                NotificationChannel.Email => ts.EmailEnabled,
                                NotificationChannel.WhatsApp => ts.WhatsAppEnabled,
                                _ => false
                            };
                        }
                    }

                    if (!isFeatureAllowed)
                    {
                        item.Status = NotificationStatus.Skipped;
                        item.StatusReason = "Skipped:FeatureOff";
                        item.LastAttemptAt = now;
                        skippedCount++;
                        continue;
                    }

                    if (item.Channel == NotificationChannel.Sms)
                    {
                        var phoneCheck = SriLankanPhoneNormalizer.NormalizeForGateway(item.Recipient);
                        if (!phoneCheck.IsValid)
                        {
                            item.Status = NotificationStatus.Skipped;
                            item.StatusReason = $"Skipped:InvalidNumber:{phoneCheck.ErrorMessage}";
                            item.LastAttemptAt = now;
                            skippedCount++;
                            continue;
                        }

                        if (!definition.ExemptFromQuietHours
                            && quietHoursEnabled
                            && QuietHoursCalculator.IsWithinQuietHours(now, timeZoneId, quietStart, quietEnd))
                        {
                            item.Status = NotificationStatus.Queued;
                            item.StatusReason = "Deferred:QuietHours";
                            item.NextAttemptAt = QuietHoursCalculator.GetQuietHoursEndUtc(now, timeZoneId, quietStart, quietEnd);
                            deferredCount++;
                            continue;
                        }

                        var extraSegments = 0;
                        if (monthlyQuota.HasValue)
                        {
                            var availableBefore = Math.Max(0, monthlyQuota.Value - runningUsedSegments);
                            extraSegments = Math.Max(0, item.SmsSegments - availableBefore);
                            if (extraSegments > 0 && overQuotaPolicy == SmsOverQuotaPolicy.Block)
                            {
                                item.Status = NotificationStatus.Skipped;
                                item.StatusReason = "Skipped:QuotaExceeded";
                                item.LastAttemptAt = now;
                                skippedCount++;
                                continue;
                            }
                        }

                        item.ExtraSegments = extraSegments;
                    }

                    item.LastAttemptAt = now;

                    var sendOutcome = await SendViaProviderAsync(item, definition, cancellationToken);
                    if (sendOutcome.IsSkipped)
                    {
                        item.Status = NotificationStatus.Skipped;
                        item.Provider = sendOutcome.Provider;
                        item.StatusReason = sendOutcome.SkipReason ?? "Skipped:TestMode";
                        skippedCount++;
                        continue;
                    }

                    item.AttemptCount++;
                    if (sendOutcome.Succeeded)
                    {
                        item.Status = NotificationStatus.Sent;
                        item.SentAt = now;
                        item.Provider = sendOutcome.Provider;
                        item.ProviderMessageId = sendOutcome.ProviderMessageId;
                        item.GatewaySegments = sendOutcome.GatewaySegments;
                        item.GatewayCost = sendOutcome.GatewayCost;
                        item.StatusReason = null;
                        item.IsDeadLetter = false;
                        sentCount++;

                        if (item.Channel == NotificationChannel.Sms)
                        {
                            runningUsedSegments += item.SmsSegments;
                        }
                    }
                    else
                    {
                        failedCount++;
                        var reason = string.IsNullOrWhiteSpace(sendOutcome.ErrorMessage)
                            ? "Provider delivery failed."
                            : sendOutcome.ErrorMessage;
                        if (item.AttemptCount >= item.MaxAttempts)
                        {
                            item.Status = NotificationStatus.Failed;
                            item.IsDeadLetter = true;
                            item.StatusReason = $"DeadLetter:{reason}";
                            deadLetterCount++;
                        }
                        else
                        {
                            item.Status = NotificationStatus.Failed;
                            item.IsDeadLetter = false;
                            var delaySeconds = (int)Math.Pow(2, item.AttemptCount) * 30;
                            item.NextAttemptAt = now.AddSeconds(delaySeconds);
                            item.StatusReason = reason;
                        }
                    }
                }
            }

            if (processedCount > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return new DispatchResultDto(
                ProcessedCount: processedCount,
                SentCount: sentCount,
                SkippedCount: skippedCount,
                FailedCount: failedCount,
                DeferredCount: deferredCount,
                DeadLetterCount: deadLetterCount);
        }
    }

    public async Task<NotificationMessageLogResponse> GetMessageLogAsync(
        NotificationMessageFilter filter,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);

        var query = db.NotificationOutboxes
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId);

        if (filter.MessageType.HasValue)
        {
            query = query.Where(o => o.MessageType == filter.MessageType.Value);
        }

        if (filter.Channel.HasValue)
        {
            query = query.Where(o => o.Channel == filter.Channel.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(o => o.Status == filter.Status.Value);
        }

        if (filter.StudentId.HasValue)
        {
            query = query.Where(o => o.StudentId == filter.StudentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();
            query = query.Where(o =>
                o.Recipient.ToLower().Contains(term)
                || (o.StudentName != null && o.StudentName.ToLower().Contains(term))
                || (o.StudentCode != null && o.StudentCode.ToLower().Contains(term))
                || o.Body.ToLower().Contains(term));
        }

        var allMatching = await query.ToListAsync(cancellationToken);

        if (filter.FromDate.HasValue)
        {
            var fromUtc = new DateTimeOffset(filter.FromDate.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            allMatching = allMatching.Where(o => o.CreatedAt >= fromUtc).ToList();
        }

        if (filter.ToDate.HasValue)
        {
            var toUtc = new DateTimeOffset(filter.ToDate.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
            allMatching = allMatching.Where(o => o.CreatedAt <= toUtc).ToList();
        }

        var ordered = allMatching
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

        var totalCount = ordered.Count;
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapMessageDto)
            .ToList();

        var quota = await GetSmsQuotaAsync(tenantId, null, cancellationToken);

        return new NotificationMessageLogResponse(items, totalCount, page, pageSize, quota);
    }

    public async Task<NotificationMessageDto> RetryMessageAsync(
        Guid messageId,
        DateTimeOffset? nowOverride = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var now = nowOverride ?? DateTimeOffset.UtcNow;

        var message = await db.NotificationOutboxes
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.Id == messageId, cancellationToken)
            ?? throw new EntityNotFoundException("NotificationMessage", messageId.ToString());

        message.Status = NotificationStatus.Queued;
        message.IsDeadLetter = false;
        message.AttemptCount = 0;
        message.StatusReason = null;
        message.NextAttemptAt = now;

        await db.SaveChangesAsync(cancellationToken);

        await DispatchPendingAsync(now, tenantId, cancellationToken);

        var refreshed = await db.NotificationOutboxes
            .AsNoTracking()
            .FirstAsync(o => o.TenantId == tenantId && o.Id == messageId, cancellationToken);

        return MapMessageDto(refreshed);
    }

    public async Task<TestSendNotificationResultDto> TriggerTestSendAsync(
        TestSendNotificationRequest request,
        DateTimeOffset? nowOverride = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var now = nowOverride ?? DateTimeOffset.UtcNow;

        var enqueueResult = await EnqueueAsync(
            new EnqueueNotificationRequest(
                MessageType: request.MessageType,
                RecipientPhone: request.RecipientPhone,
                RecipientEmail: request.RecipientEmail,
                StudentId: request.StudentId,
                Language: request.Language,
                IdempotencyKey: request.IdempotencyKey,
                Variables: request.Variables,
                ScheduledAtUtc: now),
            tenantIdOverride: tenantId,
            cancellationToken: cancellationToken);

        if (enqueueResult.EnqueuedCount == 0 && !enqueueResult.WasDuplicate)
        {
            return new TestSendNotificationResultDto(
                EnqueuedCount: 0,
                SentCount: 0,
                SkippedCount: 0,
                FailedCount: 0,
                SkippedReason: enqueueResult.SkippedReason,
                Messages: []);
        }

        if (!request.EnqueueOnly)
        {
            await DispatchPendingAsync(now, tenantId, cancellationToken);
        }

        var messageIds = enqueueResult.Messages.Select(m => m.Id).ToHashSet();
        var updatedRows = await db.NotificationOutboxes
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && messageIds.Contains(o.Id))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken);

        var dtos = updatedRows.Select(MapMessageDto).ToList();
        return new TestSendNotificationResultDto(
            EnqueuedCount: enqueueResult.EnqueuedCount,
            SentCount: dtos.Count(m => m.Status == nameof(NotificationStatus.Sent) || m.Status == nameof(NotificationStatus.Delivered)),
            SkippedCount: dtos.Count(m => m.Status == nameof(NotificationStatus.Skipped)),
            FailedCount: dtos.Count(m => m.Status == nameof(NotificationStatus.Failed)),
            SkippedReason: enqueueResult.SkippedReason,
            Messages: dtos);
    }

    public async Task<TenantSmsSenderIdDto> UpdateSmsSenderIdAsync(
        UpdateTenantSmsSenderIdRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var info = await GetTenantSmsSenderIdInfoAsync(tenantId, cancellationToken);
        if (!info.CanCustomizeSmsSenderId)
        {
            throw new FeatureNotInPlanException(FeatureKey.RemoveProductBranding);
        }

        var normalized = NormalizeSenderId(request.SmsSenderId);
        using (tenantContext.BeginSystemScope(tenantId))
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());

            tenant.SmsSenderId = normalized;
            await auditService.LogAsync(
                action: "TenantSmsSenderId.Updated",
                entityName: nameof(Tenant),
                entityId: tenant.Id.ToString(),
                details: new { SmsSenderId = normalized },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return new TenantSmsSenderIdDto(normalized, true);
    }

    private async Task<TenantSmsSenderIdDto> GetTenantSmsSenderIdInfoAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.Id == tenantId)
            .Select(t => new { t.SmsSenderId, t.AllowCustomSmsSenderId })
            .FirstOrDefaultAsync(cancellationToken);

        var brandingEval = await featureService.EvaluateAsync(tenantId, FeatureKey.RemoveProductBranding, cancellationToken);
        var canCustomize = (tenant?.AllowCustomSmsSenderId ?? false) || brandingEval.IsInPlan;
        return new TenantSmsSenderIdDto(tenant?.SmsSenderId, canCustomize);
    }

    public static string? NormalizeSenderId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length > 11 || trimmed.Any(c => !char.IsAsciiLetterOrDigit(c) && c != ' ' && c != '.' && c != '-' && c != '_'))
        {
            throw new DomainValidationException(
                "INVALID_SENDER_ID",
                "Sender ID must be 1 to 11 alphanumeric characters (plus optional spaces, '.', '-', '_').");
        }

        return trimmed;
    }

    private sealed record ProviderSendOutcome(
        bool Succeeded,
        string Provider,
        string? ProviderMessageId,
        string? ErrorMessage,
        bool IsSkipped = false,
        string? SkipReason = null,
        int? GatewaySegments = null,
        decimal? GatewayCost = null);

    private async Task<ProviderSendOutcome> SendViaProviderAsync(
        NotificationOutbox item,
        NotificationTypeDefinition definition,
        CancellationToken cancellationToken)
    {
        try
        {
            switch (item.Channel)
            {
                case NotificationChannel.Sms:
                {
                    var isPromotional = definition.SmsCategory == SmsMessageCategory.Promotional;
                    var resolvedSenderId = smsGatewayConfig is not null
                        ? await smsGatewayConfig.ResolveTenantSenderIdAsync(item.TenantId, isPromotional, null, cancellationToken)
                        : null;

                    var res = await smsProvider.SendAsync(
                        new SmsSendRequest(
                            To: item.Recipient,
                            Message: item.Body,
                            SenderId: resolvedSenderId,
                            TenantId: item.TenantId,
                            IdempotencyKey: item.IdempotencyKey,
                            IsPromotional: isPromotional),
                        cancellationToken);

                    return new ProviderSendOutcome(
                        Succeeded: res.Succeeded,
                        Provider: res.Provider,
                        ProviderMessageId: res.ProviderMessageId,
                        ErrorMessage: res.ErrorMessage,
                        IsSkipped: res.IsSkipped,
                        SkipReason: res.SkipReason,
                        GatewaySegments: res.GatewaySegments,
                        GatewayCost: res.GatewayCost);
                }

                case NotificationChannel.Email:
                {
                    await emailSender.SendAsync(
                        new EmailMessage(
                            To: item.Recipient,
                            Subject: item.Subject ?? "Notification",
                            HtmlBody: $"<p>{System.Net.WebUtility.HtmlEncode(item.Body)}</p>",
                            PlainTextBody: item.Body,
                            Metadata: new Dictionary<string, string>
                            {
                                ["Type"] = item.MessageType.ToString(),
                                ["TenantId"] = item.TenantId.ToString(),
                                ["IdempotencyKey"] = item.IdempotencyKey
                            }),
                        cancellationToken);
                    return new ProviderSendOutcome(true, "Email", $"email-{Guid.NewGuid():N}", null);
                }

                case NotificationChannel.WhatsApp:
                {
                    var res = await whatsAppProvider.SendAsync(
                        new WhatsAppSendRequest(
                            To: item.Recipient,
                            Message: item.Body,
                            TemplateName: item.MessageType.ToString(),
                            LanguageCode: item.Language,
                            TenantId: item.TenantId,
                            IdempotencyKey: item.IdempotencyKey),
                        cancellationToken);
                    return new ProviderSendOutcome(res.Succeeded, res.Provider, res.ProviderMessageId, res.ErrorMessage);
                }

                default:
                    return new ProviderSendOutcome(false, "Unknown", null, "Unsupported notification channel.");
            }
        }
        catch (Exception ex)
        {
            return new ProviderSendOutcome(false, item.Channel.ToString(), null, ex.Message);
        }
    }

    private async Task<TenantNotificationConfigDto> GetConfigDtoAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);

        var config = await db.TenantNotificationConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        return new TenantNotificationConfigDto(
            QuietHoursEnabled: config?.QuietHoursEnabled ?? true,
            QuietHoursStartMinutes: config?.QuietHoursStartMinutes ?? 21 * 60,
            QuietHoursEndMinutes: config?.QuietHoursEndMinutes ?? 6 * 60,
            TimeZone: tenant?.TimeZone ?? QuietHoursCalculator.DefaultTimeZoneId,
            FeeReminderDaysBefore: config?.FeeReminderDaysBefore ?? 3,
            FeeReminderOnDueDate: config?.FeeReminderOnDueDate ?? true,
            FeeReminderDaysAfterOverdue: config?.FeeReminderDaysAfterOverdue ?? 3,
            ExamReminderDaysBefore: config?.ExamReminderDaysBefore ?? 1,
            ExamReminderHoursBefore: config?.ExamReminderHoursBefore ?? 0,
            SmsOverQuotaPolicy: (config?.SmsOverQuotaPolicy ?? SmsOverQuotaPolicy.AllowAndRecordExtra).ToString(),
            DefaultLanguage: config?.DefaultLanguage ?? "en");
    }

    private async Task<AllowedNotificationChannelsDto> GetAllowedChannelsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var emailEval = await featureService.EvaluateAsync(tenantId, FeatureKey.EmailNotifications, cancellationToken);
        var whatsAppEval = await featureService.EvaluateAsync(tenantId, FeatureKey.WhatsAppMessaging, cancellationToken);

        return new AllowedNotificationChannelsDto(
            Sms: true,
            Email: emailEval.IsEnabled,
            EmailInPlan: emailEval.IsInPlan,
            WhatsApp: whatsAppEval.IsEnabled,
            WhatsAppInPlan: whatsAppEval.IsInPlan);
    }

    private async Task<IReadOnlyList<NotificationTypeConfigDto>> GetMessageTypeConfigsAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var settings = await db.TenantNotificationTypeSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .ToDictionaryAsync(s => s.MessageType, cancellationToken);

        var list = new List<NotificationTypeConfigDto>();
        foreach (var def in NotificationCatalog.All)
        {
            var isParentInPlan = true;
            var isParentEnabled = true;

            if (def.ParentFeatureKey.HasValue)
            {
                var eval = await featureService.EvaluateAsync(tenantId, def.ParentFeatureKey.Value, cancellationToken);
                isParentInPlan = eval.IsInPlan;
                isParentEnabled = eval.IsEnabled;
            }

            settings.TryGetValue(def.MessageType, out var saved);
            var isEnabled = saved?.IsEnabled ?? true;
            var smsEnabled = saved?.SmsEnabled ?? def.DefaultSmsEnabled;
            var emailEnabled = saved?.EmailEnabled ?? def.DefaultEmailEnabled;
            var whatsAppEnabled = saved?.WhatsAppEnabled ?? def.DefaultWhatsAppEnabled;

            list.Add(new NotificationTypeConfigDto(
                MessageType: def.MessageType.ToString(),
                ParentFeatureKey: def.ParentFeatureKey?.ToString(),
                IsParentFeatureInPlan: isParentInPlan,
                IsParentFeatureEnabled: isParentEnabled,
                IsEnabled: isEnabled,
                IsEffectiveEnabled: isParentEnabled && isEnabled,
                SmsEnabled: smsEnabled,
                EmailEnabled: emailEnabled,
                WhatsAppEnabled: whatsAppEnabled,
                ExemptFromQuietHours: def.ExemptFromQuietHours,
                AvailableVariables: def.Variables,
                SmsCategory: def.SmsCategory.ToString()));
        }

        return list;
    }

    private async Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesInternalAsync(
        Guid tenantId,
        NotificationMessageType? messageTypeFilter,
        string? languageFilter,
        CancellationToken cancellationToken)
    {
        var savedTemplates = await db.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var savedMap = savedTemplates.ToDictionary(
            t => (t.MessageType, NotificationCatalog.NormalizeLanguage(t.Language)));

        var defs = messageTypeFilter.HasValue
            ? NotificationCatalog.All.Where(d => d.MessageType == messageTypeFilter.Value)
            : NotificationCatalog.All;

        var langs = !string.IsNullOrWhiteSpace(languageFilter)
            ? [NotificationCatalog.NormalizeLanguage(languageFilter)]
            : NotificationCatalog.SupportedLanguages;

        var result = new List<NotificationTemplateDto>();
        foreach (var def in defs)
        {
            foreach (var lang in langs)
            {
                var defaultText = NotificationCatalog.GetDefaultTemplate(def.MessageType, lang);
                var hasCustom = savedMap.TryGetValue((def.MessageType, lang), out var custom) && custom.IsCustom;
                var subject = hasCustom ? custom!.Subject : defaultText.Subject;
                var body = hasCustom ? custom!.Body : defaultText.Body;

                var previewSubject = NotificationTemplateRenderer.Render(subject, NotificationCatalog.SampleVariables, fallbackToSample: true);
                var previewBody = NotificationTemplateRenderer.Render(body, NotificationCatalog.SampleVariables, fallbackToSample: true);
                var calc = segmentCalculator.Calculate(previewBody);

                result.Add(new NotificationTemplateDto(
                    MessageType: def.MessageType.ToString(),
                    Language: lang,
                    Subject: subject,
                    Body: body,
                    IsCustom: hasCustom,
                    PreviewSubject: previewSubject,
                    PreviewBody: previewBody,
                    SmsCalculation: calc,
                    AvailableVariables: def.Variables));
            }
        }

        return result;
    }

    private async Task<int?> GetMonthlySmsQuotaLimitAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var check = await limitService.CheckAsync(
            tenantId,
            LimitKey.SmsSegmentsPerMonth,
            increment: 0,
            cancellationToken: cancellationToken);
        return check.MaxAllowed;
    }

    private async Task<(int UsedSegments, int ExtraSegments)> GetMonthlySmsUsageCountersAsync(
        Guid tenantId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthEnd = monthStart.AddMonths(1);

        var sentSms = await db.NotificationOutboxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => !o.IsDeleted
                        && o.TenantId == tenantId
                        && o.Channel == NotificationChannel.Sms
                        && (o.Status == NotificationStatus.Sent || o.Status == NotificationStatus.Delivered))
            .Select(o => new { o.SentAt, o.CreatedAt, o.SmsSegments, o.ExtraSegments })
            .ToListAsync(cancellationToken);

        var inMonth = sentSms
            .Where(o =>
            {
                var ts = o.SentAt ?? o.CreatedAt;
                return ts >= monthStart && ts < monthEnd;
            })
            .ToList();

        var used = inMonth.Sum(x => x.SmsSegments);
        var extra = inMonth.Sum(x => x.ExtraSegments);
        return (used, extra);
    }

    private static NotificationMessageDto MapMessageDto(NotificationOutbox o) =>
        new(
            Id: o.Id,
            MessageType: o.MessageType.ToString(),
            Channel: o.Channel.ToString(),
            Status: o.Status.ToString(),
            Language: o.Language,
            Recipient: o.Recipient,
            RecipientName: o.RecipientName,
            StudentId: o.StudentId,
            StudentName: o.StudentName,
            StudentCode: o.StudentCode,
            Subject: o.Subject,
            Body: o.Body,
            IdempotencyKey: o.IdempotencyKey,
            SmsSegments: o.SmsSegments,
            ExtraSegments: o.ExtraSegments,
            AttemptCount: o.AttemptCount,
            MaxAttempts: o.MaxAttempts,
            IsDeadLetter: o.IsDeadLetter,
            CreatedAt: o.CreatedAt,
            NextAttemptAt: o.NextAttemptAt,
            SentAt: o.SentAt,
            DeliveredAt: o.DeliveredAt,
            LastAttemptAt: o.LastAttemptAt,
            Provider: o.Provider,
            ProviderMessageId: o.ProviderMessageId,
            StatusReason: o.StatusReason,
            GatewaySegments: o.GatewaySegments,
            GatewayCost: o.GatewayCost);
}
