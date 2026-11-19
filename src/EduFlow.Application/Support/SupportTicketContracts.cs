using EduFlow.Domain.Enums;

namespace EduFlow.Application.Support;

public sealed record SupportTicketMessageDto(
    Guid Id,
    Guid SenderUserId,
    string SenderName,
    string SenderRole,
    bool IsFromSuperAdmin,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record SupportTicketSummaryDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Category,
    string Status,
    bool IsPriority,
    string CreatedByName,
    string CreatedByEmail,
    int MessageCount,
    string LastMessagePreview,
    DateTimeOffset LastMessageAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt);

public sealed record SupportTicketDetailDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Category,
    string Status,
    bool IsPriority,
    string CreatedByName,
    string CreatedByEmail,
    DateTimeOffset LastMessageAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SupportTicketMessageDto> Messages);

public sealed record SupportTicketPageDto(
    IReadOnlyList<SupportTicketSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record CreateSupportTicketRequest(
    string Subject,
    string Body,
    SupportTicketCategory Category = SupportTicketCategory.General);

public sealed record ReplySupportTicketRequest(string Body);

public sealed record SuperAdminSupportTicketSummaryDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string TenantSubdomain,
    string? PlanName,
    string TicketNumber,
    string Subject,
    string Category,
    string Status,
    bool IsPriority,
    string CreatedByName,
    string CreatedByEmail,
    int MessageCount,
    string LastMessagePreview,
    DateTimeOffset LastMessageAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt);

public sealed record SuperAdminSupportTicketDetailDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string TenantSubdomain,
    string? PlanName,
    string TicketNumber,
    string Subject,
    string Category,
    string Status,
    bool IsPriority,
    string CreatedByName,
    string CreatedByEmail,
    DateTimeOffset LastMessageAt,
    DateTimeOffset? ResolvedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SupportTicketMessageDto> Messages);

public sealed record SuperAdminSupportInboxDto(
    int OpenCount,
    int PriorityOpenCount,
    int WaitingCount,
    int ResolvedCount,
    IReadOnlyList<SuperAdminSupportTicketSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record SuperAdminReplySupportTicketRequest(
    string Body,
    SupportTicketStatus? Status = null);

public sealed record UpdateSupportTicketStatusRequest(
    SupportTicketStatus Status);
