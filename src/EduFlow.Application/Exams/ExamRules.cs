using System.Globalization;
using EduFlow.Application.Attendance;
using EduFlow.Domain.Exceptions;

namespace EduFlow.Application.Exams;

public sealed record GradeBandInput(string Grade, decimal MinPercent, decimal MaxPercent, decimal? GradePoint, int SortOrder);

public sealed record ScoreRow(Guid Id, decimal Score, bool Ranked);

public sealed record RankAssignment(Guid Id, int? Rank);

public static class ExamRules
{
    public const decimal PercentStep = 0.01m;

    public static readonly IReadOnlyList<GradeBandInput> DefaultBands =
    [
        new("F", 0m, 34.99m, 0m, 1),
        new("S", 35m, 49.99m, 1m, 2),
        new("C", 50m, 64.99m, 2m, 3),
        new("B", 65m, 74.99m, 3m, 4),
        new("A", 75m, 100m, 4m, 5)
    ];

    public static void ValidateBands(IReadOnlyList<GradeBandInput> bands)
    {
        if (bands.Count == 0)
        {
            throw new DomainValidationException("GRADE_BANDS_REQUIRED", "Add at least one grade boundary.");
        }

        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var band in bands)
        {
            if (string.IsNullOrWhiteSpace(band.Grade) || band.Grade.Trim().Length > 8)
            {
                throw new DomainValidationException("INVALID_GRADE", "Each grade label is required and must be 8 characters or fewer.");
            }

            if (!labels.Add(band.Grade.Trim()))
            {
                throw new DomainValidationException("DUPLICATE_GRADE", $"Grade '{band.Grade.Trim()}' is listed more than once.");
            }

            if (band.MinPercent < 0 || band.MaxPercent > 100 || band.MinPercent > band.MaxPercent)
            {
                throw new DomainValidationException("INVALID_GRADE_RANGE", $"Grade '{band.Grade.Trim()}' must sit between 0 and 100.");
            }
        }

        var ordered = bands.OrderBy(b => b.MinPercent).ThenBy(b => b.MaxPercent).ToList();
        if (ordered[0].MinPercent != 0m || ordered[^1].MaxPercent != 100m)
        {
            throw new DomainValidationException(
                "GRADE_BANDS_MUST_COVER",
                "Grade boundaries must cover every mark from 0.00 to 100.00.");
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            var expected = ordered[i - 1].MaxPercent + PercentStep;
            if (ordered[i].MinPercent != expected)
            {
                throw new DomainValidationException(
                    "GRADE_BANDS_MUST_MEET",
                    "Grade boundaries must meet at each 0.01 step without gaps or overlaps.");
            }
        }
    }

    public static string ResolveGrade(decimal percentage, IReadOnlyList<GradeBandInput> bands)
    {
        var pct = decimal.Round(percentage, 2, MidpointRounding.AwayFromZero);
        if (pct < 0 || pct > 100)
        {
            throw new DomainValidationException("INVALID_PERCENTAGE", "A percentage must be between 0 and 100.");
        }

        var match = bands
            .Where(b => pct >= b.MinPercent && pct <= b.MaxPercent)
            .OrderByDescending(b => b.MinPercent)
            .FirstOrDefault();

        return match?.Grade.Trim()
            ?? throw new DomainValidationException("GRADE_NOT_FOUND", $"No grade boundary covers {pct:0.00}%.");
    }

    public static decimal Percentage(decimal obtained, decimal max)
    {
        if (max <= 0)
        {
            throw new DomainValidationException("INVALID_MAX_MARKS", "Maximum marks must be greater than zero.");
        }

        if (obtained < 0)
        {
            throw new DomainValidationException("INVALID_MARKS", "Marks cannot be negative.");
        }

        return decimal.Round(obtained / max * 100m, 2, MidpointRounding.AwayFromZero);
    }

    public static IReadOnlyList<RankAssignment> AssignCompetitionRanks(IReadOnlyList<ScoreRow> rows)
    {
        var map = rows.ToDictionary(r => r.Id, _ => (int?)null);
        var position = 0;
        decimal? previous = null;
        var currentRank = 0;

        foreach (var row in rows.Where(r => r.Ranked).OrderByDescending(r => r.Score).ThenBy(r => r.Id))
        {
            position++;
            if (previous is null || row.Score != previous.Value)
            {
                currentRank = position;
                previous = row.Score;
            }

            map[row.Id] = currentRank;
        }

        return rows.Select(r => new RankAssignment(r.Id, map[r.Id])).ToList();
    }

    public static bool IsReminderDue(
        DateTimeOffset nowUtc,
        DateTimeOffset scheduledAtUtc,
        int daysBefore,
        int hoursBefore,
        bool alreadySent)
    {
        if (alreadySent || daysBefore < 0 || hoursBefore < 0)
        {
            return false;
        }

        if (daysBefore == 0 && hoursBefore == 0)
        {
            return false;
        }

        var openAt = scheduledAtUtc - TimeSpan.FromDays(daysBefore) - TimeSpan.FromHours(hoursBefore);
        return nowUtc >= openAt && nowUtc < scheduledAtUtc;
    }

    public static string FormatExamWhen(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, AttendanceRules.ColomboZone());
        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    public static string NormalizeSeriesKey(string? seriesKey, string examName)
    {
        var raw = string.IsNullOrWhiteSpace(seriesKey) ? examName : seriesKey;
        var trimmed = raw.Trim().ToLowerInvariant();
        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }
}
