using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Domain;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Support;

public interface ISupportTicketService
{
    Task<SupportTicketPageDto> GetTenantTicketsAsync(SupportTicketStatus? status = null, int page = 1, int pageSize = 25, CancellationToken cancellationToken = default);
    Task<SupportTicketDetailDto> GetTenantTicketByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SupportTicketDetailDto> CreateTicketAsync(CreateSupportTicketRequest request, CancellationToken cancellationToken = default);
    Task<SupportTicketDetailDto> ReplyAsTenantAsync(Guid id, ReplySupportTicketRequest request, CancellationToken cancellationToken = default);
    Task<SupportTicketDetailDto> CloseTenantTicketAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SuperAdminSupportInboxDto> GetSuperAdminInboxAsync(
        SupportTicketStatus? status = null,
        bool? priorityOnly = null,
        Guid? tenantId = null,
        string? search = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default);
    Task<SuperAdminSupportTicketDetailDto> GetSuperAdminTicketByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SuperAdminSupportTicketDetailDto> ReplyAsSuperAdminAsync(Guid id, SuperAdminReplySupportTicketRequest request, CancellationToken cancellationToken = default);
    Task<SuperAdminSupportTicketDetailDto> UpdateStatusAsSuperAdminAsync(Guid id, UpdateSupportTicketStatusRequest request, CancellationToken cancellationToken = default);
}

