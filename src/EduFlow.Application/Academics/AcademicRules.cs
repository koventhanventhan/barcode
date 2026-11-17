using System.Globalization;

namespace EduFlow.Application.Academics;

public static class StudentCodes
{
    public static string Format(int sequence) => $"S{sequence.ToString("000000", CultureInfo.InvariantCulture)}";

    public static int Next(IEnumerable<string> existingCodes)
    {
        var max = 0;
        foreach (var code in existingCodes)
        {
            if (code.Length == 7
                && code[0] == 'S'
                && int.TryParse(code.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                && value > max)
            {
                max = value;
            }
        }

        return max + 1;
    }
}

public static class ContactRules
{
    public static bool IsPhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var digits = value.Count(char.IsDigit);
        if (digits is < 9 or > 15)
        {
            return false;
        }

        return value.All(c => char.IsDigit(c) || c is ' ' or '+' or '-' or '(' or ')');
    }

    public static string Digits(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

    public static string Identity(string name, string phone) =>
        $"{name.Trim().ToLowerInvariant()}|{Digits(phone)}";
}

public static class ColomboClock
{
    public static DateOnly Today()
    {
        foreach (var id in new[] { "Asia/Colombo", "Sri Lanka Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone));
            }
        }

        return DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
    }
}
