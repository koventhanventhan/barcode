using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Enums;

namespace EduFlow.Application.Notifications;

public sealed record TenantNotificationConfigDto(
    bool QuietHoursEnabled,
    int QuietHoursStartMinutes,
    int QuietHoursEndMinutes,
    string TimeZone,
    int FeeReminderDaysBefore,
    bool FeeReminderOnDueDate,
    int FeeReminderDaysAfterOverdue,
    int ExamReminderDaysBefore,
    int ExamReminderHoursBefore,
    string SmsOverQuotaPolicy,
    string DefaultLanguage);

public sealed record UpdateNotificationConfigRequest(
    bool QuietHoursEnabled,
    int QuietHoursStartMinutes,
    int QuietHoursEndMinutes,
    int FeeReminderDaysBefore,
    bool FeeReminderOnDueDate,
    int FeeReminderDaysAfterOverdue,
    int ExamReminderDaysBefore,
    SmsOverQuotaPolicy SmsOverQuotaPolicy = SmsOverQuotaPolicy.AllowAndRecordExtra,
    string DefaultLanguage = "en",
    int ExamReminderHoursBefore = 0);

public sealed record NotificationTypeConfigDto(
    string MessageType,
    string? ParentFeatureKey,
    bool IsParentFeatureInPlan,
    bool IsParentFeatureEnabled,
    bool IsEnabled,
    bool IsEffectiveEnabled,
    bool SmsEnabled,
    bool EmailEnabled,
    bool WhatsAppEnabled,
    bool ExemptFromQuietHours,
    IReadOnlyList<string> AvailableVariables,
    string SmsCategory = "Transactional");

public sealed record UpdateNotificationTypeSettingRequest(
    bool IsEnabled,
    bool SmsEnabled,
    bool EmailEnabled,
    bool WhatsAppEnabled);

public sealed record NotificationTemplateDto(
    string MessageType,
    string Language,
    string Subject,
    string Body,
    bool IsCustom,
    string PreviewSubject,
    string PreviewBody,
    SmsSegmentCalculation SmsCalculation,
    IReadOnlyList<string> AvailableVariables);

public sealed record UpdateNotificationTemplateRequest(
    string Subject,
    string Body);

public sealed record PreviewTemplateRequest(
    string Body,
    string? Subject = null,
    IReadOnlyDictionary<string, string>? Variables = null);

public sealed record PreviewTemplateResponse(
    string RenderedSubject,
    string RenderedBody,
    SmsSegmentCalculation SmsCalculation);

public sealed record SmsQuotaSummaryDto(
    int UsedSegments,
    int? MonthlyQuota,
    int RemainingSegments,
    int ExtraSegments,
    double UsagePercentage,
    bool IsWarning80Percent,
    bool IsExceeded100Percent,
    string OverQuotaPolicy);

public sealed record AllowedNotificationChannelsDto(
    bool Sms,
    bool Email,
    bool EmailInPlan,
    bool WhatsApp,
    bool WhatsAppInPlan);

public sealed record NotificationSettingsWorkspaceDto(
    TenantNotificationConfigDto Config,
    SmsQuotaSummaryDto Quota,
    AllowedNotificationChannelsDto AllowedChannels,
    IReadOnlyList<NotificationTypeConfigDto> MessageTypes,
    IReadOnlyList<NotificationTemplateDto> Templates,
    IReadOnlyDictionary<string, string> SampleVariables,
    string? SmsSenderId = null,
    bool CanCustomizeSmsSenderId = false,
    bool PromotionalSenderIdConfigured = false);

public sealed record TenantSmsSenderIdDto(
    string? SmsSenderId,
    bool CanCustomizeSmsSenderId);

public sealed record UpdateTenantSmsSenderIdRequest(
    string? SmsSenderId);

public sealed record EnqueueNotificationRequest(
    NotificationMessageType MessageType,
    string? RecipientPhone = null,
    string? RecipientEmail = null,
    string? RecipientName = null,
    Guid? StudentId = null,
    string? StudentName = null,
    string? StudentCode = null,
    string? Language = null,
    string? IdempotencyKey = null,
    IReadOnlyDictionary<string, string>? Variables = null,
    IReadOnlyList<NotificationChannel>? ChannelsOverride = null,
    DateTimeOffset? ScheduledAtUtc = null,
    string? SubjectOverride = null,
    string? BodyOverride = null);

