using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EduFlow.Application.Common.Security;

public static partial class PiiRedactor
{
    private static readonly HashSet<string> SensitiveKeyFragments = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwordhash",
        "secret",
        "merchantsecret",
        "encryptedmerchantsecret",
        "token",
        "refreshtoken",
        "accesstoken",
        "apikey",
        "authorization",
        "cookie",
        "md5sig"
    };

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        var trimmed = email.Trim();
        var atIndex = trimmed.IndexOf('@');
        if (atIndex <= 0 || atIndex == trimmed.Length - 1)
        {
            return "***";
        }

        var local = trimmed[..atIndex];
        var domain = trimmed[(atIndex + 1)..];
        var maskedLocal = local.Length switch
        {
            1 => "*",
            2 => $"{local[0]}*",
            _ => $"{local[0]}***{local[^1]}"
        };

        return $"{maskedLocal}@{domain}";
    }

    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }

        var trimmed = phone.Trim();
        if (trimmed.Length <= 4)
        {
            return "****";
        }

        var prefixLength = Math.Min(4, trimmed.Length - 4);
        var prefix = trimmed[..prefixLength];
        var suffix = trimmed[^3..];
        return $"{prefix}***{suffix}";
    }

    public static string RedactQueryString(string? queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
        {
            return string.Empty;
        }

        return SensitiveQueryParamRegex().Replace(queryString, "$1[REDACTED]");
    }

    public static string RedactSecretValues(string? text, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = RedactQueryString(text);
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrWhiteSpace(secret) && secret.Length >= 4)
            {
                result = result.Replace(secret, "[REDACTED]", StringComparison.Ordinal);
            }
        }

        return result;
    }

    public static string? SerializeAuditDetails(object? details)
    {
        if (details is null)
        {
            return null;
        }

        var json = JsonSerializer.Serialize(details);
        var node = JsonNode.Parse(json);
        if (node is null)
        {
            return json;
        }

        RedactNode(node);
        return node.ToJsonString();
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kvp => kvp.Key).ToList())
            {
                var value = obj[key];
                if (IsSensitiveKey(key))
                {
                    obj[key] = "[REDACTED]";
                }
                else if (value is not null)
                {
                    RedactNode(value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    RedactNode(item);
                }
            }
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
        return SensitiveKeyFragments.Any(fragment => normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"([?&](?:token|access_token|refresh_token|password|secret|email|phone|code|api_key|apikey)=)[^&]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveQueryParamRegex();
}
