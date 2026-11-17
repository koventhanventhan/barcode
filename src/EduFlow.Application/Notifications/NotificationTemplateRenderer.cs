using System.Text.RegularExpressions;
using EduFlow.Domain.Constants;

namespace EduFlow.Application.Notifications;

public static partial class NotificationTemplateRenderer
{
    [GeneratedRegex(@"\{(?<key>[a-zA-Z0-9_]+)\}", RegexOptions.Compiled)]
    private static partial Regex TokenRegex();

    public static string Render(
        string template,
        IReadOnlyDictionary<string, string>? variables,
        bool fallbackToSample = false)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return TokenRegex().Replace(template, match =>
        {
            var key = match.Groups["key"].Value;
            if (variables is not null)
            {
                if (variables.TryGetValue(key, out var val) && val is not null)
                {
                    return val;
                }

                var matched = variables.FirstOrDefault(kv => string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase));
                if (matched.Key is not null && matched.Value is not null)
                {
                    return matched.Value;
                }
            }

            if (fallbackToSample && NotificationCatalog.SampleVariables.TryGetValue(key, out var sampleVal))
            {
                return sampleVal;
            }

            return match.Value;
        });
    }
}

public static class QuietHoursCalculator
{
    public const string DefaultTimeZoneId = "Asia/Colombo";

    public static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim(),
            DefaultTimeZoneId,
            "Sri Lanka Standard Time"
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone(
            DefaultTimeZoneId,
            TimeSpan.FromMinutes(330),
            DefaultTimeZoneId,
            DefaultTimeZoneId);
    }

    public static bool IsWithinQuietHours(
        DateTimeOffset timestampUtc,
        string? timeZoneId,
        int startMinutes,
        int endMinutes)
    {
        if (startMinutes == endMinutes)
        {
            return false;
        }

        var tz = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(timestampUtc, tz);
        var currentMinutes = local.Hour * 60 + local.Minute;

        return startMinutes > endMinutes
            ? currentMinutes >= startMinutes || currentMinutes < endMinutes
            : currentMinutes >= startMinutes && currentMinutes < endMinutes;
    }

    public static DateTimeOffset GetQuietHoursEndUtc(
        DateTimeOffset timestampUtc,
        string? timeZoneId,
        int startMinutes,
        int endMinutes)
    {
        var tz = ResolveTimeZone(timeZoneId);
        var local = TimeZoneInfo.ConvertTime(timestampUtc, tz);
        var currentMinutes = local.Hour * 60 + local.Minute;

        DateTime targetDate = local.Date;
        if (startMinutes > endMinutes && currentMinutes >= startMinutes)
        {
            targetDate = targetDate.AddDays(1);
        }

        var localEnd = DateTime.SpecifyKind(targetDate.AddMinutes(endMinutes), DateTimeKind.Unspecified);
        var offset = tz.GetUtcOffset(localEnd);
        return new DateTimeOffset(localEnd, offset).ToUniversalTime();
    }

    public static string FormatMinutes(int minutes)
    {
        var clamped = Math.Clamp(minutes, 0, 24 * 60 - 1);
        var hrs = clamped / 60;
        var mins = clamped % 60;
        return $"{hrs:00}:{mins:00}";
    }
}
