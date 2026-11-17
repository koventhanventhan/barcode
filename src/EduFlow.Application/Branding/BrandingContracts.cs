namespace EduFlow.Application.Branding;

public sealed record BrandingDto(
    string PrimaryColor,
    string AccentColor,
    string? LogoUrl,
    string? FaviconUrl,
    string? LoginBackgroundUrl,
    string InstituteDisplayName,
    string? Tagline,
    string FontPreference,
    string ThemeMode,
    bool RemoveProductBranding,
    bool CanRemoveProductBranding,
    string? Subdomain,
    bool IsPlatformDefault);

public sealed record UpdateBrandingRequest(
    string PrimaryColor,
    string AccentColor,
    string? LogoUrl,
    string? FaviconUrl,
    string? LoginBackgroundUrl,
    string InstituteDisplayName,
    string? Tagline,
    string FontPreference,
    string ThemeMode,
    bool RemoveProductBranding);

public interface IBrandingService
{
    Task<BrandingDto> GetPublicAsync(CancellationToken cancellationToken = default);
    Task<BrandingDto> GetForEditAsync(bool platform, CancellationToken cancellationToken = default);
    Task<BrandingDto> UpdateAsync(bool platform, UpdateBrandingRequest request, CancellationToken cancellationToken = default);
    Task<BrandingDto> ResetAsync(bool platform, CancellationToken cancellationToken = default);
    Task<BrandingDto> SaveAssetAsync(
        bool platform,
        string kind,
        Stream content,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