public sealed class SupportTicketService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    IAuditService auditService,
    IEmailSender emailSender) : ISupportTicketService
{
    private static readonly SemaphoreSlim SequenceLock = new(1, 1);

    public async Task<SupportTicketPageDto> GetTenantTicketsAsync(
        SupportTicketStatus? status = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.SupportTickets.AsNoTracking();
        if (status.HasValue)
        {
            query = query.Where(t => t.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var tickets = await query
            .OrderByDescending(t => t.LastMessageAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var ticketIds = tickets.Select(t => t.Id).ToList();
        var messages = ticketIds.Count == 0
            ? []
            : await db.SupportTicketMessages
                .AsNoTracking()
                .Where(m => ticketIds.Contains(m.TicketId))
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

        var byTicket = messages.GroupBy(m => m.TicketId).ToDictionary(g => g.Key, g => g.ToList());
        var items = tickets.Select(t =>
        {
            byTicket.TryGetValue(t.Id, out var list);
            var preview = BuildPreview(list?.FirstOrDefault()?.Body);
            return new SupportTicketSummaryDto(
                t.Id,
                t.TicketNumber,
                t.Subject,
                t.Category.ToString(),
                t.Status.ToString(),
                t.IsPriority,
                t.CreatedByName,
                t.CreatedByEmail,
                list?.Count ?? 0,
                preview,
                t.LastMessageAt,
                t.ResolvedAt,
                t.ClosedAt,
                t.CreatedAt);
        }).ToList();

        return new SupportTicketPageDto(items, totalCount, safePage, safePageSize);
    }

    public async Task<SupportTicketDetailDto> GetTenantTicketByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var ticket = await db.SupportTickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SupportTicket), id.ToString());

        var messages = await db.SupportTicketMessages
            .AsNoTracking()
            .Where(m => m.TicketId == ticket.Id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new SupportTicketMessageDto(
                m.Id,
                m.SenderUserId,
                m.SenderName,
                m.SenderRole,
                m.IsFromSuperAdmin,
                m.Body,
                m.CreatedAt))
            .ToListAsync(cancellationToken);

        return MapTenantDetail(ticket, messages);
    }

    public async Task<SupportTicketDetailDto> CreateTicketAsync(
        CreateSupportTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var userId = tenantContext.UserId
            ?? throw new DomainValidationException("AUTH_REQUIRED", "Authenticated user is required.", 401);

        var subject = (request.Subject ?? string.Empty).Trim();
        if (subject.Length < 3 || subject.Length > 200)
        {
            throw new DomainValidationException("INVALID_SUBJECT", "Enter a ticket subject (3–200 characters).");
        }

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length < 5 || body.Length > 4000)
        {
            throw new DomainValidationException("INVALID_BODY", "Enter a ticket description (5–4000 characters).");
        }

        if (!Enum.IsDefined(request.Category))
        {
            throw new DomainValidationException("INVALID_CATEGORY", "Choose a valid support ticket category.");
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        var creatorName = user?.FullName ?? tenantContext.UserEmail ?? "Institute Admin";
        var creatorEmail = user?.Email ?? tenantContext.UserEmail ?? string.Empty;
        var isPriority = await featureService.IsEnabledAsync(tenantId, FeatureKey.PrioritySupport, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        SupportTicket ticket;
        SupportTicketMessage firstMessage;

        await SequenceLock.WaitAsync(cancellationToken);
        try
        {
            var maxSeq = await db.SupportTickets
                .IgnoreQueryFilters()
                .Select(t => (int?)t.TicketSequence)
                .MaxAsync(cancellationToken) ?? 0;

            var nextSeq = maxSeq + 1;
            ticket = new SupportTicket
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TicketSequence = nextSeq,
                TicketNumber = $"TKT-{nextSeq:000000}",
                Subject = subject,
                Category = request.Category,
                Status = SupportTicketStatus.Open,
                IsPriority = isPriority,
                CreatedByUserId = userId,
                CreatedByName = creatorName,
                CreatedByEmail = creatorEmail,
                LastMessageAt = now
            };

            firstMessage = new SupportTicketMessage
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                TicketId = ticket.Id,
                SenderUserId = userId,
                SenderName = creatorName,
                SenderRole = tenantContext.Role?.ToString() ?? nameof(RoleType.InstituteAdmin),
                IsFromSuperAdmin = false,
                Body = body
            };

            db.SupportTickets.Add(ticket);
            db.SupportTicketMessages.Add(firstMessage);

            await auditService.LogAsync(
                "SupportTicket.Created",
                nameof(SupportTicket),
                ticket.Id.ToString(),
                new { ticket.TicketNumber, Category = ticket.Category.ToString(), ticket.IsPriority },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            SequenceLock.Release();
        }

        return MapTenantDetail(
            ticket,
            [
                new SupportTicketMessageDto(
                    firstMessage.Id,
                    firstMessage.SenderUserId,
                    firstMessage.SenderName,
                    firstMessage.SenderRole,
                    firstMessage.IsFromSuperAdmin,
                    firstMessage.Body,
                    firstMessage.CreatedAt)
            ]);
    }

    public async Task<SupportTicketDetailDto> ReplyAsTenantAsync(
        Guid id,
        ReplySupportTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var userId = tenantContext.UserId
            ?? throw new DomainValidationException("AUTH_REQUIRED", "Authenticated user is required.", 401);

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length < 1 || body.Length > 4000)
        {
            throw new DomainValidationException("INVALID_BODY", "Enter a reply message (1–4000 characters).");
        }

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SupportTicket), id.ToString());

        if (ticket.Status == SupportTicketStatus.Closed)
        {
            throw new DomainValidationException("TICKET_CLOSED", "This ticket is closed. Open a new ticket if you need further help.");
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var msg = new SupportTicketMessage
        {
            TenantId = ticket.TenantId,
            TicketId = ticket.Id,
            SenderUserId = userId,
            SenderName = user?.FullName ?? tenantContext.UserEmail ?? "Institute User",
            SenderRole = tenantContext.Role?.ToString() ?? nameof(RoleType.InstituteAdmin),
            IsFromSuperAdmin = false,
            Body = body
        };

        db.SupportTicketMessages.Add(msg);
        ticket.LastMessageAt = now;
        if (ticket.Status is SupportTicketStatus.WaitingOnInstitute or SupportTicketStatus.Resolved)
        {
            ticket.Status = SupportTicketStatus.Open;
            ticket.ResolvedAt = null;
        }

        await auditService.LogAsync(
            "SupportTicket.Replied",
            nameof(SupportTicket),
            ticket.Id.ToString(),
            new { ticket.TicketNumber, Status = ticket.Status.ToString() },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetTenantTicketByIdAsync(ticket.Id, cancellationToken);
    }

    public async Task<SupportTicketDetailDto> CloseTenantTicketAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SupportTicket), id.ToString());

        if (ticket.Status != SupportTicketStatus.Closed)
        {
            ticket.Status = SupportTicketStatus.Closed;
            ticket.ClosedAt = DateTimeOffset.UtcNow;

            await auditService.LogAsync(
                "SupportTicket.Closed",
                nameof(SupportTicket),
                ticket.Id.ToString(),
                new { ticket.TicketNumber },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetTenantTicketByIdAsync(ticket.Id, cancellationToken);
    }

    public async Task<SuperAdminSupportInboxDto> GetSuperAdminInboxAsync(
        SupportTicketStatus? status = null,
        bool? priorityOnly = null,
        Guid? tenantId = null,
        string? search = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        RequireSuperAdmin();
        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var baseQuery = db.SupportTickets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted);

        var allStats = await baseQuery
            .Select(t => new { t.Status, t.IsPriority })
            .ToListAsync(cancellationToken);

        var openCount = allStats.Count(t => t.Status is SupportTicketStatus.Open or SupportTicketStatus.InProgress or SupportTicketStatus.WaitingOnInstitute);
        var priorityOpenCount = allStats.Count(t => t.IsPriority && t.Status is SupportTicketStatus.Open or SupportTicketStatus.InProgress or SupportTicketStatus.WaitingOnInstitute);
        var waitingCount = allStats.Count(t => t.Status == SupportTicketStatus.WaitingOnInstitute);
        var resolvedCount = allStats.Count(t => t.Status is SupportTicketStatus.Resolved or SupportTicketStatus.Closed);

        if (status.HasValue)
        {
            baseQuery = baseQuery.Where(t => t.Status == status.Value);
        }

        if (priorityOnly == true)
        {
            baseQuery = baseQuery.Where(t => t.IsPriority);
        }

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            baseQuery = baseQuery.Where(t => t.TenantId == tenantId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            var matchingTenantIds = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => !t.IsDeleted && (t.Name.ToLower().Contains(term) || t.Subdomain.ToLower().Contains(term)))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);

            baseQuery = baseQuery.Where(t =>
                t.TicketNumber.ToLower().Contains(term)
                || t.Subject.ToLower().Contains(term)
                || t.CreatedByEmail.ToLower().Contains(term)
                || matchingTenantIds.Contains(t.TenantId));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var tickets = await baseQuery
            .OrderBy(t => t.Status == SupportTicketStatus.Resolved || t.Status == SupportTicketStatus.Closed ? 1 : 0)
            .ThenByDescending(t => t.IsPriority)
            .ThenByDescending(t => t.LastMessageAt)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);

        var ticketTenantIds = tickets.Select(t => t.TenantId).Distinct().ToList();
        var tenantsById = ticketTenantIds.Count == 0
            ? new Dictionary<Guid, Tenant>()
            : await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => ticketTenantIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, cancellationToken);

        var plansByTenant = await LoadPlanNamesByTenantAsync(ticketTenantIds, cancellationToken);

        var ticketIds = tickets.Select(t => t.Id).ToList();
        var messages = ticketIds.Count == 0
            ? []
            : await db.SupportTicketMessages
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(m => !m.IsDeleted && ticketIds.Contains(m.TicketId))
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync(cancellationToken);

        var messagesByTicket = messages.GroupBy(m => m.TicketId).ToDictionary(g => g.Key, g => g.ToList());

        var items = tickets.Select(t =>
        {
            tenantsById.TryGetValue(t.TenantId, out var tenant);
            plansByTenant.TryGetValue(t.TenantId, out var planName);
            messagesByTicket.TryGetValue(t.Id, out var list);

            return new SuperAdminSupportTicketSummaryDto(
                t.Id,
                t.TenantId,
                tenant?.Name ?? "Unknown institute",
                tenant?.Subdomain ?? string.Empty,
                planName,
                t.TicketNumber,
                t.Subject,
                t.Category.ToString(),
                t.Status.ToString(),
                t.IsPriority,
                t.CreatedByName,
                t.CreatedByEmail,
                list?.Count ?? 0,
                BuildPreview(list?.FirstOrDefault()?.Body),
                t.LastMessageAt,
                t.ResolvedAt,
                t.ClosedAt,
                t.CreatedAt);
        }).ToList();

        return new SuperAdminSupportInboxDto(
            openCount,
            priorityOpenCount,
            waitingCount,
            resolvedCount,
            items,
            totalCount,
            safePage,
            safePageSize);
    }

    public async Task<SuperAdminSupportTicketDetailDto> GetSuperAdminTicketByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        RequireSuperAdmin();

        var ticket = await db.SupportTickets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(SupportTicket), id.ToString());

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticket.TenantId, cancellationToken);

        var plansByTenant = await LoadPlanNamesByTenantAsync([ticket.TenantId], cancellationToken);
        plansByTenant.TryGetValue(ticket.TenantId, out var planName);

        var messages = await db.SupportTicketMessages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => !m.IsDeleted && m.TicketId == ticket.Id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new SupportTicketMessageDto(
                m.Id,
                m.SenderUserId,
                m.SenderName,
                m.SenderRole,
                m.IsFromSuperAdmin,
                m.Body,
                m.CreatedAt))
            .ToListAsync(cancellationToken);

        return new SuperAdminSupportTicketDetailDto(
            ticket.Id,
            ticket.TenantId,
            tenant?.Name ?? "Unknown institute",
            tenant?.Subdomain ?? string.Empty,
            planName,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Category.ToString(),
            ticket.Status.ToString(),
            ticket.IsPriority,
            ticket.CreatedByName,
            ticket.CreatedByEmail,
            ticket.LastMessageAt,
            ticket.ResolvedAt,
            ticket.ClosedAt,
            ticket.CreatedAt,
            messages);
    }

    public async Task<SuperAdminSupportTicketDetailDto> ReplyAsSuperAdminAsync(
        Guid id,
        SuperAdminReplySupportTicketRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireSuperAdmin();
        var actorId = tenantContext.UserId
            ?? throw new DomainValidationException("AUTH_REQUIRED", "Authenticated super admin is required.", 401);

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length < 1 || body.Length > 4000)
        {
            throw new DomainValidationException("INVALID_BODY", "Enter a reply message (1–4000 characters).");
        }

        if (request.Status.HasValue && !Enum.IsDefined(request.Status.Value))
        {
            throw new DomainValidationException("INVALID_STATUS", "Invalid support ticket status.");
        }

        var now = DateTimeOffset.UtcNow;
        SupportTicket ticket;

        using (tenantContext.BeginSystemScope())
        {
            ticket = await db.SupportTickets
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(SupportTicket), id.ToString());

            var msg = new SupportTicketMessage
            {
                TenantId = ticket.TenantId,
                TicketId = ticket.Id,
                SenderUserId = actorId,
                SenderName = "Platform Support",
                SenderRole = nameof(RoleType.SuperAdmin),
                IsFromSuperAdmin = true,
                Body = body
            };

            db.SupportTicketMessages.Add(msg);
            ticket.AssignedToUserId ??= actorId;
            ticket.LastMessageAt = now;

            if (request.Status.HasValue)
            {
                ApplyStatus(ticket, request.Status.Value, now);
            }
            else if (ticket.Status is SupportTicketStatus.Open or SupportTicketStatus.InProgress)
            {
                ApplyStatus(ticket, SupportTicketStatus.WaitingOnInstitute, now);
            }

            await auditService.LogAsync(
                "SupportTicket.SuperAdminReplied",
                nameof(SupportTicket),
                ticket.Id.ToString(),
                new { ticket.TicketNumber, Status = ticket.Status.ToString() },
                tenantIdOverride: ticket.TenantId,
                actorUserIdOverride: actorId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(ticket.CreatedByEmail)
            && !ticket.CreatedByEmail.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    To: ticket.CreatedByEmail,
                    Subject: $"[{ticket.TicketNumber}] Support reply: {ticket.Subject}",
                    HtmlBody: $"<p>Hello {System.Net.WebUtility.HtmlEncode(ticket.CreatedByName)},</p><p>Platform Support replied to your ticket <strong>{System.Net.WebUtility.HtmlEncode(ticket.TicketNumber)}</strong>:</p><blockquote>{System.Net.WebUtility.HtmlEncode(body)}</blockquote>",
                    PlainTextBody: $"Hello {ticket.CreatedByName}, Platform Support replied to ticket {ticket.TicketNumber}: {body}",
                    Metadata: new Dictionary<string, string>
                    {
                        ["Type"] = "SupportTicketReply",
                        ["TicketNumber"] = ticket.TicketNumber,
                        ["TenantId"] = ticket.TenantId.ToString()
                    }),
                cancellationToken);
        }

        return await GetSuperAdminTicketByIdAsync(ticket.Id, cancellationToken);
    }

    public async Task<SuperAdminSupportTicketDetailDto> UpdateStatusAsSuperAdminAsync(
        Guid id,
        UpdateSupportTicketStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        RequireSuperAdmin();
        if (!Enum.IsDefined(request.Status))
        {
            throw new DomainValidationException("INVALID_STATUS", "Invalid support ticket status.");
        }

        var actorId = tenantContext.UserId;
        var now = DateTimeOffset.UtcNow;

        using (tenantContext.BeginSystemScope())
        {
            var ticket = await db.SupportTickets
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == id, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(SupportTicket), id.ToString());

            ApplyStatus(ticket, request.Status, now);
            ticket.AssignedToUserId ??= actorId;

            await auditService.LogAsync(
                "SupportTicket.StatusUpdated",
                nameof(SupportTicket),
                ticket.Id.ToString(),
                new { ticket.TicketNumber, Status = ticket.Status.ToString() },
                tenantIdOverride: ticket.TenantId,
                actorUserIdOverride: actorId,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        return await GetSuperAdminTicketByIdAsync(id, cancellationToken);
    }

    private void RequireSuperAdmin()
    {
        if (!tenantContext.IsSuperAdmin)
        {
            throw new DomainValidationException("FORBIDDEN", "Super Admin privileges are required.", 403);
        }
    }

    private async Task<Dictionary<Guid, string?>> LoadPlanNamesByTenantAsync(
        IReadOnlyList<Guid> tenantIds,
        CancellationToken cancellationToken)
    {
        if (tenantIds.Count == 0)
        {
            return new Dictionary<Guid, string?>();
        }

        var subscriptions = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => !s.IsDeleted && tenantIds.Contains(s.TenantId))
            .ToListAsync(cancellationToken);

        return subscriptions
            .GroupBy(s => s.TenantId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.CreatedAt).First().Plan?.Name);
    }

    private static void ApplyStatus(SupportTicket ticket, SupportTicketStatus status, DateTimeOffset now)
    {
        ticket.Status = status;
        switch (status)
        {
            case SupportTicketStatus.Resolved:
                ticket.ResolvedAt ??= now;
                ticket.ClosedAt = null;
                break;
            case SupportTicketStatus.Closed:
                ticket.ClosedAt ??= now;
                break;
            default:
                ticket.ResolvedAt = null;
                ticket.ClosedAt = null;
                break;
        }
    }

    private static SupportTicketDetailDto MapTenantDetail(
        SupportTicket ticket,
        IReadOnlyList<SupportTicketMessageDto> messages) =>
        new(
            ticket.Id,
            ticket.TicketNumber,
            ticket.Subject,
            ticket.Category.ToString(),
            ticket.Status.ToString(),
            ticket.IsPriority,
            ticket.CreatedByName,
            ticket.CreatedByEmail,
            ticket.LastMessageAt,
            ticket.ResolvedAt,
            ticket.ClosedAt,
            ticket.CreatedAt,
            messages);

    private static string BuildPreview(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var normalized = body.Trim().ReplaceLineEndings(" ");
        return normalized.Length <= 120 ? normalized : $"{normalized[..117]}...";
    }
}
