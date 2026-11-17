using System.Security.Claims;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Api.Middleware;

public sealed class SubscriptionGuardMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        IAppDbContext db)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (IsExemptPath(path))
        {
            await next(context);
            return;
        }

        if (tenantContext.IsSuperAdmin)
        {
            await next(context);
            return;
        }

        var platformSetting = await db.PlatformSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.Key == "default", context.RequestAborted);

        if (platformSetting is { IsMaintenanceMode: true })
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "MAINTENANCE_MODE",
                message = platformSetting.MaintenanceMessage ?? "Platform is currently undergoing scheduled maintenance."
            });
            return;
        }

        var isReadOnlyHttpMethod = HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method);

        if (tenantContext.IsReadOnly && !isReadOnlyHttpMethod)
        {
            var demoSession = string.Equals(
                context.User.FindFirstValue("demo_session"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = demoSession ? "DEMO_READ_ONLY" : "IMPERSONATION_READ_ONLY",
                message = demoSession
                    ? "This shared demo is read-only. Start a free trial to make changes."
                    : "Super Admin impersonation session is strictly read-only."
            });
            return;
        }

        if (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            await next(context);
            return;
        }

        var tenantId = tenantContext.TenantId.Value;

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, context.RequestAborted);

        if (tenant is null || !tenant.IsActive)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "SUBSCRIPTION_SUSPENDED",
                message = "This institute account is currently inactive or suspended."
            });
            return;
        }

        var subscription = await db.Subscriptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => !s.IsDeleted && s.TenantId == tenantId)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(context.RequestAborted);

        if (subscription is not null)
        {
            if (subscription.Status == SubscriptionStatus.Suspended)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "SUBSCRIPTION_SUSPENDED",
                    message = subscription.SuspendedReason ?? "Your institute's subscription is suspended."
                });
                return;
            }

            if (subscription.IsEffectivelyExpired(DateTimeOffset.UtcNow) && !isReadOnlyHttpMethod)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "SUBSCRIPTION_EXPIRED_READ_ONLY",
                    message = "Your subscription has expired. Your institute is in read-only mode until a subscription is activated."
                });
                return;
            }
        }

        await next(context);
    }

    private static bool IsExemptPath(string path)
    {
        return path.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/info", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/diagnostics", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/super-admin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/plans", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/public", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/fees/payhere/webhook", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/payments/payhere/webhook", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);
    }
}
