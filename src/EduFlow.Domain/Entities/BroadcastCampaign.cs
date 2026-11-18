using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class BroadcastCampaign : TenantEntity
{
    public string Title { get; set; } = string.Empty;
    public BroadcastAudienceKind AudienceKind { get; set; }
    public Guid? AudienceId { get; set; }
    public NotificationChannel Channel { get; set; }
    public string Language { get; set; } = "en";
    public string Subject { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; set; } = DateTimeOffset.UtcNow;
    public BroadcastCampaignStatus Status { get; set; } = BroadcastCampaignStatus.Queued;
    public int RecipientCount { get; set; }
    public int SkippedCount { get; set; }
    public int EstimatedSegments { get; set; }
}

public sealed class BroadcastRecipient : TenantEntity
{
    public Guid CampaignId { get; set; }
    public Guid StudentId { get; set; }
    public Guid? OutboxId { get; set; }
    public string StudentName { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; }
    public NotificationStatus Status { get; set; } = NotificationStatus.Queued;
    public int SmsSegments { get; set; }
    public string? StatusReason { get; set; }
}
