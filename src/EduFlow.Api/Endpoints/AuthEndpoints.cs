using System.Security.Claims;
using EduFlow.Application.Auth;
using EduFlow.Application.Common.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public static class AuthEndpoints
{
    public const string RefreshTokenCookieName = "admino_refresh_token";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Auth")
            .RequireRateLimiting("auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest request,
            IAuthService authService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await authService.LoginAsync(request, ct);
            AppendRefreshTokenCookie(httpContext, result.RefreshToken);
            return Results.Ok(result);
        }).WithName("Login");

        group.MapPost("/refresh", async (
            [FromBody] RefreshRequest? request,
            IAuthService authService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var token = request?.RefreshToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                httpContext.Request.Cookies.TryGetValue(RefreshTokenCookieName, out token);
            }

            var result = await authService.RefreshAsync(token ?? string.Empty, ct);
            AppendRefreshTokenCookie(httpContext, result.RefreshToken);
            return Results.Ok(result);
        }).WithName("RefreshToken");

        group.MapPost("/logout", async (
            [FromBody] LogoutRequest? request,
            IAuthService authService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var token = request?.RefreshToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                httpContext.Request.Cookies.TryGetValue(RefreshTokenCookieName, out token);
            }

            await authService.LogoutAsync(token, ct);
            httpContext.Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions { Path = "/api/auth" });
            return Results.Ok(new { message = "Logged out." });
        }).WithName("Logout");

        group.MapPost("/change-password", async (
            [FromBody] ChangePasswordRequest request,
            IAuthService authService,
            CancellationToken ct) =>
        {
            await authService.ChangePasswordAsync(request, ct);
            return Results.Ok(new { message = "Password updated." });
        })
        .RequireAuthorization()
        .WithName("ChangePassword");

        group.MapPost("/forgot-password", async (
            [FromBody] ForgotPasswordRequest request,
            IAuthService authService,
            CancellationToken ct) =>
        {
            await authService.ForgotPasswordAsync(request, ct);
            return Results.Ok(new { message = "If the account exists, a password reset link has been sent." });
        }).WithName("ForgotPassword");

        group.MapPost("/reset-password", async (
            [FromBody] ResetPasswordRequest request,
            IAuthService authService,
            CancellationToken ct) =>
        {
            await authService.ResetPasswordAsync(request, ct);
            return Results.Ok(new { message = "Password has been reset." });
        }).WithName("ResetPassword");

        group.MapPost("/invite", async (
            [FromBody] InviteUserRequest request,
            IAuthService authService,
            CancellationToken ct) =>
        {
            var response = await authService.InviteUserAsync(request, ct);
            return Results.Created($"/api/users/{response.UserId}", response);
        })
        .RequireAuthorization("Permission:users.manage")
        .WithName("InviteUser");

        group.MapPost("/accept-invite", async (
            [FromBody] AcceptInviteRequest request,
            IAuthService authService,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await authService.AcceptInviteAsync(request, ct);
            AppendRefreshTokenCookie(httpContext, result.RefreshToken);
            return Results.Ok(result);
        }).WithName("AcceptInvite");

        group.MapGet("/me", (HttpContext httpContext, ITenantContext tenantContext) =>
        {
            var permissions = httpContext.User.FindAll("permission").Select(c => c.Value).ToList();
            return Results.Ok(new
            {
                userId = tenantContext.UserId,
                tenantId = tenantContext.TenantId,
                email = tenantContext.UserEmail,
                role = tenantContext.Role?.ToString(),
                isImpersonating = tenantContext.IsImpersonating,
                isReadOnly = tenantContext.IsReadOnly,
                permissions
            });
        })
        .RequireAuthorization()
        .WithName("GetCurrentUser");

        return app;
    }

    public static void AppendRefreshTokenCookie(HttpContext httpContext, string refreshToken)
    {
        httpContext.Response.Cookies.Append(
            RefreshTokenCookieName,
            refreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/api/auth",
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
    }
}
