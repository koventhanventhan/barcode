using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Exceptions;

namespace EduFlow.Application.Academics;

internal static class TenantGuard
{
    public static Guid Require(ITenantContext tenantContext) =>
        tenantContext.TenantId is { } tenantId && tenantId != Guid.Empty
            ? tenantId
            : throw new DomainValidationException("TENANT_REQUIRED", "Tenant context is required.");
}
