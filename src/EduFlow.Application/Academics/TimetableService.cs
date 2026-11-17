using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Academics;

public interface ITimetableService
{
    Task<WeeklyTimetableDto> GetWeeklyTimetableAsync(
        Guid? batchId = null,
        Guid? teacherUserId = null,
        string? room = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimetableClashDto>> CheckClashesAsync(
        CheckTimetableClashesRequest request,
        CancellationToken cancellationToken = default);

    Task<ClassScheduleDto> CreateSlotAsync(
        UpsertClassScheduleRequest request,
        CancellationToken cancellationToken = default);

    Task<ClassScheduleDto> UpdateSlotAsync(
        Guid id,
        UpsertClassScheduleRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteSlotAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class TimetableService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    IAcademicService academicService) : ITimetableService
{
    public async Task<WeeklyTimetableDto> GetWeeklyTimetableAsync(
        Guid? batchId = null,
        Guid? teacherUserId = null,
        string? room = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureTimetableEnabledAsync(cancellationToken);
        var allSchedules = await academicService.GetSchedulesAsync(cancellationToken);
        var existingSlots = allSchedules
            .Select(s => new ExistingTimetableSlot(
                s.Id,
                s.Name,
                s.BatchId,
                s.BatchName,
                s.SubjectId,
                s.SubjectName,
                s.TeacherUserId,
                s.TeacherName,
                s.Weekday,
                s.StartMinutes,
                s.EndMinutes,
                s.Room,
                s.IsActive))
            .ToList();

        var clashes = TimetableRules.DetectAllGridClashes(existingSlots);

        var filtered = allSchedules.AsEnumerable();
        if (batchId.HasValue)
        {
            filtered = filtered.Where(s => s.BatchId == batchId.Value);
        }

        if (teacherUserId.HasValue)
        {
            filtered = filtered.Where(s => s.TeacherUserId == teacherUserId.Value);
        }

        if (!string.IsNullOrWhiteSpace(room))
        {
            var r = room.Trim();
            filtered = filtered.Where(s => !string.IsNullOrWhiteSpace(s.Room) && string.Equals(s.Room.Trim(), r, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = filtered.ToList();
        var days = Enumerable.Range(1, 7)
            .Select(day => new TimetableDayColumnDto(
                day,
                filteredList
                    .Where(s => s.Weekday == day)
                    .OrderBy(s => s.StartMinutes)
                    .ThenBy(s => s.Name)
                    .ToList()))
            .ToList();

        var rooms = allSchedules
            .Where(s => !string.IsNullOrWhiteSpace(s.Room))
            .Select(s => s.Room!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new WeeklyTimetableDto(days, clashes, rooms);
    }

    public async Task<IReadOnlyList<TimetableClashDto>> CheckClashesAsync(
        CheckTimetableClashesRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTimetableEnabledAsync(cancellationToken);
        var allSchedules = await academicService.GetSchedulesAsync(cancellationToken);
        var batchName = await db.AcademicBatches.AsNoTracking()
            .Where(b => b.Id == request.BatchId)
            .Select(b => b.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? "Batch";
        var teacherName = await db.Users.AsNoTracking()
            .Where(u => u.Id == request.TeacherUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? "Teacher";

        var existingSlots = allSchedules
            .Select(s => new ExistingTimetableSlot(
                s.Id,
                s.Name,
                s.BatchId,
                s.BatchName,
                s.SubjectId,
                s.SubjectName,
                s.TeacherUserId,
                s.TeacherName,
                s.Weekday,
                s.StartMinutes,
                s.EndMinutes,
                s.Room,
                s.IsActive))
            .ToList();

        return TimetableRules.DetectClashes(
            new TimetableCandidateSlot(
                request.ExcludeScheduleId,
                request.BatchId,
                batchName,
                request.TeacherUserId,
                teacherName,
                request.Weekday,
                request.StartMinutes,
                request.EndMinutes,
                request.Room),
            existingSlots);
    }

    public async Task<ClassScheduleDto> CreateSlotAsync(
        UpsertClassScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTimetableEnabledAsync(cancellationToken);
        return await academicService.CreateScheduleAsync(request, cancellationToken);
    }

    public async Task<ClassScheduleDto> UpdateSlotAsync(
        Guid id,
        UpsertClassScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureTimetableEnabledAsync(cancellationToken);
        return await academicService.UpdateScheduleAsync(id, request, cancellationToken);
    }

    public async Task DeleteSlotAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureTimetableEnabledAsync(cancellationToken);
        await academicService.DeleteScheduleAsync(id, cancellationToken);
    }

    private async Task EnsureTimetableEnabledAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var eval = await featureService.EvaluateAsync(tenantId, FeatureKey.Timetable, cancellationToken);
        if (!eval.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.Timetable);
        }

        if (!eval.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.Timetable);
        }
    }
}
