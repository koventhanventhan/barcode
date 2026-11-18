using EduFlow.Domain.Enums;

namespace EduFlow.Application.Storage;

public interface IStoredFileRegistry
{
    Task<long> CurrentSizeAsync(
        Guid tenantId,
        StoredFileCategory category,
        Guid ownerId,
        string slot,
        CancellationToken cancellationToken = default);

    Task ReplaceAsync(
        Guid tenantId,
        StoredFileCategory category,
        Guid ownerId,
        string slot,
        string storageKey,
        string fileName,
        string? contentType,
        long sizeBytes,
        CancellationToken cancellationToken = default);
}
