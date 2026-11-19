using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class SupportTicket : TenantEntity
{
    public int TicketSequence { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public SupportTicketCategory Category { get; set; } = SupportTicketCategory.General;
    public SupportTicketStatus Status { get; set; } = SupportTicketStatus.Open;
    public bool IsPriority { get; set; }
    public Guid CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string CreatedByEmail { get; set; } = string.Empty;
    public Guid? AssignedToUserId { get; set; }
    public DateTimeOffset LastMessageAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public ICollection<SupportTicketMessage> Messages { get; set; } = new List<SupportTicketMessage>();
}

public sealed class SupportTicketMessage : TenantEntity
{
    public Guid TicketId { get; set; }
    public SupportTicket? Ticket { get; set; }
    public Guid SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty;
    public bool IsFromSuperAdmin { get; set; }
    public string Body { get; set; } = string.Empty;
}
