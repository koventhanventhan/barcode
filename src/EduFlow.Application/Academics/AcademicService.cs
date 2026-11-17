using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Academics;

public interface IAcademicService
{
    Task<IReadOnlyList<EducationLevelDto>> GetLevelsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EducationLevelDto>> SetEnabledLevelsAsync(UpdateLevelSelectionRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BatchGroupDto>> GetBatchGroupsAsync(CancellationToken cancellationToken = default);
    Task<BatchGroupDto> CreateBatchGroupAsync(UpsertBatchGroupRequest request, CancellationToken cancellationToken = default);
    Task<BatchGroupDto> UpdateBatchGroupAsync(Guid id, UpsertBatchGroupRequest request, CancellationToken cancellationToken = default);
    Task DeleteBatchGroupAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BatchDto>> GetBatchesAsync(CancellationToken cancellationToken = default);
    Task<BatchDto> CreateBatchAsync(UpsertBatchRequest request, CancellationToken cancellationToken = default);
    Task<BatchDto> UpdateBatchAsync(Guid id, UpsertBatchRequest request, CancellationToken cancellationToken = default);
    Task DeleteBatchAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken = default);
    Task<SubjectDto> CreateSubjectAsync(UpsertSubjectRequest request, CancellationToken cancellationToken = default);
    Task<SubjectDto> UpdateSubjectAsync(Guid id, UpsertSubjectRequest request, CancellationToken cancellationToken = default);
    Task DeleteSubjectAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClassScheduleDto>> GetSchedulesAsync(CancellationToken cancellationToken = default);
    Task<ClassScheduleDto> CreateScheduleAsync(UpsertClassScheduleRequest request, CancellationToken cancellationToken = default);
    Task<ClassScheduleDto> UpdateScheduleAsync(Guid id, UpsertClassScheduleRequest request, CancellationToken cancellationToken = default);
    Task DeleteScheduleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TeacherOptionDto>> GetTeachersAsync(CancellationToken cancellationToken = default);
}

