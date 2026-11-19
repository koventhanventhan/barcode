using System.Net;
using System.Security.Claims;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Api.Middleware;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        IAppDbContext db)
    {
        PopulateUserContext(context, tenantContext);

        var (resolvedTenantId, resolvedSubdomain) = await ResolveFromHostOrHeaderAsync(context, db);

        Guid? jwtTenantId = null;
        var jwtTenantClaim = context.User.FindFirstValue("tenant_id");
        if (!string.IsNullOrWhiteSpace(jwtTenantClaim) && Guid.TryParse(jwtTenantClaim, out var parsedJwtTenant))
        {
            jwtTenantId = parsedJwtTenant;
        }

        if (jwtTenantId.HasValue && resolvedTenantId.HasValue && jwtTenantId.Value != resolvedTenantId.Value)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "CROSS_TENANT_ACCESS",
                message = "The authenticated token belongs to a different tenant than the requested tenant."
            });
            return;
        }

        var effectiveTenantId = resolvedTenantId ?? jwtTenantId;
        var effectiveSubdomain = resolvedSubdomain ?? context.User.FindFirstValue("tenant_subdomain");

        if (effectiveTenantId.HasValue && effectiveTenantId.Value != Guid.Empty)
        {
            tenantContext.SetTenant(effectiveTenantId.Value, effectiveSubdomain);
        }

        await next(context);
    }

    private static void PopulateUserContext(HttpContext context, ITenantContext tenantContext)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return;
        }

        var email = context.User.FindFirstValue(ClaimTypes.Email)
            ?? context.User.FindFirstValue("email");
        var roleStr = context.User.FindFirstValue(ClaimTypes.Role)
            ?? context.User.FindFirstValue("role");
        var role = Enum.TryParse<RoleType>(roleStr, ignoreCase: true, out var parsedRole)
            ? parsedRole
            : RoleType.Staff;

        var isImpersonating = string.Equals(
            context.User.FindFirstValue("impersonating"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var isReadOnly = string.Equals(
            context.User.FindFirstValue("read_only"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var remoteIp = context.Connection.RemoteIpAddress;
        var ip = remoteIp is null
            ? null
            : (remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4().ToString() : remoteIp.ToString());

        tenantContext.SetUser(userId, email, role, isImpersonating, isReadOnly, ip);
    }

    private static async Task<(Guid? TenantId, string? Subdomain)> ResolveFromHostOrHeaderAsync(
        HttpContext context,
        IAppDbContext db)
    {
        var subdomainFromHost = ExtractSubdomain(context.Request.Host.Host);
        if (!string.IsNullOrWhiteSpace(subdomainFromHost))
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => !t.IsDeleted && t.Subdomain == subdomainFromHost)
                .Select(t => new { t.Id, t.Subdomain })
                .FirstOrDefaultAsync(context.RequestAborted);

            if (tenant is not null)
            {
                return (tenant.Id, tenant.Subdomain);
            }
        }

        if (context.Request.Headers.TryGetValue("X-Tenant", out var headerValues))
        {
            var rawHeader = headerValues.FirstOrDefault()?.Trim();
            if (!string.IsNullOrWhiteSpace(rawHeader))
            {
                if (Guid.TryParse(rawHeader, out var headerTenantId))
                {
                    var tenant = await db.Tenants
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(t => !t.IsDeleted && t.Id == headerTenantId)
                        .Select(t => new { t.Id, t.Subdomain })
                        .FirstOrDefaultAsync(context.RequestAborted);

                    if (tenant is not null)
                    {
                        return (tenant.Id, tenant.Subdomain);
                    }
                }
                else
                {
                    var slug = rawHeader.ToLowerInvariant();
                    var tenant = await db.Tenants
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(t => !t.IsDeleted && t.Subdomain == slug)
                        .Select(t => new { t.Id, t.Subdomain })
                        .FirstOrDefaultAsync(context.RequestAborted);

                    if (tenant is not null)
                    {
                        return (tenant.Id, tenant.Subdomain);
                    }
                }
            }
        }

        return (null, null);
    }

    private static string? ExtractSubdomain(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        var normalized = host.Trim().ToLowerInvariant();
        if (normalized == "localhost" || IPAddress.TryParse(normalized, out _))
        {
            return null;
        }

        if (normalized.EndsWith(".localhost", StringComparison.Ordinal))
        {
            var sub = normalized[..^".localhost".Length];
            return string.IsNullOrWhiteSpace(sub) || sub == "www" || sub.Contains('.') ? null : sub;
        }

        var parts = normalized.Split('.');
        if (parts.Length >= 3)
        {
            var candidate = parts[0];
            if (candidate != "www" && candidate != "api")
            {
                return candidate;
            }
        }

        return null;
    }
}
