using EduFlow.Domain.Enums;

namespace EduFlow.Application.Broadcasts;

public sealed record BroadcastComposeRequest(
    BroadcastAudienceKind Audience,
    NotificationChannel Channel,
    string Message,
    Guid? AudienceId = null,
    IReadOnlyList<Guid>? StudentIds = null,
    string Language = "en",
    string? Subject = null,
    string? BodyTemplate = null,
    DateTimeOffset? ScheduledAt = null);

public sealed record BroadcastSkipDto(
    Guid StudentId,
    string StudentName,
    string Reason);

public sealed record BroadcastPreviewDto(
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
    IReadOnlyList<BroadcastSkipDto> Skipped);

public sealed record BroadcastSendResultDto(
    Guid CampaignId,
    int EnqueuedCount,
    int SkippedCount,
    int EstimatedSegments,
    DateTimeOffset ScheduledAt,
    string Status);

public sealed record BroadcastCampaignDto(
    Guid Id,
    string Title,
    string Audience,
    string Channel,
    string Language,
    string Subject,
    string BodyTemplate,
    string Message,
    DateTimeOffset ScheduledAt,
    string Status,
    int RecipientCount,
    int SkippedCount,
    int EstimatedSegments,
    DateTimeOffset CreatedAt);

public sealed record BroadcastRecipientDto(
    Guid Id,
    Guid StudentId,
    string StudentName,
    string Recipient,
    string Channel,
    string Status,
    int SmsSegments,
    string? StatusReason);

public sealed record BroadcastCampaignDetailDto(
    BroadcastCampaignDto Campaign,
    IReadOnlyList<BroadcastRecipientDto> Recipients);

public sealed record BroadcastHistoryPageDto(
    IReadOnlyList<BroadcastCampaignDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public interface IBroadcastService
{
    Task<BroadcastPreviewDto> PreviewAsync(BroadcastComposeRequest request, CancellationToken cancellationToken = default);
    Task<BroadcastSendResultDto> SendAsync(BroadcastComposeRequest request, CancellationToken cancellationToken = default);
    Task<BroadcastHistoryPageDto> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<BroadcastCampaignDetailDto> GetAsync(Guid campaignId, CancellationToken cancellationToken = default);
}
