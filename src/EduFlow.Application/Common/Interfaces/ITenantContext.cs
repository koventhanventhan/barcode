using EduFlow.Domain.Enums;

namespace EduFlow.Application.Common.Interfaces;

public interface ITenantContext
{
    Guid? TenantId { get; }
    string? TenantSubdomain { get; }
    Guid? UserId { get; }
    string? UserEmail { get; }
    RoleType? Role { get; }
    bool IsSuperAdmin { get; }
    bool IsImpersonating { get; }
    bool IsReadOnly { get; }
    bool IsSystemScope { get; }
    string? IpAddress { get; }

    void SetTenant(Guid tenantId, string? subdomain = null);
    void SetUser(
        Guid userId,
        string? email,
        RoleType role,
        bool isImpersonating = false,
        bool isReadOnly = false,
        string? ipAddress = null);
    IDisposable BeginSystemScope(Guid? tenantId = null);
}
