namespace EduFlow.Domain.Enums;

public enum NotificationMessageType
{
    AttendancePresent = 1,
    AttendanceAbsent = 2,
    ClassStarted = 3,
    ClassStopped = 4,
    ClassCancelled = 5,
    FeeDueReminder = 6,
    PaymentReceivedParent = 7,
    PaymentReceivedAdmin = 8,
    ExamScheduled = 9,
    ExamReminder = 10,
    MarksReleased = 11,
    Broadcast = 12,
    BackupResultAdmin = 13,
    DailySummaryAdmin = 14
}

public enum NotificationChannel
{
    Sms = 1,
    Email = 2,
    WhatsApp = 3
}

public enum NotificationStatus
{
    Queued = 1,
    Sent = 2,
    Delivered = 3,
    Failed = 4,
    Skipped = 5
}

public enum SmsOverQuotaPolicy
{
    AllowAndRecordExtra = 1,
    Block = 2
}

public enum SmsMessageCategory
{
    Transactional = 1,
    Promotional = 2
}
