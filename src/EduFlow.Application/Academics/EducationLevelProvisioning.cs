using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Academics;

public static class EducationLevelProvisioning
{
    public static async Task EnsureSelectionsAsync(
        IAppDbContext db,
        Guid tenantId,
        int? maxEnabled,
        CancellationToken cancellationToken)
    {
        var catalog = await db.EducationLevels
            .IgnoreQueryFilters()
            .Where(level => !level.IsDeleted)
            .OrderBy(level => level.SortOrder)
            .ToListAsync(cancellationToken);

        if (catalog.Count == 0)
        {
            throw new DomainValidationException("LEVELS_NOT_SEEDED", "Education levels are not available yet.");
        }

        var exists = await db.TenantEducationLevels
            .IgnoreQueryFilters()
            .AnyAsync(row => !row.IsDeleted && row.TenantId == tenantId, cancellationToken);

        if (exists)
        {
            return;
        }

        var enableCount = maxEnabled ?? catalog.Count;
        if (enableCount < 0)
        {
            enableCount = 0;
        }

        if (enableCount > catalog.Count)
        {
            enableCount = catalog.Count;
        }

        for (var i = 0; i < catalog.Count; i++)
        {
            db.TenantEducationLevels.Add(new TenantEducationLevel
            {
                TenantId = tenantId,
                EducationLevelId = catalog[i].Id,
                IsEnabled = i < enableCount
            });
        }
    }
}
