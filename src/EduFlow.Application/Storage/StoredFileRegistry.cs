using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Storage;

public sealed class StoredFileRegistry(IAppDbContext db) : IStoredFileRegistry
{
    public async Task<long> CurrentSizeAsync(
        Guid tenantId,
        StoredFileCategory category,
        Guid ownerId,
        string slot,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.StoredFileRecords
            .IgnoreQueryFilters()
            .Where(file => !file.IsDeleted
                           && file.TenantId == tenantId
                           && file.Category == category
                           && file.OwnerId == ownerId
                           && file.Slot == slot)
            .Select(file => file.SizeBytes)
            .ToListAsync(cancellationToken);

        long total = 0;
        foreach (var size in rows)
        {
            total += size;
        }

        return total;
    }

    public async Task ReplaceAsync(
        Guid tenantId,
        StoredFileCategory category,
        Guid ownerId,
        string slot,
        string storageKey,
        string fileName,
        string? contentType,
        long sizeBytes,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.StoredFileRecords
            .IgnoreQueryFilters()
            .Where(file => !file.IsDeleted
                           && file.TenantId == tenantId
                           && file.Category == category
                           && file.OwnerId == ownerId
                           && file.Slot == slot)
            .ToListAsync(cancellationToken);

        foreach (var file in existing)
        {
            db.StoredFileRecords.Remove(file);
        }

        db.StoredFileRecords.Add(new StoredFileRecord
        {
            TenantId = tenantId,
            Category = category,
            OwnerId = ownerId,
            Slot = slot,
            StorageKey = storageKey,
            FileName = string.IsNullOrWhiteSpace(fileName) ? slot : fileName.Trim(),
            ContentType = contentType,
            SizeBytes = sizeBytes
        });
    }
}
