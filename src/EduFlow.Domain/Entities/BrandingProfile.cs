using EduFlow.Domain.Common;

namespace EduFlow.Domain.Entities;

/// <summary>
/// Institute or platform visual identity. TenantId Guid.Empty is the platform default.
/// Not an <see cref="ITenantEntity"/> because the platform row has no tenant.
/// </summary>
public sealed class BrandingProfile : BaseEntity
{
    public const string DefaultPrimaryColor = "#1F4E46";
    public const string DefaultAccentColor = "#C45C26";
    public const string FontInter = "Inter";
    public const string FontSystem = "System";
    public const string ThemeLight = "light";
    public const string ThemeDark = "dark";
    public const string ThemeSystem = "system";

    public Guid TenantId { get; set; }
    public string PrimaryColor { get; set; } = DefaultPrimaryColor;
    public string AccentColor { get; set; } = DefaultAccentColor;
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? LoginBackgroundUrl { get; set; }
    public string InstituteDisplayName { get; set; } = string.Empty;
    public string? Tagline { get; set; }
    public string FontPreference { get; set; } = FontInter;
    public string ThemeMode { get; set; } = ThemeLight;
    public bool RemoveProductBranding { get; set; }
}
