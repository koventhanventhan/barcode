using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Notifications;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Attendance;

public sealed class AttendanceService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService features,
    INotificationService notifications,
    IAuditService audit,
    IAttendanceBoard board) : IAttendanceService
{
    public async Task<AttendancePolicyDto> GetPolicyAsync(CancellationToken cancellationToken = default)
    {
        var policy = await EnsurePolicyAsync(cancellationToken);
        return ToPolicy(policy);
    }

    public async Task<AttendancePolicyDto> UpdatePolicyAsync(UpdateAttendancePolicyRequest request, CancellationToken cancellationToken = default)
    {
        if (request.LateAfterMinutes is < 0 or > 180)
        {
            throw new DomainValidationException("INVALID_LATE_THRESHOLD", "Late threshold must be between 0 and 180 minutes.");
        }

        var policy = await EnsurePolicyAsync(cancellationToken);
        policy.LateAfterMinutes = request.LateAfterMinutes;
        policy.MarkAbsentOnStop = request.MarkAbsentOnStop;
        await audit.LogAsync("Attendance.PolicyUpdated", nameof(AttendancePolicy), policy.Id.ToString(), request, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ToPolicy(policy);
    }

    public async Task<IReadOnlyList<ClassSessionDto>> ListSessionsAsync(DateOnly? date, CancellationToken cancellationToken = default)
    {
        var day = date ?? ColomboClock.Today();
        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(item => item.SessionDate == day)
            .OrderBy(item => item.StartedAt)
            .ToListAsync(cancellationToken);
        return await MapSessionsAsync(sessions, cancellationToken);
    }

    public async Task<ClassSessionDto> OpenSessionAsync(OpenClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var schedule = await RequireScheduleAsync(request.ClassScheduleId, cancellationToken);
        var day = request.SessionDate ?? ColomboClock.Today();
        var existing = await db.ClassSessions.FirstOrDefaultAsync(
            item => item.ClassScheduleId == schedule.Id && item.SessionDate == day,
            cancellationToken);
        if (existing is not null)
        {
            return (await MapSessionsAsync([existing], cancellationToken)).Single();
        }

        var session = new ClassSession
        {
            TenantId = tenantId,
            ClassScheduleId = schedule.Id,
            SessionDate = day,
            Status = ClassSessionStatus.Scheduled,
            BranchId = schedule.BranchId
        };
        db.ClassSessions.Add(session);
        await audit.LogAsync("Attendance.SessionOpened", nameof(ClassSession), null, new { schedule.Id, day }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await MapSessionsAsync([session], cancellationToken)).Single();
    }

    public async Task<ClassSessionDto> StartSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (session.Status != ClassSessionStatus.Scheduled)
        {
            throw new DomainValidationException("INVALID_SESSION_STATE", "Only a scheduled class can be started.");
        }

        session.Status = ClassSessionStatus.Started;
        session.StartedAt = DateTimeOffset.UtcNow;
        session.StartedByUserId = tenantContext.UserId;
        if (await features.IsEnabledAsync(session.TenantId, FeatureKey.OnlineAttendance, cancellationToken))
        {
            session.JoinCode = await UniqueJoinCodeAsync(session.TenantId, cancellationToken);
        }

        if (!session.StartedNotified)
        {
            var students = await EnrolledStudentsAsync(session, cancellationToken);
            await NotifyAsync(session, students, NotificationMessageType.ClassStarted, "class-started", cancellationToken);
            session.StartedNotified = true;
        }

        await audit.LogAsync("Attendance.SessionStarted", nameof(ClassSession), session.Id.ToString(), cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(session, cancellationToken);
        return (await MapSessionsAsync([session], cancellationToken)).Single();
    }

    public async Task<ClassSessionDto> StopSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (session.Status != ClassSessionStatus.Started)
        {
            throw new DomainValidationException("INVALID_SESSION_STATE", "Only a started class can be stopped.");
        }

        var policy = await EnsurePolicyAsync(cancellationToken);
        var students = await EnrolledStudentsAsync(session, cancellationToken);
        var marks = await db.AttendanceMarks.Where(item => item.ClassSessionId == session.Id).ToListAsync(cancellationToken);
        if (policy.MarkAbsentOnStop)
        {
            foreach (var student in students)
            {
                var mark = marks.FirstOrDefault(item => item.StudentId == student.Id);
                if (mark is { IsUndone: false, Status: AttendanceMarkStatus.Present or AttendanceMarkStatus.Late or AttendanceMarkStatus.Absent })
                {
                    continue;
                }

                mark = UpsertMark(session, mark, student.Id, AttendanceMarkStatus.Absent, AttendanceMarkSource.AutoAbsent, "Not scanned before the class stopped.");
                if (!mark.AbsentNotified)
                {
                    await NotifyAsync(session, [student], NotificationMessageType.AttendanceAbsent, "attendance-absent", cancellationToken);
                    mark.AbsentNotified = true;
                }
            }
        }

        session.Status = ClassSessionStatus.Stopped;
        session.StoppedAt = DateTimeOffset.UtcNow;
        if (!session.StoppedNotified)
        {
            await NotifyAsync(session, students, NotificationMessageType.ClassStopped, "class-stopped", cancellationToken);
            session.StoppedNotified = true;
        }
        await audit.LogAsync("Attendance.SessionStopped", nameof(ClassSession), session.Id.ToString(), cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(session, cancellationToken);
        return (await MapSessionsAsync([session], cancellationToken)).Single();
    }

    public async Task<ClassSessionDto> CancelSessionAsync(Guid sessionId, CancelClassSessionRequest request, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        if (session.Status is ClassSessionStatus.Stopped or ClassSessionStatus.Cancelled)
        {
            throw new DomainValidationException("INVALID_SESSION_STATE", "A stopped or cancelled class cannot be cancelled again.");
        }

        var reason = request.Reason?.Trim();
        if (reason is { Length: > 300 })
        {
            throw new DomainValidationException("INVALID_REASON", "The reason must be 300 characters or fewer.");
        }

        session.Status = ClassSessionStatus.Cancelled;
        session.CancelledAt = DateTimeOffset.UtcNow;
        if (!session.CancelledNotified)
        {
            var students = await EnrolledStudentsAsync(session, cancellationToken);
            await NotifyAsync(session, students, NotificationMessageType.ClassCancelled, "class-cancelled", cancellationToken);
            session.CancelledNotified = true;
        }
        session.CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason;
        await audit.LogAsync("Attendance.SessionCancelled", nameof(ClassSession), session.Id.ToString(), new { reason }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(session, cancellationToken);
        return (await MapSessionsAsync([session], cancellationToken)).Single();
    }

    public Task<ScanAttendanceResultDto> ScanAsync(ScanAttendanceRequest request, CancellationToken cancellationToken = default) =>
        MarkByCodeAsync(request.Code, request.SessionId, AttendanceMarkSource.Scan, requireOnlineFeature: false, joinCode: null, cancellationToken);

    public async Task<ScanAttendanceResultDto> JoinOnlineAsync(JoinAttendanceRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var online = await features.EvaluateAsync(tenantId, FeatureKey.OnlineAttendance, cancellationToken);
        if (!online.IsEnabled)
        {
            throw online.IsInPlan
                ? new FeatureDisabledException(FeatureKey.OnlineAttendance)
                : new FeatureNotInPlanException(FeatureKey.OnlineAttendance);
        }

        var code = request.JoinCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainValidationException("JOIN_CODE_REQUIRED", "Enter the class join code.");
        }

        var session = await db.ClassSessions.FirstOrDefaultAsync(
            item => item.JoinCode == code && item.Status == ClassSessionStatus.Started,
            cancellationToken);
        if (session is null)
        {
            throw new DomainValidationException("JOIN_CODE_INVALID", "That join code is not an open class.");
        }

        return await MarkByCodeAsync(request.StudentCode, session.Id, AttendanceMarkSource.Online, requireOnlineFeature: true, joinCode: code, cancellationToken);
    }

    public async Task<AttendanceRegisterStudentDto> MarkManualAsync(Guid sessionId, ManualAttendanceRequest request, CancellationToken cancellationToken = default)
    {
        var session = await RequireOpenForMarkingAsync(sessionId, cancellationToken);
        if (!Enum.TryParse<AttendanceMarkStatus>(request.Status, ignoreCase: true, out var status))
        {
            throw new DomainValidationException("INVALID_STATUS", "Status must be Present, Late, or Absent.");
        }

        var reason = NormalizeReason(request.Reason, required: status == AttendanceMarkStatus.Absent);
        var student = await db.Students.FirstOrDefaultAsync(item => item.Id == request.StudentId && item.Status == StudentStatus.Active, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Student), request.StudentId.ToString());
        await EnsureEnrolledAsync(session, student.Id, cancellationToken);
        var mark = await db.AttendanceMarks.FirstOrDefaultAsync(item => item.ClassSessionId == session.Id && item.StudentId == student.Id, cancellationToken);
        mark = UpsertMark(session, mark, student.Id, status, AttendanceMarkSource.Manual, reason);
        await NotifyMarkAsync(session, student, mark, cancellationToken);
        await audit.LogAsync("Attendance.MarkedManual", nameof(AttendanceMark), mark.Id.ToString(), new { student.Id, status }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(session, cancellationToken);
        return ToRegisterStudent(student, mark);
    }

    public async Task<AttendanceRegisterStudentDto> UndoAsync(Guid sessionId, Guid studentId, UndoAttendanceRequest request, CancellationToken cancellationToken = default)
    {
        var session = await RequireOpenForMarkingAsync(sessionId, cancellationToken);
        var reason = NormalizeReason(request.Reason, required: true);
        var mark = await db.AttendanceMarks.FirstOrDefaultAsync(item => item.ClassSessionId == session.Id && item.StudentId == studentId && !item.IsUndone, cancellationToken)
            ?? throw new DomainValidationException("MARK_NOT_FOUND", "This student has no attendance mark to undo.");
        mark.IsUndone = true;
        mark.UndoneAt = DateTimeOffset.UtcNow;
        mark.UndoReason = reason;
        var student = await db.Students.FirstAsync(item => item.Id == studentId, cancellationToken);
        await audit.LogAsync("Attendance.Undone", nameof(AttendanceMark), mark.Id.ToString(), new { studentId, reason }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(session, cancellationToken);
        return ToRegisterStudent(student, mark);
    }

    public async Task<IReadOnlyList<AttendanceRegisterSessionDto>> GetRegisterAsync(AttendanceRegisterQuery query, CancellationToken cancellationToken = default)
    {
        var day = query.Date ?? ColomboClock.Today();
        var sessions = await db.ClassSessions.AsNoTracking().Where(item => item.SessionDate == day).ToListAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            return [];
        }

        var scheduleIds = sessions.Select(item => item.ClassScheduleId).Distinct().ToList();
        var schedules = await db.ClassSchedules.AsNoTracking().Where(item => scheduleIds.Contains(item.Id)).ToListAsync(cancellationToken);
        if (query.BatchId.HasValue)
        {
            var allowed = schedules.Where(item => item.BatchId == query.BatchId.Value).Select(item => item.Id).ToHashSet();
            sessions = sessions.Where(item => allowed.Contains(item.ClassScheduleId)).ToList();
            if (sessions.Count == 0)
            {
                return [];
            }
        }

        var mapped = await MapSessionsAsync(sessions, cancellationToken);
        var mappedById = mapped.ToDictionary(item => item.Id);
        var schedulesById = schedules.ToDictionary(item => item.Id);
        var batchIds = schedules.Select(item => item.BatchId).Distinct().ToList();
        var enrollments = await db.StudentEnrollments.AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId) && item.IsActive)
            .Select(item => new { item.BatchId, item.StudentId })
            .ToListAsync(cancellationToken);
        var studentIds = enrollments.Select(item => item.StudentId).Distinct().ToList();
        var studentsById = await db.Students.AsNoTracking()
            .Where(item => studentIds.Contains(item.Id) && item.Status == StudentStatus.Active)
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var sessionIds = sessions.Select(item => item.Id).ToList();
        var allMarks = await db.AttendanceMarks.AsNoTracking()
            .Where(item => sessionIds.Contains(item.ClassSessionId))
            .ToListAsync(cancellationToken);
        var marksBySession = allMarks.ToLookup(item => item.ClassSessionId);
        var studentIdsByBatch = enrollments.ToLookup(item => item.BatchId, item => item.StudentId);

        var result = new List<AttendanceRegisterSessionDto>(sessions.Count);
        foreach (var session in sessions)
        {
            var schedule = schedulesById[session.ClassScheduleId];
            var students = studentIdsByBatch[schedule.BatchId]
                .Where(studentsById.ContainsKey)
                .Select(id => studentsById[id])
                .ToList();
            var marks = marksBySession[session.Id].ToList();
            var rows = students.Select(student => ToRegisterStudent(student, marks.FirstOrDefault(item => item.StudentId == student.Id))).ToList();
            result.Add(new AttendanceRegisterSessionDto(mappedById[session.Id], rows));
        }

        return result.OrderBy(item => item.Session.ClassName).ToList();
    }

    public async Task<AttendanceReportDto> GetReportAsync(AttendanceReportQuery query, CancellationToken cancellationToken = default)
    {
        var to = query.To ?? ColomboClock.Today();
        var from = query.From ?? to.AddDays(-30);
        if (from > to)
        {
            throw new DomainValidationException("INVALID_RANGE", "The start date must be on or before the end date.");
        }

        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(item => item.SessionDate >= from && item.SessionDate <= to && item.Status == ClassSessionStatus.Stopped)
            .ToListAsync(cancellationToken);
        var schedules = await db.ClassSchedules.AsNoTracking()
            .Where(item => sessions.Select(session => session.ClassScheduleId).Contains(item.Id))
            .ToListAsync(cancellationToken);
        if (query.BatchId.HasValue)
        {
            var allowed = schedules.Where(item => item.BatchId == query.BatchId.Value).Select(item => item.Id).ToHashSet();
            sessions = sessions.Where(item => allowed.Contains(item.ClassScheduleId)).ToList();
        }

        var sessionIds = sessions.Select(item => item.Id).ToList();
        var marks = await db.AttendanceMarks.AsNoTracking()
            .Where(item => sessionIds.Contains(item.ClassSessionId) && !item.IsUndone)
            .ToListAsync(cancellationToken);
        var batches = await db.AcademicBatches.AsNoTracking().ToDictionaryAsync(item => item.Id, cancellationToken);
        var byDay = sessions
            .GroupBy(item => item.SessionDate)
            .OrderBy(group => group.Key)
            .Select(group => CountPoint(group.Key, marks.Where(mark => group.Any(session => session.Id == mark.ClassSessionId))))
            .ToList();
        var byBatch = sessions
            .GroupBy(item => schedules.First(schedule => schedule.Id == item.ClassScheduleId).BatchId)
            .Select(group =>
            {
                var counted = marks.Where(mark => group.Any(session => session.Id == mark.ClassSessionId)).ToList();
                var present = counted.Count(mark => mark.Status is AttendanceMarkStatus.Present or AttendanceMarkStatus.Late);
                var late = counted.Count(mark => mark.Status == AttendanceMarkStatus.Late);
                var absent = counted.Count(mark => mark.Status == AttendanceMarkStatus.Absent);
                var total = present + absent;
                return new AttendanceBatchPointDto(
                    group.Key,
                    batches.TryGetValue(group.Key, out var batch) ? batch.Name : "Batch",
                    present - late,
                    late,
                    absent,
                    total == 0 ? 0 : Math.Round(present * 100m / total, 1));
            })
            .OrderBy(item => item.BatchName)
            .ToList();
        var presentTotal = byDay.Sum(item => item.Present + item.Late);
        var absentTotal = byDay.Sum(item => item.Absent);
        var grand = presentTotal + absentTotal;
        return new AttendanceReportDto(
            byDay.Sum(item => item.Present),
            byDay.Sum(item => item.Late),
            absentTotal,
            grand == 0 ? 0 : Math.Round(presentTotal * 100m / grand, 1),
            byDay,
            byBatch);
    }

    public async Task<IReadOnlyList<StudentAttendanceItemDto>> GetStudentHistoryAsync(Guid studentId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(item => item.Id == studentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Student), studentId.ToString());
        var end = to ?? ColomboClock.Today();
        var start = from ?? end.AddDays(-90);
        var marks = await db.AttendanceMarks.AsNoTracking()
            .Where(item => item.StudentId == student.Id && !item.IsUndone)
            .ToListAsync(cancellationToken);
        var sessionIds = marks.Select(item => item.ClassSessionId).ToList();
        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(item => sessionIds.Contains(item.Id) && item.SessionDate >= start && item.SessionDate <= end)
            .ToListAsync(cancellationToken);
        var schedules = await db.ClassSchedules.AsNoTracking()
            .Where(item => sessions.Select(session => session.ClassScheduleId).Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        return sessions
            .Select(session =>
            {
                var mark = marks.First(item => item.ClassSessionId == session.Id);
                var name = schedules.TryGetValue(session.ClassScheduleId, out var schedule) ? schedule.Name : "Class";
                return new StudentAttendanceItemDto(session.Id, session.SessionDate, name, mark.Status.ToString(), mark.MarkedAt);
            })
            .OrderByDescending(item => item.SessionDate)
            .ToList();
    }

    public async Task<AttendanceBoardDto> GetBoardAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await RequireSessionAsync(sessionId, cancellationToken);
        return await BuildBoardAsync(session, cancellationToken);
    }

    private async Task<ScanAttendanceResultDto> MarkByCodeAsync(
        string? rawCode,
        Guid? sessionId,
        AttendanceMarkSource source,
        bool requireOnlineFeature,
        string? joinCode,
        CancellationToken cancellationToken)
    {
        var code = rawCode?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainValidationException("CODE_REQUIRED", "Scan or enter a student code.");
        }

        var student = await db.Students.FirstOrDefaultAsync(item => item.StudentCode == code, cancellationToken);
        if (student is null)
        {
            throw new EntityNotFoundException("Student code", code);
        }

        if (student.Status != StudentStatus.Active)
        {
            throw new DomainValidationException("STUDENT_INACTIVE", $"{student.FullName} is not an active student.");
        }

        var session = await ResolveSessionAsync(student.Id, sessionId, cancellationToken);
        if (requireOnlineFeature && !string.Equals(session.JoinCode, joinCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException("JOIN_CODE_INVALID", "That join code is not an open class.");
        }

        var existing = await db.AttendanceMarks.FirstOrDefaultAsync(
            item => item.ClassSessionId == session.Id && item.StudentId == student.Id && !item.IsUndone,
            cancellationToken);
        if (existing is { Status: AttendanceMarkStatus.Present or AttendanceMarkStatus.Late })
        {
            var label = AttendanceRules.FormatColombo(existing.MarkedAt);
            return new ScanAttendanceResultDto(
                AttendanceRules.OutcomeAlreadyMarked,
                $"Already marked at {label}.",
                session.Id,
                student.Id,
                student.FullName,
                student.StudentCode,
                existing.Status.ToString(),
                existing.MarkedAt,
                label);
        }

        var policy = await EnsurePolicyAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var status = session.StartedAt.HasValue && AttendanceRules.IsLate(session.StartedAt.Value, now, policy.LateAfterMinutes)
            ? AttendanceMarkStatus.Late
            : AttendanceMarkStatus.Present;
        var stored = await db.AttendanceMarks.FirstOrDefaultAsync(item => item.ClassSessionId == session.Id && item.StudentId == student.Id, cancellationToken);
        stored = UpsertMark(session, stored, student.Id, status, source, reason: null);
        await NotifyMarkAsync(session, student, stored, cancellationToken);
        await audit.LogAsync("Attendance.Scanned", nameof(AttendanceMark), stored.Id.ToString(), new { student.StudentCode, status, source }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await PublishAsync(session, cancellationToken);
        var markedLabel = AttendanceRules.FormatColombo(stored.MarkedAt);
        var verb = status == AttendanceMarkStatus.Late ? "late" : "present";
        return new ScanAttendanceResultDto(
            AttendanceRules.OutcomeMarked,
            $"{student.FullName} marked {verb} at {markedLabel}.",
            session.Id,
            student.Id,
            student.FullName,
            student.StudentCode,
            status.ToString(),
            stored.MarkedAt,
            markedLabel);
    }

    private async Task<ClassSession> ResolveSessionAsync(Guid studentId, Guid? sessionId, CancellationToken cancellationToken)
    {
        if (sessionId.HasValue)
        {
            var chosen = await RequireSessionAsync(sessionId.Value, cancellationToken);
            if (chosen.Status != ClassSessionStatus.Started)
            {
                throw new DomainValidationException("SESSION_NOT_STARTED", "Start the class before marking attendance.");
            }

            await EnsureEnrolledAsync(chosen, studentId, cancellationToken);
            return chosen;
        }

        var today = ColomboClock.Today();
        var open = await db.ClassSessions.Where(item => item.Status == ClassSessionStatus.Started && item.SessionDate == today).ToListAsync(cancellationToken);
        var scheduleIds = open.Select(item => item.ClassScheduleId).ToList();
        var schedules = await db.ClassSchedules.AsNoTracking().Where(item => scheduleIds.Contains(item.Id)).ToListAsync(cancellationToken);
        var enrolledBatchIds = await db.StudentEnrollments.AsNoTracking()
            .Where(item => item.StudentId == studentId && item.IsActive)
            .Select(item => item.BatchId)
            .ToListAsync(cancellationToken);
        var matches = open.Where(item =>
        {
            var schedule = schedules.FirstOrDefault(candidate => candidate.Id == item.ClassScheduleId);
            return schedule is not null && enrolledBatchIds.Contains(schedule.BatchId);
        }).ToList();
        return matches.Count switch
        {
            0 => throw new DomainValidationException("NO_OPEN_SESSION", "No started class matches this student. Start the class, or the student is not enrolled."),
            1 => matches[0],
            _ => throw new DomainValidationException("SESSION_AMBIGUOUS", "More than one class is open for this student. Choose the class on the scanner.")
        };
    }

    private async Task EnsureEnrolledAsync(ClassSession session, Guid studentId, CancellationToken cancellationToken)
    {
        var schedule = await db.ClassSchedules.AsNoTracking().FirstAsync(item => item.Id == session.ClassScheduleId, cancellationToken);
        var enrolled = await db.StudentEnrollments.AnyAsync(
            item => item.StudentId == studentId && item.BatchId == schedule.BatchId && item.IsActive,
            cancellationToken);
        if (!enrolled)
        {
            throw new DomainValidationException("STUDENT_NOT_ENROLLED", "This student is not enrolled in the class batch.");
        }
    }

    private AttendanceMark UpsertMark(
        ClassSession session,
        AttendanceMark? mark,
        Guid studentId,
        AttendanceMarkStatus status,
        AttendanceMarkSource source,
        string? reason)
    {
        if (mark is null)
        {
            mark = new AttendanceMark
            {
                TenantId = session.TenantId == Guid.Empty ? TenantGuard.Require(tenantContext) : session.TenantId,
                ClassSessionId = session.Id,
                StudentId = studentId,
                BranchId = session.BranchId
            };
            db.AttendanceMarks.Add(mark);
        }

        mark.BranchId ??= session.BranchId;
        mark.Status = status;
        mark.Source = source;
        mark.MarkedAt = DateTimeOffset.UtcNow;
        mark.MarkedByUserId = tenantContext.UserId;
        mark.Reason = reason;
        mark.IsUndone = false;
        mark.UndoneAt = null;
        mark.UndoReason = null;
        return mark;
    }

    private async Task NotifyMarkAsync(ClassSession session, Student student, AttendanceMark mark, CancellationToken cancellationToken)
    {
        if (mark.Status is AttendanceMarkStatus.Present or AttendanceMarkStatus.Late && !mark.PresentNotified)
        {
            await NotifyAsync(session, [student], NotificationMessageType.AttendancePresent, $"attendance-present:{mark.MarkedAt.ToUnixTimeSeconds()}", cancellationToken);
            mark.PresentNotified = true;
        }

        if (mark.Status == AttendanceMarkStatus.Absent && !mark.AbsentNotified)
        {
            await NotifyAsync(session, [student], NotificationMessageType.AttendanceAbsent, $"attendance-absent:{mark.MarkedAt.ToUnixTimeSeconds()}", cancellationToken);
            mark.AbsentNotified = true;
        }
    }

    private async Task NotifyAsync(
        ClassSession session,
        IReadOnlyList<Student> students,
        NotificationMessageType messageType,
        string keyPrefix,
        CancellationToken cancellationToken)
    {
        var schedule = await db.ClassSchedules.AsNoTracking().FirstAsync(item => item.Id == session.ClassScheduleId, cancellationToken);
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstAsync(item => item.Id == session.TenantId, cancellationToken);
        var when = messageType switch
        {
            NotificationMessageType.ClassStopped => session.StoppedAt ?? DateTimeOffset.UtcNow,
            NotificationMessageType.ClassCancelled => session.CancelledAt ?? DateTimeOffset.UtcNow,
            _ => session.StartedAt ?? DateTimeOffset.UtcNow
        };
        foreach (var student in students)
        {
            if (string.IsNullOrWhiteSpace(student.GuardianPhone))
            {
                continue;
            }

            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["studentName"] = student.FullName,
                ["studentCode"] = student.StudentCode,
                ["className"] = schedule.Name,
                ["batchName"] = schedule.Name,
                ["time"] = AttendanceRules.FormatColombo(when),
                ["date"] = AttendanceRules.FormatDate(session.SessionDate),
                ["instituteName"] = tenant.Name
            };
            await notifications.EnqueueAsync(
                new EnqueueNotificationRequest(
                    messageType,
                    RecipientPhone: student.GuardianPhone,
                    RecipientName: student.GuardianName,
                    StudentId: student.Id,
                    StudentName: student.FullName,
                    StudentCode: student.StudentCode,
                    Language: "en",
                    IdempotencyKey: $"{keyPrefix}:{session.Id:N}:{student.Id:N}",
                    Variables: variables),
                session.TenantId,
                cancellationToken);
        }
    }

    private async Task<List<Student>> EnrolledStudentsAsync(ClassSession session, CancellationToken cancellationToken)
    {
        var schedule = await db.ClassSchedules.AsNoTracking().FirstAsync(item => item.Id == session.ClassScheduleId, cancellationToken);
        var studentIds = await db.StudentEnrollments.AsNoTracking()
            .Where(item => item.BatchId == schedule.BatchId && item.IsActive)
            .Select(item => item.StudentId)
            .ToListAsync(cancellationToken);
        return await db.Students.Where(item => studentIds.Contains(item.Id) && item.Status == StudentStatus.Active).ToListAsync(cancellationToken);
    }

    private async Task<ClassSchedule> RequireScheduleAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ClassSchedules.FirstOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken)
        ?? throw new EntityNotFoundException(nameof(ClassSchedule), id.ToString());

    private async Task<ClassSession> RequireSessionAsync(Guid id, CancellationToken cancellationToken)
    {
        var session = await db.ClassSessions.FirstOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(ClassSession), id.ToString());
        if (session.TenantId == Guid.Empty)
        {
            session.TenantId = TenantGuard.Require(tenantContext);
        }

        return session;
    }

    private async Task<ClassSession> RequireOpenForMarkingAsync(Guid id, CancellationToken cancellationToken)
    {
        var session = await RequireSessionAsync(id, cancellationToken);
        if (session.Status is ClassSessionStatus.Cancelled)
        {
            throw new DomainValidationException("INVALID_SESSION_STATE", "Attendance cannot be changed after the class is cancelled.");
        }

        if (session.Status is ClassSessionStatus.Scheduled)
        {
            throw new DomainValidationException("SESSION_NOT_STARTED", "Start the class before marking attendance.");
        }

        return session;
    }

    private async Task<AttendancePolicy> EnsurePolicyAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var policy = await db.AttendancePolicies.FirstOrDefaultAsync(cancellationToken);
        if (policy is not null)
        {
            return policy;
        }

        policy = new AttendancePolicy
        {
            TenantId = tenantId,
            LateAfterMinutes = AttendanceRules.DefaultLateAfterMinutes,
            MarkAbsentOnStop = true
        };
        db.AttendancePolicies.Add(policy);
        await db.SaveChangesAsync(cancellationToken);
        return policy;
    }

    private async Task<string> UniqueJoinCodeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = AttendanceRules.NewJoinCode();
            var taken = await db.ClassSessions.IgnoreQueryFilters().AnyAsync(
                item => !item.IsDeleted && item.TenantId == tenantId && item.JoinCode == code && item.Status == ClassSessionStatus.Started,
                cancellationToken);
            if (!taken)
            {
                return code;
            }
        }

        return AttendanceRules.NewJoinCode() + AttendanceRules.NewJoinCode()[..2];
    }

    private async Task PublishAsync(ClassSession session, CancellationToken cancellationToken)
    {
        var snapshot = await BuildBoardAsync(session, cancellationToken);
        await board.PublishAsync(session.TenantId == Guid.Empty ? TenantGuard.Require(tenantContext) : session.TenantId, snapshot, cancellationToken);
    }

    private async Task<AttendanceBoardDto> BuildBoardAsync(ClassSession session, CancellationToken cancellationToken)
    {
        var students = await EnrolledStudentsAsync(session, cancellationToken);
        var marks = await db.AttendanceMarks.AsNoTracking()
            .Where(item => item.ClassSessionId == session.Id && !item.IsUndone)
            .OrderByDescending(item => item.MarkedAt)
            .ToListAsync(cancellationToken);
        var names = students.ToDictionary(item => item.Id);
        var scans = marks
            .Where(item => item.Status is AttendanceMarkStatus.Present or AttendanceMarkStatus.Late)
            .Take(8)
            .Select(item => new AttendanceBoardScanDto(
                item.StudentId,
                names.TryGetValue(item.StudentId, out var student) ? student.FullName : "Student",
                names.TryGetValue(item.StudentId, out student) ? student.StudentCode : string.Empty,
                item.Status.ToString(),
                item.MarkedAt,
                AttendanceRules.FormatColombo(item.MarkedAt)))
            .ToList();
        return new AttendanceBoardDto(
            session.Id,
            students.Count,
            marks.Count(item => item.Status == AttendanceMarkStatus.Present),
            marks.Count(item => item.Status == AttendanceMarkStatus.Late),
            marks.Count(item => item.Status == AttendanceMarkStatus.Absent),
            scans);
    }

    private async Task<IReadOnlyList<ClassSessionDto>> MapSessionsAsync(IReadOnlyList<ClassSession> sessions, CancellationToken cancellationToken)
    {
        if (sessions.Count == 0)
        {
            return [];
        }

        var scheduleIds = sessions.Select(item => item.ClassScheduleId).Distinct().ToList();
        var schedules = await db.ClassSchedules.AsNoTracking().Where(item => scheduleIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var batchIds = schedules.Values.Select(item => item.BatchId).Distinct().ToList();
        var batches = await db.AcademicBatches.AsNoTracking().Where(item => batchIds.Contains(item.Id)).ToDictionaryAsync(item => item.Id, cancellationToken);
        var enrollmentCounts = await db.StudentEnrollments.AsNoTracking()
            .Where(item => batchIds.Contains(item.BatchId) && item.IsActive)
            .GroupBy(item => item.BatchId)
            .Select(group => new { BatchId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.BatchId, item => item.Count, cancellationToken);
        var sessionIds = sessions.Select(item => item.Id).ToList();
        var marks = await db.AttendanceMarks.AsNoTracking().Where(item => sessionIds.Contains(item.ClassSessionId) && !item.IsUndone).ToListAsync(cancellationToken);
        var marksBySession = marks.ToLookup(item => item.ClassSessionId);
        var result = new List<ClassSessionDto>();
        foreach (var session in sessions)
        {
            schedules.TryGetValue(session.ClassScheduleId, out var schedule);
            var batchName = schedule is not null && batches.TryGetValue(schedule.BatchId, out var batch) ? batch.Name : string.Empty;
            var enrolled = schedule is not null && enrollmentCounts.TryGetValue(schedule.BatchId, out var count) ? count : 0;
            var sessionMarks = marksBySession[session.Id].ToList();
            result.Add(new ClassSessionDto(
                session.Id,
                session.ClassScheduleId,
                schedule?.Name ?? "Class",
                schedule?.BatchId ?? Guid.Empty,
                batchName,
                session.SessionDate,
                session.Status.ToString(),
                session.StartedAt,
                session.StoppedAt,
                session.CancelledAt,
                session.JoinCode,
                enrolled,
                sessionMarks.Count(item => item.Status == AttendanceMarkStatus.Present),
                sessionMarks.Count(item => item.Status == AttendanceMarkStatus.Late),
                sessionMarks.Count(item => item.Status == AttendanceMarkStatus.Absent)));
        }

        return result;
    }

    private static AttendancePolicyDto ToPolicy(AttendancePolicy policy) =>
        new(policy.LateAfterMinutes, policy.MarkAbsentOnStop);

    private static AttendanceRegisterStudentDto ToRegisterStudent(Student student, AttendanceMark? mark) =>
        new(
            student.Id,
            student.StudentCode,
            student.FullName,
            mark is null || mark.IsUndone ? null : mark.Status.ToString(),
            mark is null || mark.IsUndone ? null : mark.Source.ToString(),
            mark is null || mark.IsUndone ? null : mark.MarkedAt,
            mark?.Reason ?? mark?.UndoReason,
            mark?.IsUndone ?? false);

    private static AttendanceDayPointDto CountPoint(DateOnly date, IEnumerable<AttendanceMark> marks)
    {
        var list = marks.ToList();
        return new AttendanceDayPointDto(
            date,
            list.Count(item => item.Status == AttendanceMarkStatus.Present),
            list.Count(item => item.Status == AttendanceMarkStatus.Late),
            list.Count(item => item.Status == AttendanceMarkStatus.Absent));
    }

    private static string? NormalizeReason(string? reason, bool required)
    {
        var trimmed = reason?.Trim();
        if (required && string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainValidationException("REASON_REQUIRED", "Enter a reason.");
        }

        if (trimmed is { Length: > 300 })
        {
            throw new DomainValidationException("INVALID_REASON", "The reason must be 300 characters or fewer.");
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
