namespace EduFlow.Application.Parents;

public static class PhoneNumber
{
    public const string PlaceholderDomain = ".parent.admino.local";

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length < 9)
        {
            return string.Empty;
        }

        if (digits.StartsWith("94", StringComparison.Ordinal) && digits.Length >= 11)
        {
            return "+" + digits;
        }

        if (digits.StartsWith('0'))
        {
            return "+94" + digits[1..];
        }

        return "+" + digits;
    }

    public static bool LooksLikePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('@'))
        {
            return false;
        }

        return value.Count(char.IsDigit) >= 9;
    }

    public static string PlaceholderEmail(string normalizedPhone, Guid tenantId)
    {
        var digits = normalizedPhone.TrimStart('+');
        return $"{digits}@{tenantId:N}{PlaceholderDomain}";
    }

    public static bool IsPlaceholderEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.EndsWith(PlaceholderDomain, StringComparison.OrdinalIgnoreCase);
}
