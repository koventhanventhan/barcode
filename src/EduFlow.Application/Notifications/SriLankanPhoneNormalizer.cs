namespace EduFlow.Application.Notifications;

public sealed record PhoneNormalizationResult(
    bool IsValid,
    string? Canonical947,
    string? ErrorCode,
    string? ErrorMessage);

public static class SriLankanPhoneNormalizer
{
    public static PhoneNormalizationResult NormalizeForGateway(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new PhoneNormalizationResult(
                false,
                null,
                "INVALID_NUMBER",
                "Recipient mobile number is required.");
        }

        var cleaned = raw.Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);

        if (cleaned.Length == 0)
        {
            return new PhoneNormalizationResult(
                false,
                null,
                "INVALID_NUMBER",
                "Recipient mobile number is required.");
        }

        var hasPlus = cleaned[0] == '+';
        var body = hasPlus ? cleaned[1..] : cleaned;

        if (body.Length == 0 || body.Any(c => !char.IsAsciiDigit(c)))
        {
            return new PhoneNormalizationResult(
                false,
                null,
                "INVALID_NUMBER",
                "Invalid phone number format. Use 07XXXXXXXX, +947XXXXXXXX, or 947XXXXXXXX.");
        }

        if (hasPlus)
        {
            if (body.Length == 11 && body.StartsWith("947", StringComparison.Ordinal))
            {
                return new PhoneNormalizationResult(true, body, null, null);
            }

            if (body.Length == 11 && body.StartsWith("94", StringComparison.Ordinal))
            {
                return new PhoneNormalizationResult(
                    false,
                    null,
                    "INVALID_NUMBER",
                    "Landline or non-mobile numbers cannot receive SMS. Use a Sri Lankan mobile number (+947XXXXXXXX).");
            }

            return new PhoneNormalizationResult(
                false,
                null,
                "INVALID_NUMBER",
                "Invalid Sri Lankan mobile number. Accepted formats: 07XXXXXXXX, +947XXXXXXXX, or 947XXXXXXXX.");
        }

        if (body.StartsWith("0", StringComparison.Ordinal))
        {
            if (body.Length == 10 && body.StartsWith("07", StringComparison.Ordinal))
            {
                return new PhoneNormalizationResult(true, "94" + body[1..], null, null);
            }

            if (body.Length == 10)
            {
                return new PhoneNormalizationResult(
                    false,
                    null,
                    "INVALID_NUMBER",
                    "Landline numbers cannot receive SMS. Use a Sri Lankan mobile number (07XXXXXXXX).");
            }

            return new PhoneNormalizationResult(
                false,
                null,
                "INVALID_NUMBER",
                "Invalid Sri Lankan mobile number. Accepted formats: 07XXXXXXXX, +947XXXXXXXX, or 947XXXXXXXX.");
        }

        if (body.StartsWith("94", StringComparison.Ordinal))
        {
            if (body.Length == 11 && body.StartsWith("947", StringComparison.Ordinal))
            {
                return new PhoneNormalizationResult(true, body, null, null);
            }

            if (body.Length == 11)
            {
                return new PhoneNormalizationResult(
                    false,
                    null,
                    "INVALID_NUMBER",
                    "Landline or non-mobile numbers cannot receive SMS. Use a Sri Lankan mobile number (947XXXXXXXX).");
            }
        }

        return new PhoneNormalizationResult(
            false,
            null,
            "INVALID_NUMBER",
            "Invalid Sri Lankan mobile number. Accepted formats: 07XXXXXXXX, +947XXXXXXXX, or 947XXXXXXXX.");
    }

    public static IReadOnlyList<string> ParseAllowList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var items = raw.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var set = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var result = NormalizeForGateway(item);
            if (result.IsValid && result.Canonical947 is not null && seen.Add(result.Canonical947))
            {
                set.Add(result.Canonical947);
            }
        }

        return set;
    }

    public static bool IsInAllowList(string? phone, IEnumerable<string> allowList)
    {
        var normalized = NormalizeForGateway(phone);
        if (!normalized.IsValid || normalized.Canonical947 is null)
        {
            return false;
        }

        foreach (var entry in allowList)
        {
            var entryNormalized = NormalizeForGateway(entry);
            var candidate = entryNormalized.IsValid ? entryNormalized.Canonical947 : entry.Trim();
            if (string.Equals(normalized.Canonical947, candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
