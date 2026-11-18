using EduFlow.Api.Authorization;
using EduFlow.Application.Branding;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public static class BrandingEndpoints
{
    public static IEndpointRouteBuilder MapBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/public/branding", async (
                [FromQuery] string? code,
                HttpContext httpContext,
                IBrandingService branding,
                CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "public, no-cache";
                httpContext.Response.Headers.Vary = "Host, X-Tenant";
                var hasExplicitCode = httpContext.Request.Query.ContainsKey("code");
                return Results.Ok(await branding.GetPublicAsync(code, hasExplicitCode, ct));
            })
            .WithTags("Branding")
            .WithName("GetPublicBranding")
            .RequireRateLimiting("public")
            .AllowAnonymous();

        app.MapGet("/api/branding/current", async (IBrandingService branding, CancellationToken ct) =>
                Results.Ok(await branding.GetCurrentAsync(ct)))
            .WithTags("Branding")
            .WithName("GetCurrentBranding")
            .RequireAuthorization();

        var settings = app.MapGroup("/api/settings/branding")
            .WithTags("Branding")
            .RequireAuthorization();

        settings.MapGet("/", async (IBrandingService branding, CancellationToken ct) =>
            Results.Ok(await branding.GetForEditAsync(platform: false, ct)))
            .WithName("GetTenantBranding");

        settings.MapPut("/", async ([FromBody] UpdateBrandingRequest request, IBrandingService branding, CancellationToken ct) =>
            Results.Ok(await branding.UpdateAsync(false, request, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("UpdateTenantBranding");

        settings.MapPost("/reset", async (IBrandingService branding, CancellationToken ct) =>
            Results.Ok(await branding.ResetAsync(false, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("ResetTenantBranding");

        settings.MapPost("/assets", async (HttpRequest request, IBrandingService branding, CancellationToken ct) =>
            Results.Ok(await SaveAssetAsync(request, branding, platform: false, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .DisableAntiforgery()
            .WithName("UploadTenantBrandingAsset");

        var platform = app.MapGroup("/api/super-admin/branding")
            .WithTags("Branding")
            .RequireSuperAdmin();

        platform.MapGet("/", async (IBrandingService branding, CancellationToken ct) =>
            Results.Ok(await branding.GetForEditAsync(platform: true, ct)))
            .WithName("GetPlatformBranding");

        platform.MapPut("/", async ([FromBody] UpdateBrandingRequest request, IBrandingService branding, CancellationToken ct) =>
            Results.Ok(await branding.UpdateAsync(true, request, ct)))
            .WithName("UpdatePlatformBranding");

        platform.MapPost("/reset", async (IBrandingService branding, CancellationToken ct) =>
            Results.Ok(await branding.ResetAsync(true, ct)))
            .WithName("ResetPlatformBranding");

        platform.MapPost("/assets", async (HttpRequest request, IBrandingService branding, CancellationToken ct) =>
            Results.Ok(await SaveAssetAsync(request, branding, platform: true, ct)))
            .DisableAntiforgery()
            .WithName("UploadPlatformBrandingAsset");

        return app;
    }

    private static async Task<BrandingDto> SaveAssetAsync(
        HttpRequest request,
        IBrandingService branding,
        bool platform,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
        {
            throw new DomainValidationException("INVALID_FORM", "Upload the image as multipart form data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            throw new DomainValidationException("FILE_REQUIRED", "Choose an image to upload.");
        }

        var kind = form["kind"].ToString();
        await using var stream = file.OpenReadStream();
        return await branding.SaveAssetAsync(
            platform,
            kind,
            stream,
            file.FileName,
            file.ContentType,
            cancellationToken);
    }
}
