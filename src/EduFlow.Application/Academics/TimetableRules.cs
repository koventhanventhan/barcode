using System.Globalization;
using EduFlow.Domain.Enums;

namespace EduFlow.Application.Academics;

public sealed record ExistingTimetableSlot(
    Guid Id,
    string Name,
    Guid BatchId,
    string BatchName,
    Guid SubjectId,
    string SubjectName,
    Guid TeacherUserId,
    string TeacherName,
    int Weekday,
    int StartMinutes,
    int EndMinutes,
    string? Room,
    bool IsActive);

public sealed record TimetableCandidateSlot(
    Guid? ExcludeScheduleId,
    Guid BatchId,
    string BatchName,
    Guid TeacherUserId,
    string TeacherName,
    int Weekday,
    int StartMinutes,
    int EndMinutes,
    string? Room,
    bool IsActive = true);

public static class TimetableRules
{
    public static bool Overlaps(int startA, int endA, int startB, int endB) =>
        startA < endB && startB < endA;

    public static string FormatMinutes(int minutes)
    {
        var clamped = Math.Clamp(minutes, 0, 1440);
        var h = clamped / 60;
        var m = clamped % 60;
        return $"{h.ToString("00", CultureInfo.InvariantCulture)}:{m.ToString("00", CultureInfo.InvariantCulture)}";
    }

    public static IReadOnlyList<TimetableClashDto> DetectClashes(
        TimetableCandidateSlot candidate,
        IEnumerable<ExistingTimetableSlot> existingSlots)
    {
        if (!candidate.IsActive)
        {
            return [];
        }

        var clashes = new List<TimetableClashDto>();
        var normalizedRoom = string.IsNullOrWhiteSpace(candidate.Room)
            ? null
            : candidate.Room.Trim();

        foreach (var slot in existingSlots)
        {
            if (!slot.IsActive
                || (candidate.ExcludeScheduleId.HasValue && slot.Id == candidate.ExcludeScheduleId.Value)
                || slot.Weekday != candidate.Weekday
                || !Overlaps(candidate.StartMinutes, candidate.EndMinutes, slot.StartMinutes, slot.EndMinutes))
            {
                continue;
            }

            var timeWindow = $"{FormatMinutes(slot.StartMinutes)}–{FormatMinutes(slot.EndMinutes)}";

            if (slot.TeacherUserId == candidate.TeacherUserId)
            {
                clashes.Add(new TimetableClashDto(
                    TimetableClashType.Teacher,
                    slot.Id,
                    slot.Name,
                    slot.Weekday,
                    slot.StartMinutes,
                    slot.EndMinutes,
                    $"Teacher '{candidate.TeacherName}' already has '{slot.Name}' at {timeWindow}."));
            }

            if (slot.BatchId == candidate.BatchId)
            {
                clashes.Add(new TimetableClashDto(
                    TimetableClashType.Batch,
                    slot.Id,
                    slot.Name,
                    slot.Weekday,
                    slot.StartMinutes,
                    slot.EndMinutes,
                    $"Batch '{candidate.BatchName}' already has '{slot.Name}' at {timeWindow}."));
            }

            if (normalizedRoom is not null
                && !string.IsNullOrWhiteSpace(slot.Room)
                && string.Equals(normalizedRoom, slot.Room.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                clashes.Add(new TimetableClashDto(
                    TimetableClashType.Room,
                    slot.Id,
                    slot.Name,
                    slot.Weekday,
                    slot.StartMinutes,
                    slot.EndMinutes,
                    $"Room '{normalizedRoom}' is already booked for '{slot.Name}' at {timeWindow}."));
            }
        }

        return clashes;
    }

    public static IReadOnlyList<TimetableClashDto> DetectAllGridClashes(IReadOnlyList<ExistingTimetableSlot> slots)
    {
        var active = slots.Where(s => s.IsActive).OrderBy(s => s.Weekday).ThenBy(s => s.StartMinutes).ToList();
        var results = new List<TimetableClashDto>();

        for (var i = 0; i < active.Count; i++)
        {
            var a = active[i];
            for (var j = i + 1; j < active.Count; j++)
            {
                var b = active[j];
                if (a.Weekday != b.Weekday || !Overlaps(a.StartMinutes, a.EndMinutes, b.StartMinutes, b.EndMinutes))
                {
                    continue;
                }

                var candidate = new TimetableCandidateSlot(
                    a.Id,
                    a.BatchId,
                    a.BatchName,
                    a.TeacherUserId,
                    a.TeacherName,
                    a.Weekday,
                    a.StartMinutes,
                    a.EndMinutes,
                    a.Room,
                    true);

                results.AddRange(DetectClashes(candidate, [b]));
            }
        }

        return results;
    }
}
