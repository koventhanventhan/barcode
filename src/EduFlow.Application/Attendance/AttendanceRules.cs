using System.Globalization;

namespace EduFlow.Application.Attendance;

public static class AttendanceRules
{
    public const int DefaultLateAfterMinutes = 10;
    public const string OutcomeMarked = "Marked";
    public const string OutcomeAlreadyMarked = "AlreadyMarked";

    private const string JoinAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static bool IsLate(DateTimeOffset startedAt, DateTimeOffset scannedAt, int lateAfterMinutes)
    {
        var threshold = Math.Clamp(lateAfterMinutes, 0, 180);
        return scannedAt > startedAt.AddMinutes(threshold);
    }

    public static string FormatColombo(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, ColomboZone());
        return local.ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    public static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string NewJoinCode()
    {
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = JoinAlphabet[Random.Shared.Next(JoinAlphabet.Length)];
        }

        return new string(chars);
    }

    public static TimeZoneInfo ColomboZone()
    {
        foreach (var id in new[] { "Asia/Colombo", "Sri Lanka Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
            {
                return zone;
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("Asia/Colombo", TimeSpan.FromHours(5.5), "Asia/Colombo", "Asia/Colombo");
    }
}
