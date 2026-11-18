using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EduFlow.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const string KnownDevSigningKey = "Admino_Dev_Signing_Key_Must_Be_At_Least_32_Bytes_Long!";

    public string Issuer { get; set; } = "Admino";
    public string Audience { get; set; } = "AdminoClient";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public GeneratedAccessToken GenerateAccessToken(
        User user,
        IReadOnlyList<string> permissions,
        string? tenantSubdomain = null,
        bool readOnlyDemo = false)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.RoleType.ToString())
        };

        if (user.TenantId != Guid.Empty)
        {
            claims.Add(new Claim("tenant_id", user.TenantId.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(tenantSubdomain))
        {
            claims.Add(new Claim("tenant_subdomain", tenantSubdomain));
        }

        foreach (var permission in permissions.Distinct())
        {
            claims.Add(new Claim("permission", permission));
        }

        if (readOnlyDemo)
        {
            claims.Add(new Claim("read_only", "true"));
            claims.Add(new Claim("demo_session", "true"));
        }

        return WriteToken(claims, expiresAt);
    }

    public GeneratedAccessToken GenerateReadOnlyImpersonationToken(User superAdmin, Tenant targetTenant)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, superAdmin.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, superAdmin.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier, superAdmin.Id.ToString()),
            new(ClaimTypes.Email, superAdmin.Email),
            new(ClaimTypes.Name, $"{superAdmin.FullName} (Impersonating {targetTenant.Subdomain})"),
            new(ClaimTypes.Role, RoleType.InstituteAdmin.ToString()),
            new("tenant_id", targetTenant.Id.ToString()),
            new("tenant_subdomain", targetTenant.Subdomain),
            new("impersonating", "true"),
            new("read_only", "true"),
            new("actor_id", superAdmin.Id.ToString())
        };

        foreach (var perm in Permissions.All)
        {
            claims.Add(new Claim("permission", perm.Key));
        }

        return WriteToken(claims, expiresAt);
    }

    private GeneratedAccessToken WriteToken(IEnumerable<Claim> claims, DateTimeOffset expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return new GeneratedAccessToken(encoded, expiresAt);
    }
}