public sealed record EnqueueNotificationResultDto(
    int EnqueuedCount,
    bool WasDuplicate,
    string? SkippedReason,
    IReadOnlyList<NotificationMessageDto> Messages);

public sealed record NotificationMessageDto(
    Guid Id,
    string MessageType,
    string Channel,
    string Status,
    string Language,
    string Recipient,
    string? RecipientName,
    Guid? StudentId,
    string? StudentName,
    string? StudentCode,
    string? Subject,
    string Body,
    string IdempotencyKey,
    int SmsSegments,
    int ExtraSegments,
    int AttemptCount,
    int MaxAttempts,
    bool IsDeadLetter,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? LastAttemptAt,
    string? Provider,
    string? ProviderMessageId,
    string? StatusReason,
    int? GatewaySegments = null,
    decimal? GatewayCost = null);

public sealed record NotificationMessageFilter(
    NotificationMessageType? MessageType = null,
    NotificationChannel? Channel = null,
    NotificationStatus? Status = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? StudentId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record NotificationMessageLogResponse(
    IReadOnlyList<NotificationMessageDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    SmsQuotaSummaryDto Quota);

public sealed record DispatchResultDto(
    int ProcessedCount,
    int SentCount,
    int SkippedCount,
    int FailedCount,
    int DeferredCount,
    int DeadLetterCount);

public sealed record TestSendNotificationRequest(
    NotificationMessageType MessageType,
    string? RecipientPhone = null,
    string? RecipientEmail = null,
    Guid? StudentId = null,
    string? Language = null,
    string? IdempotencyKey = null,
    IReadOnlyDictionary<string, string>? Variables = null,
    bool EnqueueOnly = false);

public sealed record TestSendNotificationResultDto(
    int EnqueuedCount,
    int SentCount,
    int SkippedCount,
    int FailedCount,
    string? SkippedReason,
    IReadOnlyList<NotificationMessageDto> Messages);

public interface INotificationService
{
    Task<NotificationSettingsWorkspaceDto> GetSettingsWorkspaceAsync(CancellationToken cancellationToken = default);
    Task<TenantNotificationConfigDto> UpdateConfigAsync(UpdateNotificationConfigRequest request, CancellationToken cancellationToken = default);
    Task<NotificationTypeConfigDto> UpdateTypeSettingAsync(NotificationMessageType messageType, UpdateNotificationTypeSettingRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationTemplateDto>> GetTemplatesAsync(NotificationMessageType? messageType = null, string? language = null, CancellationToken cancellationToken = default);
    Task<NotificationTemplateDto> UpdateTemplateAsync(NotificationMessageType messageType, string language, UpdateNotificationTemplateRequest request, CancellationToken cancellationToken = default);
    Task<NotificationTemplateDto> ResetTemplateAsync(NotificationMessageType messageType, string language, CancellationToken cancellationToken = default);
    PreviewTemplateResponse PreviewTemplate(PreviewTemplateRequest request);
    Task<SmsQuotaSummaryDto> GetSmsQuotaAsync(Guid? tenantIdOverride = null, DateTimeOffset? nowOverride = null, CancellationToken cancellationToken = default);
    Task<EnqueueNotificationResultDto> EnqueueAsync(EnqueueNotificationRequest request, Guid? tenantIdOverride = null, CancellationToken cancellationToken = default);
    Task<NotificationMessageLogResponse> GetMessageLogAsync(NotificationMessageFilter filter, CancellationToken cancellationToken = default);
    Task<NotificationMessageDto> RetryMessageAsync(Guid messageId, DateTimeOffset? nowOverride = null, CancellationToken cancellationToken = default);
    Task<TestSendNotificationResultDto> TriggerTestSendAsync(TestSendNotificationRequest request, DateTimeOffset? nowOverride = null, CancellationToken cancellationToken = default);
    Task<TenantSmsSenderIdDto> UpdateSmsSenderIdAsync(UpdateTenantSmsSenderIdRequest request, CancellationToken cancellationToken = default);
}

public interface INotificationDispatcher
{
    Task<DispatchResultDto> DispatchPendingAsync(
        DateTimeOffset? nowOverride = null,
        Guid? tenantIdFilter = null,
        CancellationToken cancellationToken = default);
}