public sealed class AcademicService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    ILimitService limitService,
    IAuditService auditService) : IAcademicService
{
    public async Task<IReadOnlyList<EducationLevelDto>> GetLevelsAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var catalog = await db.EducationLevels.AsNoTracking().OrderBy(level => level.SortOrder).ToListAsync(cancellationToken);
        var enabled = await db.TenantEducationLevels.AsNoTracking()
            .Where(row => row.IsEnabled)
            .Select(row => row.EducationLevelId)
            .ToListAsync(cancellationToken);
        var enabledSet = enabled.ToHashSet();
        return catalog.Select(level => new EducationLevelDto(
            level.Id,
            level.Code,
            level.Name,
            level.Description,
            level.SortOrder,
            enabledSet.Contains(level.Id))).ToList();
    }

    public async Task<IReadOnlyList<EducationLevelDto>> SetEnabledLevelsAsync(
        UpdateLevelSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var catalog = await db.EducationLevels.OrderBy(level => level.SortOrder).ToListAsync(cancellationToken);
        var desired = request.EnabledLevelIds.Distinct().ToHashSet();
        var unknown = desired.Where(id => catalog.All(level => level.Id != id)).ToList();
        if (unknown.Count > 0)
        {
            throw new DomainValidationException("UNKNOWN_LEVEL", "One or more education levels are not in the catalog.");
        }

        await limitService.EnsureWithinLimitAsync(
            tenantId,
            LimitKey.EducationLevels,
            currentCountOverride: 0,
            increment: desired.Count,
            cancellationToken);

        var selections = await db.TenantEducationLevels.ToListAsync(cancellationToken);
        var currentlyEnabled = selections.Where(row => row.IsEnabled).Select(row => row.EducationLevelId).ToHashSet();
        var turningOff = currentlyEnabled.Except(desired).ToList();
        if (turningOff.Count > 0)
        {
            var usedByBatch = await db.AcademicBatches.AnyAsync(batch => turningOff.Contains(batch.EducationLevelId), cancellationToken);
            var usedByStudent = await db.Students.AnyAsync(student => turningOff.Contains(student.EducationLevelId), cancellationToken);
            var usedBySubject = await db.Subjects.AnyAsync(
                subject => subject.EducationLevelId.HasValue && turningOff.Contains(subject.EducationLevelId.Value),
                cancellationToken);
            if (usedByBatch || usedByStudent || usedBySubject)
            {
                throw new DomainValidationException(
                    "LEVEL_IN_USE",
                    "Turn off a level only after its batches, subjects, and students have been moved.");
            }
        }

        foreach (var level in catalog)
        {
            var row = selections.FirstOrDefault(item => item.EducationLevelId == level.Id);
            if (row is null)
            {
                db.TenantEducationLevels.Add(new TenantEducationLevel
                {
                    EducationLevelId = level.Id,
                    IsEnabled = desired.Contains(level.Id)
                });
            }
            else
            {
                row.IsEnabled = desired.Contains(level.Id);
            }
        }

        await auditService.LogAsync(
            "EducationLevels.Updated",
            nameof(TenantEducationLevel),
            tenantId.ToString(),
            new { Enabled = desired.Count },
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await GetLevelsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BatchGroupDto>> GetBatchGroupsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureBatchGroupsAsync(cancellationToken);
        return await db.BatchGroups.AsNoTracking()
            .OrderBy(group => group.Name)
            .Select(group => new BatchGroupDto(group.Id, group.Name, group.Code, group.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<BatchGroupDto> CreateBatchGroupAsync(UpsertBatchGroupRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureBatchGroupsAsync(cancellationToken);
        var code = NormalizeCode(request.Code);
        await EnsureUniqueGroupCodeAsync(code, null, cancellationToken);
        var group = new BatchGroup
        {
            Name = RequireName(request.Name),
            Code = code,
            IsActive = request.IsActive
        };
        db.BatchGroups.Add(group);
        await auditService.LogAsync("BatchGroup.Created", nameof(BatchGroup), group.Id.ToString(), new { group.Code }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new BatchGroupDto(group.Id, group.Name, group.Code, group.IsActive);
    }

    public async Task<BatchGroupDto> UpdateBatchGroupAsync(Guid id, UpsertBatchGroupRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureBatchGroupsAsync(cancellationToken);
        var group = await db.BatchGroups.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(BatchGroup), id.ToString());
        var code = NormalizeCode(request.Code);
        await EnsureUniqueGroupCodeAsync(code, id, cancellationToken);
        group.Name = RequireName(request.Name);
        group.Code = code;
        group.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return new BatchGroupDto(group.Id, group.Name, group.Code, group.IsActive);
    }

    public async Task DeleteBatchGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await EnsureBatchGroupsAsync(cancellationToken);
        var group = await db.BatchGroups.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(BatchGroup), id.ToString());
        var used = await db.AcademicBatches.AnyAsync(batch => batch.BatchGroupId == id, cancellationToken);
        if (used)
        {
            throw new DomainValidationException("GROUP_IN_USE", "Move batches out of this group before deleting it.");
        }

        db.BatchGroups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BatchDto>> GetBatchesAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var batches = await db.AcademicBatches.AsNoTracking().OrderBy(batch => batch.Name).ToListAsync(cancellationToken);
        var levels = await db.EducationLevels.AsNoTracking().ToDictionaryAsync(level => level.Id, cancellationToken);
        var groups = await db.BatchGroups.AsNoTracking().ToDictionaryAsync(group => group.Id, cancellationToken);
        var branches = await db.Branches.AsNoTracking().ToDictionaryAsync(branch => branch.Id, cancellationToken);
        var counts = await db.StudentEnrollments.AsNoTracking()
            .Where(enrollment => enrollment.IsActive)
            .GroupBy(enrollment => enrollment.BatchId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.Key, item => item.Count, cancellationToken);

        return batches.Select(batch => MapBatch(batch, levels, groups, branches, counts)).ToList();
    }

    public async Task<BatchDto> CreateBatchAsync(UpsertBatchRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.Batches, cancellationToken: cancellationToken);
        var level = await RequireEnabledLevelAsync(request.EducationLevelId, cancellationToken);
        await RequireBranchAsync(request.BranchId, cancellationToken);
        await RequireGroupAsync(request.BatchGroupId, cancellationToken);
        var code = NormalizeCode(request.Code);
        await EnsureUniqueBatchCodeAsync(code, null, cancellationToken);
        var batch = new AcademicBatch
        {
            Name = RequireName(request.Name),
            Code = code,
            EducationLevelId = level.Id,
            BranchId = request.BranchId,
            BatchGroupId = request.BatchGroupId,
            AcademicYear = TrimOptional(request.AcademicYear, 20),
            IsActive = request.IsActive
        };
        db.AcademicBatches.Add(batch);
        await auditService.LogAsync("Batch.Created", nameof(AcademicBatch), batch.Id.ToString(), new { batch.Code }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await GetBatchesAsync(cancellationToken)).Single(item => item.Id == batch.Id);
    }

    public async Task<BatchDto> UpdateBatchAsync(Guid id, UpsertBatchRequest request, CancellationToken cancellationToken = default)
    {
        var batch = await db.AcademicBatches.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Batch", id.ToString());
        var level = await RequireEnabledLevelAsync(request.EducationLevelId, cancellationToken);
        if (batch.EducationLevelId != level.Id)
        {
            var enrolled = await db.StudentEnrollments.AnyAsync(item => item.BatchId == id && item.IsActive, cancellationToken);
            if (enrolled)
            {
                throw new DomainValidationException("BATCH_IN_USE", "Move enrolled students before changing this batch's education level.");
            }
        }

        await RequireBranchAsync(request.BranchId, cancellationToken);
        var groupId = request.BatchGroupId;
        if (!groupId.HasValue)
        {
            var evaluation = await featureService.EvaluateAsync(batch.TenantId, FeatureKey.BatchGroups, cancellationToken);
            if (!evaluation.IsEnabled)
            {
                groupId = batch.BatchGroupId;
            }
        }

        await RequireGroupAsync(groupId, cancellationToken);
        var code = NormalizeCode(request.Code);
        await EnsureUniqueBatchCodeAsync(code, id, cancellationToken);
        batch.Name = RequireName(request.Name);
        batch.Code = code;
        batch.EducationLevelId = level.Id;
        batch.BranchId = request.BranchId;
        batch.BatchGroupId = groupId;
        batch.AcademicYear = TrimOptional(request.AcademicYear, 20);
        batch.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return (await GetBatchesAsync(cancellationToken)).Single(item => item.Id == batch.Id);
    }

    public async Task DeleteBatchAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = await db.AcademicBatches.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException("Batch", id.ToString());
        var enrolled = await db.StudentEnrollments.AnyAsync(item => item.BatchId == id && item.IsActive, cancellationToken);
        var scheduled = await db.ClassSchedules.AnyAsync(item => item.BatchId == id, cancellationToken);
        if (enrolled || scheduled)
        {
            throw new DomainValidationException("BATCH_IN_USE", "Remove class schedules and enrollments before deleting this batch.");
        }

        db.AcademicBatches.Remove(batch);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SubjectDto>> GetSubjectsAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var subjects = await db.Subjects.AsNoTracking().OrderBy(subject => subject.Name).ToListAsync(cancellationToken);
        var levels = await db.EducationLevels.AsNoTracking().ToDictionaryAsync(level => level.Id, cancellationToken);
        return subjects.Select(subject => MapSubject(subject, levels)).ToList();
    }

    public async Task<SubjectDto> CreateSubjectAsync(UpsertSubjectRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.Subjects, cancellationToken: cancellationToken);
        if (request.EducationLevelId.HasValue)
        {
            await RequireEnabledLevelAsync(request.EducationLevelId.Value, cancellationToken);
        }

        var code = NormalizeCode(request.Code);
        await EnsureUniqueSubjectCodeAsync(code, null, cancellationToken);
        var subject = new Subject
        {
            Name = RequireName(request.Name),
            Code = code,
            EducationLevelId = request.EducationLevelId,
            IsActive = request.IsActive
        };
        db.Subjects.Add(subject);
        await auditService.LogAsync("Subject.Created", nameof(Subject), subject.Id.ToString(), new { subject.Code }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var levels = await db.EducationLevels.AsNoTracking().ToDictionaryAsync(level => level.Id, cancellationToken);
        return MapSubject(subject, levels);
    }

    public async Task<SubjectDto> UpdateSubjectAsync(Guid id, UpsertSubjectRequest request, CancellationToken cancellationToken = default)
    {
        var subject = await db.Subjects.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Subject), id.ToString());
        if (request.EducationLevelId.HasValue)
        {
            await RequireEnabledLevelAsync(request.EducationLevelId.Value, cancellationToken);
        }

        var code = NormalizeCode(request.Code);
        await EnsureUniqueSubjectCodeAsync(code, id, cancellationToken);
        subject.Name = RequireName(request.Name);
        subject.Code = code;
        subject.EducationLevelId = request.EducationLevelId;
        subject.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        var levels = await db.EducationLevels.AsNoTracking().ToDictionaryAsync(level => level.Id, cancellationToken);
        return MapSubject(subject, levels);
    }

    public async Task DeleteSubjectAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var subject = await db.Subjects.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Subject), id.ToString());
        var used = await db.ClassSchedules.AnyAsync(item => item.SubjectId == id, cancellationToken);
        if (used)
        {
            throw new DomainValidationException("SUBJECT_IN_USE", "Remove class schedules that use this subject before deleting it.");
        }

        db.Subjects.Remove(subject);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ClassScheduleDto>> GetSchedulesAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var schedules = await db.ClassSchedules.AsNoTracking().OrderBy(item => item.Weekday).ThenBy(item => item.StartMinutes).ToListAsync(cancellationToken);
        return await MapSchedulesAsync(schedules, cancellationToken);
    }

    public async Task<ClassScheduleDto> CreateScheduleAsync(UpsertClassScheduleRequest request, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var (batch, subject, teacher) = await ValidateScheduleAsync(request, null, cancellationToken);
        var schedule = new ClassSchedule
        {
            Name = ScheduleName(request.Name, subject.Name, batch.Name),
            BatchId = batch.Id,
            SubjectId = subject.Id,
            TeacherUserId = teacher.Id,
            Weekday = request.Weekday,
            StartMinutes = request.StartMinutes,
            EndMinutes = request.EndMinutes,
            Room = TrimOptional(request.Room, 50),
            IsActive = request.IsActive
        };
        db.ClassSchedules.Add(schedule);
        await auditService.LogAsync("ClassSchedule.Created", nameof(ClassSchedule), schedule.Id.ToString(), new { schedule.Name }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await MapSchedulesAsync([schedule], cancellationToken)).Single();
    }

    public async Task<ClassScheduleDto> UpdateScheduleAsync(Guid id, UpsertClassScheduleRequest request, CancellationToken cancellationToken = default)
    {
        var schedule = await db.ClassSchedules.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ClassSchedule), id.ToString());
        var (batch, subject, teacher) = await ValidateScheduleAsync(request, id, cancellationToken);
        schedule.Name = ScheduleName(request.Name, subject.Name, batch.Name);
        schedule.BatchId = batch.Id;
        schedule.SubjectId = subject.Id;
        schedule.TeacherUserId = teacher.Id;
        schedule.Weekday = request.Weekday;
        schedule.StartMinutes = request.StartMinutes;
        schedule.EndMinutes = request.EndMinutes;
        schedule.Room = TrimOptional(request.Room, 50);
        schedule.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        return (await MapSchedulesAsync([schedule], cancellationToken)).Single();
    }

    public async Task DeleteScheduleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schedule = await db.ClassSchedules.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ClassSchedule), id.ToString());
        db.ClassSchedules.Remove(schedule);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeacherOptionDto>> GetTeachersAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        return await db.Users.AsNoTracking()
            .Where(user => user.IsActive && (user.RoleType == RoleType.Teacher || user.RoleType == RoleType.InstituteAdmin))
            .OrderBy(user => user.FullName)
            .Select(user => new TeacherOptionDto(user.Id, user.FullName, user.RoleType.ToString()))
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureBatchGroupsAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var evaluation = await featureService.EvaluateAsync(tenantId, FeatureKey.BatchGroups, cancellationToken);
        if (!evaluation.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.BatchGroups);
        }

        if (!evaluation.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.BatchGroups);
        }
    }

    private async Task RequireGroupAsync(Guid? groupId, CancellationToken cancellationToken)
    {
        if (!groupId.HasValue)
        {
            return;
        }

        await EnsureBatchGroupsAsync(cancellationToken);
        var exists = await db.BatchGroups.AnyAsync(group => group.Id == groupId.Value, cancellationToken);
        if (!exists)
        {
            throw new EntityNotFoundException(nameof(BatchGroup), groupId.Value.ToString());
        }
    }

    private async Task<EducationLevel> RequireEnabledLevelAsync(Guid levelId, CancellationToken cancellationToken)
    {
        var level = await db.EducationLevels.FirstOrDefaultAsync(item => item.Id == levelId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(EducationLevel), levelId.ToString());
        var enabled = await db.TenantEducationLevels.AnyAsync(row => row.EducationLevelId == levelId && row.IsEnabled, cancellationToken);
        if (!enabled)
        {
            throw new DomainValidationException("LEVEL_NOT_ENABLED", $"{level.Name} is not enabled for this institute.");
        }

        return level;
    }

    private async Task RequireBranchAsync(Guid? branchId, CancellationToken cancellationToken)
    {
        if (!branchId.HasValue)
        {
            return;
        }

        var exists = await db.Branches.AnyAsync(branch => branch.Id == branchId.Value, cancellationToken);
        if (!exists)
        {
            throw new EntityNotFoundException(nameof(Branch), branchId.Value.ToString());
        }
    }

    private async Task<(AcademicBatch Batch, Subject Subject, User Teacher)> ValidateScheduleAsync(
        UpsertClassScheduleRequest request,
        Guid? currentId,
        CancellationToken cancellationToken)
    {
        if (request.Weekday is < 1 or > 7)
        {
            throw new DomainValidationException("INVALID_WEEKDAY", "Weekday must be 1 (Monday) through 7 (Sunday).");
        }

        if (request.StartMinutes is < 0 or > 1439 || request.EndMinutes is < 1 or > 1440 || request.EndMinutes <= request.StartMinutes)
        {
            throw new DomainValidationException("INVALID_TIME", "Class end time must be after the start time.");
        }

        var batch = await db.AcademicBatches.FirstOrDefaultAsync(item => item.Id == request.BatchId, cancellationToken)
            ?? throw new EntityNotFoundException("Batch", request.BatchId.ToString());
        var subject = await db.Subjects.FirstOrDefaultAsync(item => item.Id == request.SubjectId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Subject), request.SubjectId.ToString());
        if (subject.EducationLevelId.HasValue && subject.EducationLevelId != batch.EducationLevelId)
        {
            throw new DomainValidationException("SUBJECT_LEVEL_MISMATCH", "This subject belongs to a different education level than the batch.");
        }

        var teacher = await db.Users.FirstOrDefaultAsync(user => user.Id == request.TeacherUserId && user.IsActive, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(User), request.TeacherUserId.ToString());
        if (teacher.RoleType is not (RoleType.Teacher or RoleType.InstituteAdmin))
        {
            throw new DomainValidationException("TEACHER_NOT_ASSIGNABLE", "Assign an active teacher or institute admin.");
        }

        if (request.IsActive)
        {
            var sameDaySchedules = await db.ClassSchedules.AsNoTracking()
                .Where(item => item.Id != currentId && item.IsActive && item.Weekday == request.Weekday)
                .ToListAsync(cancellationToken);

            var existingSlots = sameDaySchedules
                .Select(item => new ExistingTimetableSlot(
                    item.Id,
                    item.Name,
                    item.BatchId,
                    item.BatchId == batch.Id ? batch.Name : "Batch",
                    item.SubjectId,
                    subject.Name,
                    item.TeacherUserId,
                    item.TeacherUserId == teacher.Id ? teacher.FullName : "Teacher",
                    item.Weekday,
                    item.StartMinutes,
                    item.EndMinutes,
                    item.Room,
                    item.IsActive))
                .ToList();

            var clashes = TimetableRules.DetectClashes(
                new TimetableCandidateSlot(
                    currentId,
                    batch.Id,
                    batch.Name,
                    teacher.Id,
                    teacher.FullName,
                    request.Weekday,
                    request.StartMinutes,
                    request.EndMinutes,
                    request.Room,
                    request.IsActive),
                existingSlots);

            if (clashes.Count > 0)
            {
                var firstClash = clashes[0];
                var code = firstClash.ClashType switch
                {
                    TimetableClashType.Teacher => "TIMETABLE_CLASH_TEACHER",
                    TimetableClashType.Batch => "TIMETABLE_CLASH_BATCH",
                    TimetableClashType.Room => "TIMETABLE_CLASH_ROOM",
                    _ => "TIMETABLE_CLASH"
                };
                throw new DomainValidationException(code, firstClash.Message, 409);
            }
        }

        return (batch, subject, teacher);
    }

    private async Task EnsureUniqueBatchCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var exists = await db.AcademicBatches.AnyAsync(batch => batch.Code == code && batch.Id != exceptId, cancellationToken);
        if (exists)
        {
            throw new DomainValidationException("CODE_EXISTS", $"Batch code '{code}' is already used.", 409);
        }
    }

    private async Task EnsureUniqueSubjectCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var exists = await db.Subjects.AnyAsync(subject => subject.Code == code && subject.Id != exceptId, cancellationToken);
        if (exists)
        {
            throw new DomainValidationException("CODE_EXISTS", $"Subject code '{code}' is already used.", 409);
        }
    }

    private async Task EnsureUniqueGroupCodeAsync(string code, Guid? exceptId, CancellationToken cancellationToken)
    {
        var exists = await db.BatchGroups.AnyAsync(group => group.Code == code && group.Id != exceptId, cancellationToken);
        if (exists)
        {
            throw new DomainValidationException("CODE_EXISTS", $"Group code '{code}' is already used.", 409);
        }
    }

    private async Task<IReadOnlyList<ClassScheduleDto>> MapSchedulesAsync(
        IReadOnlyList<ClassSchedule> schedules,
        CancellationToken cancellationToken)
    {
        var batchIds = schedules.Select(item => item.BatchId).Distinct().ToList();
        var subjectIds = schedules.Select(item => item.SubjectId).Distinct().ToList();
        var teacherIds = schedules.Select(item => item.TeacherUserId).Distinct().ToList();
        var batches = await db.AcademicBatches.AsNoTracking().Where(batch => batchIds.Contains(batch.Id)).ToDictionaryAsync(batch => batch.Id, cancellationToken);
        var subjects = await db.Subjects.AsNoTracking().Where(subject => subjectIds.Contains(subject.Id)).ToDictionaryAsync(subject => subject.Id, cancellationToken);
        var teachers = await db.Users.AsNoTracking().Where(user => teacherIds.Contains(user.Id)).ToDictionaryAsync(user => user.Id, cancellationToken);
        return schedules.Select(item => new ClassScheduleDto(
            item.Id,
            item.Name,
            item.BatchId,
            batches.TryGetValue(item.BatchId, out var batch) ? batch.Name : string.Empty,
            item.SubjectId,
            subjects.TryGetValue(item.SubjectId, out var subject) ? subject.Name : string.Empty,
            item.TeacherUserId,
            teachers.TryGetValue(item.TeacherUserId, out var teacher) ? teacher.FullName : string.Empty,
            item.Weekday,
            item.StartMinutes,
            item.EndMinutes,
            item.Room,
            item.IsActive)).ToList();
    }

    private static BatchDto MapBatch(
        AcademicBatch batch,
        IReadOnlyDictionary<Guid, EducationLevel> levels,
        IReadOnlyDictionary<Guid, BatchGroup> groups,
        IReadOnlyDictionary<Guid, Branch> branches,
        IReadOnlyDictionary<Guid, int> counts)
    {
        levels.TryGetValue(batch.EducationLevelId, out var level);
        BatchGroup? group = null;
        if (batch.BatchGroupId.HasValue)
        {
            groups.TryGetValue(batch.BatchGroupId.Value, out group);
        }

        Branch? branch = null;
        if (batch.BranchId.HasValue)
        {
            branches.TryGetValue(batch.BranchId.Value, out branch);
        }

        counts.TryGetValue(batch.Id, out var count);
        return new BatchDto(
            batch.Id,
            batch.Name,
            batch.Code,
            batch.EducationLevelId,
            level?.Code ?? string.Empty,
            level?.Name ?? string.Empty,
            batch.BranchId,
            branch?.Name,
            batch.BatchGroupId,
            group?.Name,
            batch.AcademicYear,
            batch.IsActive,
            count);
    }

    private static SubjectDto MapSubject(Subject subject, IReadOnlyDictionary<Guid, EducationLevel> levels)
    {
        EducationLevel? level = null;
        if (subject.EducationLevelId.HasValue)
        {
            levels.TryGetValue(subject.EducationLevelId.Value, out level);
        }

        return new SubjectDto(subject.Id, subject.Name, subject.Code, subject.EducationLevelId, level?.Code, level?.Name, subject.IsActive);
    }

    private static string RequireName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 120)
        {
            throw new DomainValidationException("VALIDATION", "Name must be between 1 and 120 characters.");
        }

        return trimmed;
    }

    private static string NormalizeCode(string code)
    {
        var trimmed = code.Trim().ToUpperInvariant();
        if (trimmed.Length is < 1 or > 32 || !trimmed.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            throw new DomainValidationException("VALIDATION", "Code must be 1–32 letters, numbers, or hyphens.");
        }

        return trimmed;
    }

    private static string? TrimOptional(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > max)
        {
            throw new DomainValidationException("VALIDATION", $"Value must be {max} characters or fewer.");
        }

        return trimmed;
    }

    private static string ScheduleName(string? name, string subject, string batch) =>
        string.IsNullOrWhiteSpace(name) ? $"{subject} · {batch}" : RequireName(name);
}
