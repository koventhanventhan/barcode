using EduFlow.Application.Auth;
using EduFlow.Application.Marketing;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public static class PublicEndpoints
{
    public static IEndpointRouteBuilder MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public")
            .WithTags("Public")
            .RequireRateLimiting("auth");

        group.MapGet("/subdomains/{subdomain}/availability", async (
            string subdomain,
            IPublicSiteService site,
            CancellationToken ct) => Results.Ok(await site.CheckSubdomainAsync(subdomain, ct)))
            .WithName("CheckSubdomainAvailability");

        group.MapPost("/signup", async (
            [FromBody] SignupRequest request,
            IPublicSiteService site,
            CancellationToken ct) => Results.Ok(await site.SignupAsync(request, ct)))
            .WithName("PublicSignup");

        group.MapPost("/demo-requests", async (
            [FromBody] DemoRequestInput request,
            IPublicSiteService site,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var notify = configuration["Seed:SuperAdmin:Email"]
                ?? Environment.GetEnvironmentVariable("SUPERADMIN_EMAIL")
                ?? "superadmin@admino.lk";
            return Results.Ok(await site.RequestDemoAsync(request, notify, ct));
        }).WithName("RequestDemo");

        group.MapPost("/demo/login", async (
            IPublicSiteService site,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var result = await site.StartDemoSessionAsync(ct);
            AuthEndpoints.AppendRefreshTokenCookie(httpContext, result.RefreshToken);
            return Results.Ok(result);
        }).WithName("StartDemoSession");

        var onboarding = app.MapGroup("/api/onboarding")
            .WithTags("Onboarding")
            .RequireAuthorization();

        onboarding.MapGet("/", async (IPublicSiteService site, CancellationToken ct) =>
            Results.Ok(await site.GetOnboardingAsync(ct)))
            .WithName("GetOnboardingChecklist");

        onboarding.MapPost("/cards-printed", async (IPublicSiteService site, CancellationToken ct) =>
        {
            await site.MarkCardsPrintedAsync(ct);
            return Results.Ok(await site.GetOnboardingAsync(ct));
        }).WithName("MarkIdCardsPrinted");

        return app;
    }
}
