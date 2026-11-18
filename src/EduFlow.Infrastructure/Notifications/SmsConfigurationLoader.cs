using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace EduFlow.Infrastructure.Notifications;

public sealed record SmsConfigurationMetadata(IReadOnlyList<string> DeprecatedKeysDetected);

public static class SmsConfigurationLoader
{
    private static readonly string[] LegacyKeys =
    [
        "Notifications:Sms:Provider",
        "Notifications:Sms:UserId",
        "Notifications:Sms:ApiKey",
        "Notifications:Sms:SenderId",
        "Notifications:Sms:PromotionalSenderId",
        "Notifications:Sms:BaseUrl",
        "Notifications:Sms:TestMode",
        "Notifications:Sms:TestAllowList",
        "Notifications:Sms:LowBalanceThreshold",
        "Notifications:Sms:NotifyLk:Endpoint",
        "Notifications:Sms:NotifyLk:UserId",
        "Notifications:Sms:NotifyLk:ApiKey",
        "Notifications:Sms:NotifyLk:SenderId"
    ];

    public static IReadOnlyList<string> DetectDeprecatedKeys(IConfiguration configuration)
    {
        var detected = new List<string>();
        foreach (var key in LegacyKeys)
        {
            if (!string.IsNullOrWhiteSpace(configuration[key]))
            {
                detected.Add(key);
            }
        }

        return detected;
    }

    public static void ApplyTo(SmsProviderOptions target, IConfiguration configuration)
    {
        var provider = FirstNonBlank(
            configuration["Sms:Provider"],
            configuration["Notifications:Sms:Provider"]);
        if (provider is not null)
        {
            target.Provider = provider;
        }

        var userId = FirstNonBlank(
            configuration["Sms:UserId"],
            configuration["Notifications:Sms:UserId"],
            configuration["Notifications:Sms:NotifyLk:UserId"]);
        if (userId is not null)
        {
            target.UserId = userId;
        }

        var apiKey = FirstNonBlank(
            configuration["Sms:ApiKey"],
            configuration["Notifications:Sms:ApiKey"],
            configuration["Notifications:Sms:NotifyLk:ApiKey"]);
        if (apiKey is not null)
        {
            target.ApiKey = apiKey;
        }

        var senderId = FirstNonBlank(
            configuration["Sms:SenderId"],
            configuration["Notifications:Sms:SenderId"],
            configuration["Notifications:Sms:NotifyLk:SenderId"]);
        if (senderId is not null)
        {
            target.SenderId = senderId;
        }

        var promotionalSenderId = FirstNonBlank(
            configuration["Sms:PromotionalSenderId"],
            configuration["Notifications:Sms:PromotionalSenderId"]);
        if (promotionalSenderId is not null)
        {
            target.PromotionalSenderId = promotionalSenderId;
        }

        var baseUrl = FirstNonBlank(
            configuration["Sms:BaseUrl"],
            configuration["Notifications:Sms:BaseUrl"],
            NormalizeLegacyEndpoint(configuration["Notifications:Sms:NotifyLk:Endpoint"]));
        if (baseUrl is not null)
        {
            target.BaseUrl = baseUrl;
        }

        var testAllowList = FirstNonBlank(
            configuration["Sms:TestAllowList"],
            configuration["Notifications:Sms:TestAllowList"]);
        if (testAllowList is not null)
        {
            target.TestAllowList = testAllowList;
        }

        var testModeRaw = FirstNonBlank(
            configuration["Sms:TestMode"],
            configuration["Notifications:Sms:TestMode"]);
        if (testModeRaw is not null && bool.TryParse(testModeRaw, out var parsedTestMode))
        {
            target.TestMode = parsedTestMode;
        }

        var lowBalRaw = FirstNonBlank(
            configuration["Sms:LowBalanceThreshold"],
            configuration["Notifications:Sms:LowBalanceThreshold"]);
        if (lowBalRaw is not null
            && decimal.TryParse(lowBalRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedLowBal))
        {
            target.LowBalanceThreshold = parsedLowBal;
        }
    }

    private static string? NormalizeLegacyEndpoint(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        var trimmed = endpoint.Trim().TrimEnd('/');
        if (trimmed.EndsWith("/send", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^"/send".Length];
        }

        return trimmed;
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
