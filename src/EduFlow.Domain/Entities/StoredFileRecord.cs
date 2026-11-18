using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class StoredFileRecord : TenantEntity
{
    public StoredFileCategory Category { get; set; }
    public Guid? OwnerId { get; set; }
    public string Slot { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long SizeBytes { get; set; }
}
