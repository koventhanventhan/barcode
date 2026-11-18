using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class TenantAddOn : TenantEntity
{
    public TenantAddOnType Type { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ValidTo { get; set; }
    public string? Note { get; set; }
}
