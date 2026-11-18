using System.Text.RegularExpressions;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Storage;
using EduFlow.Domain;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace EduFlow.Application.Branding;

public sealed partial class BrandingService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    IAuditService auditService,
    IFileStorage fileStorage,
    ILimitService limitService,
    IStoredFileRegistry storedFiles,
    IAntivirusScanner? antivirusScanner = null,
    IMemoryCache? cache = null) : IBrandingService
{
    private const long MaxAssetBytes = 2 * 1024 * 1024;
    private static string CacheKey(Guid tenantId) => $"branding:public:{tenantId:N}";

    public async Task<BrandingDto> GetPublicAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId is Guid id && id != Guid.Empty ? id : Guid.Empty;
        var key = CacheKey(tenantId);
        if (cache is not null && cache.TryGetValue(key, out BrandingDto? cached) && cached is not null)
        {
            return cached;
        }

        BrandingDto result;
        if (tenantId == Guid.Empty)
        {
            result = await MapPlatformAsync(cancellationToken);
        }
        else
        {
            var tenant = await LoadTenantAsync(tenantId, cancellationToken);
            var profile = await FindProfileAsync(tenantId, cancellationToken);

            var canRemove = await CanRemoveProductBrandingAsync(tenantId, cancellationToken);
            result = Map(profile, tenant.Name, tenant.Subdomain, canRemove, isPlatform: false);
        }

        cache?.Set(key, result, TimeSpan.FromMinutes(2));
        return result;
    }

    public async Task<BrandingDto> GetForEditAsync(bool platform, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant(platform);
        if (tenantId == Guid.Empty)
        {
            return await MapPlatformAsync(cancellationToken);
        }

        var tenant = await LoadTenantAsync(tenantId, cancellationToken);
        var profile = await FindProfileAsync(tenantId, cancellationToken);
        var canRemove = await CanRemoveProductBrandingAsync(tenantId, cancellationToken);
        return Map(profile, tenant.Name, tenant.Subdomain, canRemove, isPlatform: false);
    }

    public async Task<BrandingDto> UpdateAsync(
        bool platform,
        UpdateBrandingRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant(platform);
        var profile = await LoadOrCreateTrackedAsync(tenantId, cancellationToken);
        await ApplyAsync(profile, request, cancellationToken);
        await auditService.LogAsync(
            "Branding.Updated",
            nameof(BrandingProfile),
            profile.Id.ToString(),
            new { profile.PrimaryColor, profile.AccentColor, profile.ThemeMode, profile.RemoveProductBranding },
            tenantIdOverride: tenantId == Guid.Empty ? null : tenantId,
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache?.Remove(CacheKey(tenantId));
        return await GetForEditAsync(platform, cancellationToken);
    }

    public async Task<BrandingDto> ResetAsync(bool platform, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant(platform);
        var profile = await LoadOrCreateTrackedAsync(tenantId, cancellationToken);
        profile.PrimaryColor = BrandingProfile.DefaultPrimaryColor;
        profile.AccentColor = BrandingProfile.DefaultAccentColor;
        profile.FontPreference = BrandingProfile.FontInter;
        profile.ThemeMode = BrandingProfile.ThemeLight;
        profile.RemoveProductBranding = false;
        profile.LogoUrl = null;
        profile.FaviconUrl = null;
        profile.LoginBackgroundUrl = null;

        if (tenantId == Guid.Empty)
        {
            profile.InstituteDisplayName = ProductName.Value;
            profile.Tagline = null;
        }
        else
        {
            var tenant = await LoadTenantAsync(tenantId, cancellationToken);
            profile.InstituteDisplayName = tenant.Name;
            profile.Tagline = null;
            tenant.LogoUrl = null;
        }

        await auditService.LogAsync(
            "Branding.Reset",
            nameof(BrandingProfile),
            profile.Id.ToString(),
            null,
            tenantIdOverride: tenantId == Guid.Empty ? null : tenantId,
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache?.Remove(CacheKey(tenantId));
        return await GetForEditAsync(platform, cancellationToken);
    }

    public async Task<BrandingDto> SaveAssetAsync(
        bool platform,
        string kind,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var normalizedKind = kind.Trim().ToLowerInvariant();
        if (normalizedKind is not ("logo" or "favicon" or "background"))
        {
            throw new DomainValidationException("INVALID_ASSET_KIND", "Asset kind must be logo, favicon, or background.");
        }

        var extension = contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => throw new DomainValidationException("INVALID_ASSET_TYPE", "Upload a PNG, JPEG, or WebP image.")
        };

        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxAssetBytes)
            {
                throw new DomainValidationException("FILE_TOO_LARGE", "Image must be 2 MB or smaller.");
            }

            buffer.Write(chunk, 0, read);
        }

        if (total < 12)
        {
            throw new DomainValidationException("INVALID_ASSET_TYPE", "The file contents do not match an allowed image type.");
        }

        await FileUploadValidator.EnsureCleanAndValidSignatureAsync(
            buffer,
            fileName,
            contentType,
            extension,
            "INVALID_ASSET_TYPE",
            "The file contents do not match an allowed image type.",
            antivirusScanner,
            cancellationToken);

        buffer.Position = 0;
        var tenantId = RequireTenant(platform);
        var folder = tenantId == Guid.Empty ? "branding/platform" : $"branding/{tenantId:N}";
        var profile = await LoadOrCreateTrackedAsync(tenantId, cancellationToken);
        if (tenantId != Guid.Empty)
        {
            var replacing = await storedFiles.CurrentSizeAsync(
                tenantId,
                StoredFileCategory.Branding,
                profile.Id,
                normalizedKind,
                cancellationToken);
            await limitService.EnsureStorageAllowsAsync(tenantId, total, replacing, cancellationToken);
        }

        var stored = await fileStorage.SaveAsync(folder, $"{normalizedKind}-{Guid.NewGuid():N}{extension}", buffer, cancellationToken);
        switch (normalizedKind)
        {
            case "logo":
                profile.LogoUrl = stored.Url;
                break;
            case "favicon":
                profile.FaviconUrl = stored.Url;
                break;
            default:
                profile.LoginBackgroundUrl = stored.Url;
                break;
        }

        if (tenantId != Guid.Empty && normalizedKind == "logo")
        {
            var tenant = await LoadTenantAsync(tenantId, cancellationToken);
            tenant.LogoUrl = stored.Url;
        }

        if (tenantId != Guid.Empty)
        {
            await storedFiles.ReplaceAsync(
                tenantId,
                StoredFileCategory.Branding,
                profile.Id,
                normalizedKind,
                stored.Url,
                fileName,
                contentType,
                stored.SizeBytes,
                cancellationToken);
        }

        await auditService.LogAsync(
            "Branding.AssetUploaded",
            nameof(BrandingProfile),
            profile.Id.ToString(),
            new { Kind = normalizedKind, stored.Url },
            tenantIdOverride: tenantId == Guid.Empty ? null : tenantId,
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        cache?.Remove(CacheKey(tenantId));
        return await GetForEditAsync(platform, cancellationToken);
    }

    private async Task ApplyAsync(BrandingProfile profile, UpdateBrandingRequest request, CancellationToken cancellationToken)
    {
        var primary = NormalizeColor(request.PrimaryColor, "Primary color");
        var accent = NormalizeColor(request.AccentColor, "Accent color");
        var font = request.FontPreference?.Trim();
        if (font is not (BrandingProfile.FontInter or BrandingProfile.FontSystem))
        {
            throw new DomainValidationException("INVALID_FONT", "Font must be Inter or System.");
        }

        var theme = request.ThemeMode?.Trim().ToLowerInvariant();
        if (theme is not (BrandingProfile.ThemeLight or BrandingProfile.ThemeDark or BrandingProfile.ThemeSystem))
        {
            throw new DomainValidationException("INVALID_THEME", "Theme must be light, dark, or system.");
        }

        var displayName = request.InstituteDisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 2 or > 200)
        {
            throw new DomainValidationException("INVALID_DISPLAY_NAME", "Display name must be between 2 and 200 characters.");
        }

        var tagline = string.IsNullOrWhiteSpace(request.Tagline) ? null : request.Tagline.Trim();
        if (tagline is { Length: > 300 })
        {
            throw new DomainValidationException("INVALID_TAGLINE", "Tagline must be 300 characters or fewer.");
        }

        ValidateAssetUrl(request.LogoUrl, "Logo");
        ValidateAssetUrl(request.FaviconUrl, "Favicon");
        ValidateAssetUrl(request.LoginBackgroundUrl, "Login background");

        var tenantId = profile.TenantId;
        if (request.RemoveProductBranding)
        {
            if (tenantId == Guid.Empty)
            {
                profile.RemoveProductBranding = true;
            }
            else
            {
                var evaluation = await featureService.EvaluateAsync(tenantId, FeatureKey.RemoveProductBranding, cancellationToken);
                if (!evaluation.IsInPlan)
                {
                    throw new FeatureNotInPlanException(FeatureKey.RemoveProductBranding);
                }

                if (!evaluation.IsEnabled)
                {
                    throw new FeatureDisabledException(FeatureKey.RemoveProductBranding);
                }

                profile.RemoveProductBranding = true;
            }
        }
        else
        {
            profile.RemoveProductBranding = false;
        }

        profile.PrimaryColor = primary;
        profile.AccentColor = accent;
        profile.FontPreference = font;
        profile.ThemeMode = theme;
        profile.InstituteDisplayName = displayName;
        profile.Tagline = tagline;
        profile.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        profile.FaviconUrl = string.IsNullOrWhiteSpace(request.FaviconUrl) ? null : request.FaviconUrl.Trim();
        profile.LoginBackgroundUrl = string.IsNullOrWhiteSpace(request.LoginBackgroundUrl) ? null : request.LoginBackgroundUrl.Trim();

        if (tenantId != Guid.Empty)
        {
            var tenant = await LoadTenantAsync(tenantId, cancellationToken);
            tenant.LogoUrl = profile.LogoUrl;
        }
    }

    private async Task<BrandingProfile> LoadOrCreateTrackedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = await db.BrandingProfiles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.TenantId == tenantId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        string displayName;
        if (tenantId == Guid.Empty)
        {
            displayName = ProductName.Value;
        }
        else
        {
            displayName = (await LoadTenantAsync(tenantId, cancellationToken)).Name;
        }

        var created = new BrandingProfile
        {
            TenantId = tenantId,
            InstituteDisplayName = displayName
        };
        db.BrandingProfiles.Add(created);
        return created;
    }

    private async Task<BrandingDto> MapPlatformAsync(CancellationToken cancellationToken)
    {
        var profile = await FindProfileAsync(Guid.Empty, cancellationToken);
        return Map(profile, ProductName.Value, null, canRemove: true, isPlatform: true);
    }

    private async Task<Tenant> LoadTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            throw new EntityNotFoundException(nameof(Tenant));
        }

        return tenant;
    }

    private async Task<bool> CanRemoveProductBrandingAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return true;
        }

        var evaluation = await featureService.EvaluateAsync(tenantId, FeatureKey.RemoveProductBranding, cancellationToken);
        return evaluation.IsEnabled;
    }

    private async Task<BrandingProfile?> FindProfileAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return await db.BrandingProfiles
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.TenantId == tenantId, cancellationToken);
    }

    private Guid RequireTenant(bool platform)
    {
        if (platform)
        {
            if (!tenantContext.IsSuperAdmin)
            {
                throw new DomainValidationException("FORBIDDEN", "Only a super admin can edit platform branding.", 403);
            }

            return Guid.Empty;
        }

        if (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            throw new DomainValidationException("TENANT_REQUIRED", "Tenant context is required.", 400);
        }

        return tenantContext.TenantId.Value;
    }

    private static BrandingDto Map(
        BrandingProfile? profile,
        string fallbackName,
        string? subdomain,
        bool canRemove,
        bool isPlatform)
    {
        return new BrandingDto(
            profile?.PrimaryColor ?? BrandingProfile.DefaultPrimaryColor,
            profile?.AccentColor ?? BrandingProfile.DefaultAccentColor,
            profile?.LogoUrl,
            profile?.FaviconUrl,
            profile?.LoginBackgroundUrl,
            string.IsNullOrWhiteSpace(profile?.InstituteDisplayName) ? fallbackName : profile.InstituteDisplayName,
            profile?.Tagline,
            profile?.FontPreference ?? BrandingProfile.FontInter,
            profile?.ThemeMode ?? BrandingProfile.ThemeLight,
            profile?.RemoveProductBranding ?? false,
            canRemove,
            subdomain,
            isPlatform);
    }

    private static string NormalizeColor(string? value, string label)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || !HexColor().IsMatch(trimmed))
        {
            throw new DomainValidationException("INVALID_COLOR", $"{label} must be a #RRGGBB color.");
        }

        return trimmed.ToUpperInvariant();
    }

    private static void ValidateAssetUrl(string? url, string label)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var trimmed = url.Trim();
        var allowed = trimmed.StartsWith("/files/", StringComparison.Ordinal)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!allowed)
        {
            throw new DomainValidationException("INVALID_ASSET_URL", $"{label} must be an uploaded file or an https URL.");
        }
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
