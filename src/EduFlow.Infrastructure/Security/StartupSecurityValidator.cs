using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace EduFlow.Infrastructure.Security;

public static class StartupSecurityValidator
{
    public const string KnownDefaultSuperAdminEmail = "superadmin@admino.lk";
    public const string KnownDefaultSuperAdminPassword = "SuperAdmin@123!";
    public const int MinimumJwtSigningKeyLength = 32;
    public const int MinimumSuperAdminPasswordLength = 12;

    public static bool IsDevelopmentOrTesting(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing");

    public static void ValidateStartupConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ValidateJwtSigningKey(configuration, environment);
        ValidateAllowedHosts(configuration, environment);
    }

    public static void ValidateJwtSigningKey(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var rawKey = configuration[$"{JwtOptions.SectionName}:SigningKey"] ?? string.Empty;
        var trimmedKey = rawKey.Trim();

        if (string.IsNullOrWhiteSpace(trimmedKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is required. Configure a secret signing key of at least 32 characters.");
        }

        if (trimmedKey.Length < MinimumJwtSigningKeyLength
            || Encoding.UTF8.GetByteCount(trimmedKey) < MinimumJwtSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {MinimumJwtSigningKeyLength} characters (bytes) long.");
        }

        if (!IsDevelopmentOrTesting(environment)
            && string.Equals(trimmedKey, JwtOptions.KnownDevSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must not use the known development signing key outside Development and Testing environments.");
        }
    }

    public static IReadOnlyList<string> ParseAllowedHosts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .ToList();
    }

    public static void ValidateAllowedHosts(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (IsDevelopmentOrTesting(environment))
        {
            return;
        }

        var hosts = ParseAllowedHosts(configuration["AllowedHosts"]);
        if (hosts.Count == 0 || hosts.Any(h => string.Equals(h, "*", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "AllowedHosts must list explicit hostnames and must not contain '*' outside Development and Testing environments.");
        }
    }

    public static (string Email, string Password, string FullName) ResolveAndValidateSuperAdminCredentials(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var fullName = configuration["Seed:SuperAdmin:FullName"] ?? "Admino Super Admin";

        if (IsDevelopmentOrTesting(environment))
        {
            var devEmail = FirstNonBlank(
                configuration["Seed:SuperAdmin:Email"],
                configuration["SUPERADMIN_EMAIL"],
                KnownDefaultSuperAdminEmail);

            var devPassword = FirstNonBlank(
                configuration["Seed:SuperAdmin:Password"],
                configuration["SUPERADMIN_PASSWORD"],
                KnownDefaultSuperAdminPassword);

            return (devEmail.Trim().ToLowerInvariant(), devPassword, fullName);
        }

        var prodEmail = configuration["SUPERADMIN_EMAIL"];
        if (string.IsNullOrWhiteSpace(prodEmail))
        {
            throw new InvalidOperationException(
                "SUPERADMIN_EMAIL is required outside Development and Testing environments.");
        }

        var prodPassword = configuration["SUPERADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(prodPassword))
        {
            throw new InvalidOperationException(
                "SUPERADMIN_PASSWORD is required outside Development and Testing environments.");
        }

        if (prodPassword.Length < MinimumSuperAdminPasswordLength)
        {
            throw new InvalidOperationException(
                $"SUPERADMIN_PASSWORD must be at least {MinimumSuperAdminPasswordLength} characters long outside Development and Testing environments.");
        }

        if (string.Equals(prodPassword, KnownDefaultSuperAdminPassword, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SUPERADMIN_PASSWORD must not equal the known default development password outside Development and Testing environments.");
        }

        return (prodEmail.Trim().ToLowerInvariant(), prodPassword, fullName);
    }

    public static bool ShouldSeedDemoData(
        IConfiguration configuration,
        IHostEnvironment environment) =>
        environment.IsDevelopment() || configuration.GetValue<bool>("Demo:Enabled");

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
