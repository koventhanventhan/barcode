using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Enums;

namespace EduFlow.Infrastructure.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public string? TenantSubdomain { get; private set; }
    public Guid? UserId { get; private set; }
    public string? UserEmail { get; private set; }
    public RoleType? Role { get; private set; }
    public bool IsSuperAdmin => Role == RoleType.SuperAdmin && !IsImpersonating;
    public bool IsImpersonating { get; private set; }
    public bool IsReadOnly { get; private set; }
    public bool IsSystemScope { get; private set; }
    public string? IpAddress { get; private set; }

    public void SetTenant(Guid tenantId, string? subdomain = null)
    {
        TenantId = tenantId;
        if (subdomain is not null)
        {
            TenantSubdomain = subdomain;
        }
    }

    public void SetUser(
        Guid userId,
        string? email,
        RoleType role,
        bool isImpersonating = false,
        bool isReadOnly = false,
        string? ipAddress = null)
    {
        UserId = userId;
        UserEmail = email;
        Role = role;
        IsImpersonating = isImpersonating;
        IsReadOnly = isReadOnly;
        if (ipAddress is not null)
        {
            IpAddress = ipAddress;
        }
    }

    public IDisposable BeginSystemScope(Guid? tenantId = null)
    {
        var previousSystemScope = IsSystemScope;
        var previousTenantId = TenantId;

        IsSystemScope = true;
        if (tenantId.HasValue)
        {
            TenantId = tenantId.Value;
        }

        return new ScopeRestore(() =>
        {
            IsSystemScope = previousSystemScope;
            TenantId = previousTenantId;
        });
    }

    private sealed class ScopeRestore(Action onDispose) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                onDispose();
            }
        }
    }
}
