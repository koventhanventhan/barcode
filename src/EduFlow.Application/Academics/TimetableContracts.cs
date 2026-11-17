using EduFlow.Domain.Enums;

namespace EduFlow.Application.Academics;

public sealed record TimetableClashDto(
    TimetableClashType ClashType,
    Guid ConflictingScheduleId,
    string ConflictingScheduleName,
    int Weekday,
    int StartMinutes,
    int EndMinutes,
    string Message);

public sealed record TimetableDayColumnDto(
    int Weekday,
    IReadOnlyList<ClassScheduleDto> Slots);

public sealed record WeeklyTimetableDto(
    IReadOnlyList<TimetableDayColumnDto> Days,
    IReadOnlyList<TimetableClashDto> Clashes,
    IReadOnlyList<string> Rooms);

public sealed record CheckTimetableClashesRequest(
    Guid? ExcludeScheduleId,
    Guid BatchId,
    Guid SubjectId,
    Guid TeacherUserId,
    int Weekday,
    int StartMinutes,
    int EndMinutes,
    string? Room);
