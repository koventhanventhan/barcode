using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

public sealed class TenantOnboarding : TenantEntity
{
    public DateTimeOffset? CardsPrintedAt { get; set; }
}
