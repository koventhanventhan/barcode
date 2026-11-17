using EduFlow.Api.Endpoints;

namespace EduFlow.Api.Middleware;

public sealed class CookieCsrfMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (IsProtectedCookieMutation(context.Request))
        {
            var secFetchSite = context.Request.Headers["Sec-Fetch-Site"].FirstOrDefault();
            if (string.Equals(secFetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "CSRF_ORIGIN_MISMATCH",
                    message = "Cross-site cookie request blocked."
                });
                return;
            }

            var originHeader = context.Request.Headers.Origin.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(originHeader))
            {
                var referer = context.Request.Headers.Referer.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
                {
                    originHeader = $"{refererUri.Scheme}://{refererUri.Authority}";
                }
            }

            if (!string.IsNullOrWhiteSpace(originHeader) && !IsTrustedOrigin(originHeader, context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    code = "CSRF_ORIGIN_MISMATCH",
                    message = "Cross-site origin is not permitted for cookie authentication."
                });
                return;
            }
        }

        await next(context);
    }

    private static bool IsProtectedCookieMutation(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method)
            || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method))
        {
            return false;
        }

        if (!request.Path.StartsWithSegments("/api/auth/refresh", StringComparison.OrdinalIgnoreCase)
            && !request.Path.StartsWithSegments("/api/auth/logout", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return request.Cookies.ContainsKey(AuthEndpoints.RefreshTokenCookieName);
    }

    private bool IsTrustedOrigin(string originHeader, HttpRequest request)
    {
        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        if (string.Equals(originUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (originUri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || request.Host.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var configuredOrigins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        foreach (var configured in configuredOrigins)
        {
            if (!Uri.TryCreate(configured, UriKind.Absolute, out var allowedUri))
            {
                continue;
            }

            if (string.Equals(originUri.Authority, allowedUri.Authority, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (originUri.Host.EndsWith("." + allowedUri.Host, StringComparison.OrdinalIgnoreCase)
                && string.Equals(originUri.Scheme, allowedUri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
