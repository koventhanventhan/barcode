using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.SuperAdmin;

public sealed record TenantAddOnDto(
    Guid Id,
    string Type,
    int Quantity,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool IsActive,
    string? Note);

public sealed record GrantTenantAddOnRequest(
    TenantAddOnType Type,
    int Quantity,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    string? Note = null);

public sealed partial class SuperAdminService
{
    public async Task<IReadOnlyList<TenantAddOnDto>> GetAddOnsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        await RequireTenantAsync(tenantId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var rows = await db.TenantAddOns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(addOn => !addOn.IsDeleted && addOn.TenantId == tenantId)
            .OrderByDescending(addOn => addOn.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(addOn => MapAddOn(addOn, now)).ToList();
    }

    public async Task<TenantAddOnDto> GrantAddOnAsync(
        Guid tenantId,
        GrantTenantAddOnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Type))
        {
            throw new DomainValidationException("ADDON_TYPE", "Choose extra storage, extra students, or extra SMS segments.");
        }

        if (request.Quantity is < 1 or > 100_000)
        {
            throw new DomainValidationException("ADDON_QUANTITY", "Quantity must be between 1 and 100000.");
        }

        var validFrom = request.ValidFrom ?? DateTimeOffset.UtcNow;
        if (request.ValidTo is DateTimeOffset validTo && validTo <= validFrom)
        {
            throw new DomainValidationException("ADDON_DATES", "The end date must be after the start date.");
        }

        var note = request.Note?.Trim();
        if (note is { Length: > 300 })
        {
            throw new DomainValidationException("ADDON_NOTE", "Keep the note under 300 characters.");
        }

        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(s => s.Plan)
            .Where(s => !s.IsDeleted && s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Subscription), tenantId.ToString());

        if (request.Type == TenantAddOnType.ExtraStudents
            && !string.Equals(subscription.Plan?.Code, "Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException(
                "ADDON_NOT_ALLOWED",
                "Extra students can be granted only on the Enterprise plan.");
        }

        var addOn = new TenantAddOn
        {
            TenantId = tenantId,
            Type = request.Type,
            Quantity = request.Quantity,
            ValidFrom = validFrom,
            ValidTo = request.ValidTo,
            Note = string.IsNullOrWhiteSpace(note) ? null : note
        };

        using (tenantContext.BeginSystemScope(tenantId))
        {
            db.TenantAddOns.Add(addOn);
            await auditService.LogAsync(
                "SuperAdmin.AddOnGranted",
                nameof(TenantAddOn),
                addOn.Id.ToString(),
                new { tenantId, addOn.Type, addOn.Quantity, addOn.ValidFrom, addOn.ValidTo },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return MapAddOn(addOn, DateTimeOffset.UtcNow);
    }

    public async Task RevokeAddOnAsync(Guid tenantId, Guid addOnId, CancellationToken cancellationToken = default)
    {
        var addOn = await db.TenantAddOns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => !item.IsDeleted && item.TenantId == tenantId && item.Id == addOnId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(TenantAddOn), addOnId.ToString());

        using (tenantContext.BeginSystemScope(tenantId))
        {
            db.TenantAddOns.Remove(addOn);
            await auditService.LogAsync(
                "SuperAdmin.AddOnRevoked",
                nameof(TenantAddOn),
                addOn.Id.ToString(),
                new { tenantId, addOn.Type, addOn.Quantity },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RequireTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var exists = await db.Tenants.IgnoreQueryFilters().AnyAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);
        if (!exists)
        {
            throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());
        }
    }

    private static TenantAddOnDto MapAddOn(TenantAddOn addOn, DateTimeOffset now) =>
        new(
            addOn.Id,
            addOn.Type.ToString(),
            addOn.Quantity,
            addOn.ValidFrom,
            addOn.ValidTo,
            addOn.ValidFrom <= now && (addOn.ValidTo is null || addOn.ValidTo > now),
            addOn.Note);
}
