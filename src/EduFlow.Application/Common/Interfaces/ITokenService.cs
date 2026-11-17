using EduFlow.Domain.Entities;

namespace EduFlow.Application.Common.Interfaces;

public sealed record GeneratedAccessToken(
    string Token,
    DateTimeOffset ExpiresAt);

public interface ITokenService
{
    GeneratedAccessToken GenerateAccessToken(
        User user,
        IReadOnlyList<string> permissions,
        string? tenantSubdomain = null,
        bool readOnlyDemo = false);

    GeneratedAccessToken GenerateReadOnlyImpersonationToken(
        User superAdmin,
        Tenant targetTenant);
}
