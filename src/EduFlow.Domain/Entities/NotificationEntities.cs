using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class TenantNotificationConfig : TenantEntity
{
    public bool QuietHoursEnabled { get; set; } = true;
    public int QuietHoursStartMinutes { get; set; } = 21 * 60;
    public int QuietHoursEndMinutes { get; set; } = 6 * 60;
    public int FeeReminderDaysBefore { get; set; } = 3;
    public bool FeeReminderOnDueDate { get; set; } = true;
    public int FeeReminderDaysAfterOverdue { get; set; } = 3;
    public int ExamReminderDaysBefore { get; set; } = 1;
    public SmsOverQuotaPolicy SmsOverQuotaPolicy { get; set; } = SmsOverQuotaPolicy.AllowAndRecordExtra;
    public string DefaultLanguage { get; set; } = "en";
}

public sealed class TenantNotificationTypeSetting : TenantEntity
{
    public NotificationMessageType MessageType { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool SmsEnabled { get; set; } = true;
    public bool EmailEnabled { get; set; }
    public bool WhatsAppEnabled { get; set; }
}

public sealed class NotificationTemplate : TenantEntity
{
    public NotificationMessageType MessageType { get; set; }
    public string Language { get; set; } = "en";
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsCustom { get; set; } = true;
}

public sealed class NotificationOutbox : TenantEntity
{
    public NotificationMessageType MessageType { get; set; }
    public NotificationChannel Channel { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Queued;
    public string Language { get; set; } = "en";
    public string Recipient { get; set; } = string.Empty;
    public string? RecipientName { get; set; }
    public Guid? StudentId { get; set; }
    public string? StudentName { get; set; }
    public string? StudentCode { get; set; }
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public int SmsSegments { get; set; }
    public int ExtraSegments { get; set; }
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 5;
    public bool IsDeadLetter { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? Provider { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? StatusReason { get; set; }
}
