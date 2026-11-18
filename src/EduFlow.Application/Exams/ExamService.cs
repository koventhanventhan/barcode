using System.Globalization;
using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Notifications;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Exams;

public sealed class ExamService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    INotificationService notificationService,
    IExamDocumentService documents,
    IFileStorage fileStorage,
    IAuditService auditService) : IExamService
{
    public async Task<IReadOnlyList<GradingSchemeDto>> GetGradingSchemesAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        if (!await db.GradingSchemes.AnyAsync(cancellationToken))
        {
            await CreateDefaultSchemeAsync(tenantId, cancellationToken);
        }

        return await MapSchemesAsync(cancellationToken);
    }

    public async Task<GradingSchemeDto> CreateGradingSchemeAsync(
        UpsertGradingSchemeRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var (name, bands) = ValidateScheme(request);
        var scheme = new GradingScheme
        {
            TenantId = tenantId,
            Name = name,
            IsDefault = request.IsDefault
        };
        db.GradingSchemes.Add(scheme);
        if (request.IsDefault)
        {
            await ClearOtherDefaultsAsync(scheme.Id, cancellationToken);
        }

        AddBoundaries(tenantId, scheme.Id, bands);
        await auditService.LogAsync("GradingScheme.Created", nameof(GradingScheme), scheme.Id.ToString(), new { scheme.Name }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await MapSchemesAsync(cancellationToken)).Single(s => s.Id == scheme.Id);
    }

    public async Task<GradingSchemeDto> UpdateGradingSchemeAsync(
        Guid id,
        UpsertGradingSchemeRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var scheme = await db.GradingSchemes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new DomainValidationException("SCHEME_NOT_FOUND", "Grading scheme was not found.");
        var (name, bands) = ValidateScheme(request);
        scheme.Name = name;
        scheme.IsDefault = request.IsDefault;
        if (request.IsDefault)
        {
            await ClearOtherDefaultsAsync(scheme.Id, cancellationToken);
        }

        var existing = await db.GradeBoundaries.Where(b => b.GradingSchemeId == id).ToListAsync(cancellationToken);
        foreach (var boundary in existing)
        {
            boundary.IsDeleted = true;
        }

        AddBoundaries(tenantId, scheme.Id, bands);
        await auditService.LogAsync("GradingScheme.Updated", nameof(GradingScheme), scheme.Id.ToString(), new { scheme.Name }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await MapSchemesAsync(cancellationToken)).Single(s => s.Id == scheme.Id);
    }

    public async Task DeleteGradingSchemeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var scheme = await db.GradingSchemes.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new DomainValidationException("SCHEME_NOT_FOUND", "Grading scheme was not found.");
        if (await db.Exams.AnyAsync(e => e.GradingSchemeId == id, cancellationToken))
        {
            throw new DomainValidationException("SCHEME_IN_USE", "This grading scheme is used by an exam.");
        }

        var boundaries = await db.GradeBoundaries.Where(b => b.GradingSchemeId == id).ToListAsync(cancellationToken);
        foreach (var boundary in boundaries)
        {
            boundary.IsDeleted = true;
        }

        scheme.IsDeleted = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExamListItemDto>> GetExamsAsync(
        Guid? batchId,
        ExamStatus? status,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var exams = await db.Exams.AsNoTracking()
            .Where(e => batchId == null || e.BatchId == batchId)
            .Where(e => status == null || e.Status == status)
            .OrderByDescending(e => e.ScheduledAt ?? e.CreatedAt)
            .ToListAsync(cancellationToken);
        return await MapListAsync(exams, cancellationToken);
    }

    public async Task<ExamSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var now = DateTimeOffset.UtcNow;
        var exams = await db.Exams.AsNoTracking().ToListAsync(cancellationToken);
        var upcoming = exams
            .Where(e => e.ScheduledAt >= now && e.Status is ExamStatus.Scheduled or ExamStatus.Locked)
            .OrderBy(e => e.ScheduledAt)
            .ToList();
        var mapped = await MapListAsync(upcoming.Take(5).ToList(), cancellationToken);
        return new ExamSummaryDto(
            upcoming.Count,
            exams.Count(e => e.Status == ExamStatus.Published),
            mapped);
    }

    public Task<ExamDetailDto> GetExamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        return MapDetailAsync(id, cancellationToken);
    }

    public async Task<ExamDetailDto> CreateExamAsync(UpsertExamRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var batch = await RequireBatchAsync(request.BatchId, cancellationToken);
        var schemeId = request.GradingSchemeId ?? await EnsureDefaultSchemeIdAsync(tenantId, cancellationToken);
        await RequireSchemeAsync(schemeId, cancellationToken);
        var subjects = await ValidateSubjectsAsync(request, cancellationToken);
        var exam = new Exam
        {
            TenantId = tenantId,
            Name = RequireName(request.Name),
            Kind = request.Kind,
            BatchId = batch.Id,
            EducationLevelId = batch.EducationLevelId,
            GradingSchemeId = schemeId,
            SeriesKey = ExamRules.NormalizeSeriesKey(request.SeriesKey, request.Name),
            Status = ExamStatus.Draft,
            IncludeMarksInNotification = request.IncludeMarksInNotification,
            ReminderDaysBefore = NormalizeReminderDays(request.ReminderDaysBefore),
            ReminderHoursBefore = NormalizeReminderHours(request.ReminderHoursBefore)
        };
        db.Exams.Add(exam);
        AddSubjects(tenantId, exam.Id, subjects);
        await auditService.LogAsync("Exam.Created", nameof(Exam), exam.Id.ToString(), new { exam.Name, exam.Kind, exam.BatchId }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(exam.Id, cancellationToken);
    }

    public async Task<ExamDetailDto> UpdateExamAsync(Guid id, UpsertExamRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var exam = await RequireExamAsync(id, cancellationToken);
        if (exam.Status != ExamStatus.Draft)
        {
            throw new DomainValidationException("EXAM_NOT_EDITABLE", "Only a draft exam can be edited. Unlock published results first if you need to change papers.");
        }

        var batch = await RequireBatchAsync(request.BatchId, cancellationToken);
        var schemeId = request.GradingSchemeId ?? exam.GradingSchemeId;
        await RequireSchemeAsync(schemeId, cancellationToken);
        var subjects = await ValidateSubjectsAsync(request, cancellationToken);
        exam.Name = RequireName(request.Name);
        exam.Kind = request.Kind;
        exam.BatchId = batch.Id;
        exam.EducationLevelId = batch.EducationLevelId;
        exam.GradingSchemeId = schemeId;
        exam.SeriesKey = ExamRules.NormalizeSeriesKey(request.SeriesKey, request.Name);
        exam.IncludeMarksInNotification = request.IncludeMarksInNotification;
        exam.ReminderDaysBefore = NormalizeReminderDays(request.ReminderDaysBefore);
        exam.ReminderHoursBefore = NormalizeReminderHours(request.ReminderHoursBefore);
        await SyncSubjectsAsync(tenantId, exam.Id, subjects, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(exam.Id, cancellationToken);
    }

    public async Task DeleteExamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var exam = await RequireExamAsync(id, cancellationToken);
        if (exam.Status != ExamStatus.Draft)
        {
            throw new DomainValidationException("EXAM_NOT_DELETABLE", "Only a draft exam can be deleted.");
        }

        await SoftDeleteChildrenAsync(exam.Id, cancellationToken);
        exam.IsDeleted = true;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExamDetailDto> ScheduleExamAsync(Guid id, ScheduleExamRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var exam = await RequireExamAsync(id, cancellationToken);
        if (exam.Status is ExamStatus.Locked or ExamStatus.Published)
        {
            throw new DomainValidationException("EXAM_LOCKED", "Unlock the exam before changing its schedule.");
        }

        if (string.IsNullOrWhiteSpace(request.Room) == false && request.Room.Trim().Length > 80)
        {
            throw new DomainValidationException("INVALID_ROOM", "Room must be 80 characters or fewer.");
        }

        var moved = exam.ScheduledAt != request.ScheduledAt;
        exam.ScheduledAt = request.ScheduledAt;
        exam.Room = string.IsNullOrWhiteSpace(request.Room) ? null : request.Room.Trim();
        exam.Status = ExamStatus.Scheduled;
        if (moved)
        {
            exam.ReminderSentAt = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (exam.ScheduledNotifiedAt is null)
        {
            var (enqueued, _, _) = await NotifyStudentsAsync(
                tenantId,
                exam,
                NotificationMessageType.ExamScheduled,
                static (examId, studentId, _) => $"exam-scheduled:{examId:N}:{studentId:N}",
                includeMarks: false,
                cancellationToken);
            if (enqueued > 0)
            {
                exam.ScheduledNotifiedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await auditService.LogAsync("Exam.Scheduled", nameof(Exam), exam.Id.ToString(), new { exam.ScheduledAt, exam.Room }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(exam.Id, cancellationToken);
    }

    public async Task<ExamDetailDto> LockExamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var exam = await RequireExamAsync(id, cancellationToken);
        if (exam.Status != ExamStatus.Scheduled || exam.ScheduledAt is null)
        {
            throw new DomainValidationException("EXAM_NOT_SCHEDULABLE", "Schedule the exam before locking marks.");
        }

        exam.Status = ExamStatus.Locked;
        exam.LockedAt = DateTimeOffset.UtcNow;
        await auditService.LogAsync("Exam.Locked", nameof(Exam), exam.Id.ToString(), cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(exam.Id, cancellationToken);
    }

    public async Task<ExamDetailDto> UnlockExamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var exam = await RequireExamAsync(id, cancellationToken);
        if (exam.Status != ExamStatus.Locked)
        {
            throw new DomainValidationException("EXAM_NOT_LOCKED", "Only a locked exam can be unlocked.");
        }

        exam.Status = ExamStatus.Scheduled;
        exam.LockedAt = null;
        await auditService.LogAsync("Exam.Unlocked", nameof(Exam), exam.Id.ToString(), cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(exam.Id, cancellationToken);
    }

    public async Task<PublishExamResultDto> PublishExamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var exam = await RequireExamAsync(id, cancellationToken);
        if (exam.Status == ExamStatus.Published)
        {
            var pending = await NotifyMarksReleasedAsync(tenantId, exam, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            var ranked = await db.ExamResults.CountAsync(r => r.ExamId == exam.Id, cancellationToken);
            return new PublishExamResultDto(ranked, pending.Enqueued, pending.Skipped);
        }

        if (exam.Status is not (ExamStatus.Scheduled or ExamStatus.Locked) || exam.ScheduledAt is null)
        {
            throw new DomainValidationException("EXAM_NOT_READY", "Schedule the exam before publishing results.");
        }

        var papers = await db.ExamSubjects.Where(s => s.ExamId == exam.Id).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        if (papers.Count == 0)
        {
            throw new DomainValidationException("EXAM_SUBJECTS_REQUIRED", "Add at least one subject before publishing.");
        }

        var students = await LoadEnrolledStudentsAsync(exam.BatchId, cancellationToken);
        if (students.Count == 0)
        {
            throw new DomainValidationException("EXAM_NO_STUDENTS", "This batch has no active students to rank.");
        }

        var marks = await db.ExamMarks.Where(m => m.ExamId == exam.Id).ToListAsync(cancellationToken);
        var markMap = marks.ToDictionary(m => (m.StudentId, m.ExamSubjectId));
        foreach (var student in students)
        {
            foreach (var paper in papers)
            {
                if (!markMap.ContainsKey((student.Id, paper.Id)))
                {
                    throw new DomainValidationException(
                        "MARKS_INCOMPLETE",
                        $"Enter marks or mark absent for {student.FullName} before publishing.");
                }
            }
        }

        var bands = await LoadBandsAsync(exam.GradingSchemeId, cancellationToken);
        foreach (var paper in papers)
        {
            var paperMarks = marks.Where(m => m.ExamSubjectId == paper.Id).ToList();
            foreach (var mark in paperMarks)
            {
                mark.Grade = mark.IsAbsent || mark.Marks is null
                    ? null
                    : ExamRules.ResolveGrade(ExamRules.Percentage(mark.Marks.Value, paper.MaxMarks), bands);
                mark.SubjectRank = null;
            }

            var assignments = ExamRules.AssignCompetitionRanks(paperMarks
                .Select(m => new ScoreRow(m.Id, m.IsAbsent ? 0 : m.Marks ?? 0, !m.IsAbsent))
                .ToList());
            var rankById = assignments.ToDictionary(a => a.Id, a => a.Rank);
            foreach (var mark in paperMarks)
            {
                mark.SubjectRank = rankById[mark.Id];
            }
        }

        var attendance = await LoadAttendanceAsync(students.Select(s => s.Id).ToList(), DateOnly.FromDateTime(exam.ScheduledAt.Value.UtcDateTime), cancellationToken);
        var existingResults = await db.ExamResults.Where(r => r.ExamId == exam.Id).ToListAsync(cancellationToken);
        var resultByStudent = existingResults.ToDictionary(r => r.StudentId);
        foreach (var student in students)
        {
            var obtained = 0m;
            var max = 0m;
            foreach (var paper in papers)
            {
                var mark = markMap[(student.Id, paper.Id)];
                max += paper.MaxMarks;
                if (!mark.IsAbsent && mark.Marks is not null)
                {
                    obtained += mark.Marks.Value;
                }
            }

            var percentage = ExamRules.Percentage(obtained, max);
            if (!resultByStudent.TryGetValue(student.Id, out var result))
            {
                result = new ExamResult { TenantId = tenantId, ExamId = exam.Id, StudentId = student.Id };
                db.ExamResults.Add(result);
                resultByStudent[student.Id] = result;
            }

            result.TotalMarks = obtained;
            result.MaxMarks = max;
            result.Percentage = percentage;
            result.Grade = ExamRules.ResolveGrade(percentage, bands);
            result.AttendancePercent = attendance.TryGetValue(student.Id, out var percent) ? percent : null;
        }

        var batchRanks = ExamRules.AssignCompetitionRanks(resultByStudent.Values
            .Select(r => new ScoreRow(r.Id, r.Percentage, true))
            .ToList());
        var batchRankById = batchRanks.ToDictionary(a => a.Id, a => a.Rank);
        foreach (var result in resultByStudent.Values)
        {
            result.BatchRank = batchRankById[result.Id];
        }

        exam.Status = ExamStatus.Published;
        exam.PublishedAt = DateTimeOffset.UtcNow;
        await auditService.LogAsync("Exam.Published", nameof(Exam), exam.Id.ToString(), new { Students = students.Count }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await RecomputeLevelRanksAsync(exam.SeriesKey, exam.EducationLevelId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var sent = await NotifyMarksReleasedAsync(tenantId, exam, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new PublishExamResultDto(students.Count, sent.Enqueued, sent.Skipped);
    }

    public async Task<ExamDetailDto> UnpublishExamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var exam = await RequireExamAsync(id, cancellationToken);
        if (exam.Status != ExamStatus.Published)
        {
            throw new DomainValidationException("EXAM_NOT_PUBLISHED", "This exam is not published.");
        }

        exam.Status = ExamStatus.Scheduled;
        exam.PublishedAt = null;
        await auditService.LogAsync("Exam.Unpublished", nameof(Exam), exam.Id.ToString(), cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await RecomputeLevelRanksAsync(exam.SeriesKey, exam.EducationLevelId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await MapDetailAsync(exam.Id, cancellationToken);
    }

    public async Task<MarksGridDto> GetMarksAsync(Guid examId, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var exam = await RequireExamAsync(examId, cancellationToken);
        return await BuildGridAsync(exam, cancellationToken);
    }

    public async Task<MarksGridDto> SaveMarksAsync(Guid examId, SaveMarksRequest request, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var exam = await RequireExamAsync(examId, cancellationToken);
        if (exam.Status is ExamStatus.Locked or ExamStatus.Published)
        {
            throw new DomainValidationException("MARKS_LOCKED", "Unlock the exam before changing marks.");
        }

        var papers = await db.ExamSubjects.Where(s => s.ExamId == exam.Id).ToListAsync(cancellationToken);
        var paperById = papers.ToDictionary(p => p.Id);
        var students = await LoadEnrolledStudentsAsync(exam.BatchId, cancellationToken);
        var studentIds = students.Select(s => s.Id).ToHashSet();
        var existing = await db.ExamMarks
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.ExamId == exam.Id)
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(m => (m.StudentId, m.ExamSubjectId));

        foreach (var entry in request.Entries)
        {
            if (!studentIds.Contains(entry.StudentId) || !paperById.TryGetValue(entry.ExamSubjectId, out var paper))
            {
                throw new DomainValidationException("INVALID_MARK_CELL", "A mark refers to a student or subject that is not on this exam.");
            }

            if (entry.IsAbsent && entry.Marks is not null)
            {
                throw new DomainValidationException("INVALID_MARKS", "An absent student cannot also have a mark.");
            }

            decimal? marks = null;
            if (!entry.IsAbsent)
            {
                if (entry.Marks is null)
                {
                    throw new DomainValidationException("MARKS_REQUIRED", "Enter a mark or mark the student absent.");
                }

                marks = decimal.Round(entry.Marks.Value, 2, MidpointRounding.AwayFromZero);
                if (marks < 0 || marks > paper.MaxMarks)
                {
                    throw new DomainValidationException("MARKS_OUT_OF_RANGE", $"Marks must be between 0 and {paper.MaxMarks:0.##}.");
                }
            }

            if (!byKey.TryGetValue((entry.StudentId, entry.ExamSubjectId), out var row))
            {
                row = new ExamMark
                {
                    TenantId = tenantId,
                    ExamId = exam.Id,
                    ExamSubjectId = entry.ExamSubjectId,
                    StudentId = entry.StudentId
                };
                db.ExamMarks.Add(row);
                byKey[(entry.StudentId, entry.ExamSubjectId)] = row;
            }

            row.IsDeleted = false;
            row.Marks = marks;
            row.IsAbsent = entry.IsAbsent;
            row.Grade = null;
            row.SubjectRank = null;
        }

        if (request.Remarks is { Count: > 0 })
        {
            var results = await db.ExamResults.Where(r => r.ExamId == exam.Id).ToListAsync(cancellationToken);
            var resultByStudent = results.ToDictionary(r => r.StudentId);
            foreach (var remark in request.Remarks)
            {
                if (!studentIds.Contains(remark.StudentId))
                {
                    throw new DomainValidationException("INVALID_REMARK", "Remarks refer to a student who is not on this exam.");
                }

                if (!resultByStudent.TryGetValue(remark.StudentId, out var result))
                {
                    result = new ExamResult
                    {
                        TenantId = tenantId,
                        ExamId = exam.Id,
                        StudentId = remark.StudentId,
                        Grade = string.Empty
                    };
                    db.ExamResults.Add(result);
                }

                result.TeacherRemarks = TrimRemark(remark.TeacherRemarks);
                result.PrincipalRemarks = TrimRemark(remark.PrincipalRemarks);
            }
        }

        await auditService.LogAsync("Exam.MarksSaved", nameof(Exam), exam.Id.ToString(), new { request.Entries.Count }, cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildGridAsync(exam, cancellationToken);
    }

    public async Task<RankListDto> GetRanksAsync(Guid examId, string scope, Guid? subjectId, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var exam = await RequireExamAsync(examId, cancellationToken);
        var normalized = (scope ?? "batch").Trim().ToLowerInvariant();
        if (normalized == "subject")
        {
            return await SubjectRanksAsync(exam, subjectId, cancellationToken);
        }

        if (normalized == "level")
        {
            var examIds = await db.Exams.AsNoTracking()
                .Where(e => e.Status == ExamStatus.Published && e.SeriesKey == exam.SeriesKey && e.EducationLevelId == exam.EducationLevelId)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
            if (exam.Status != ExamStatus.Published && !examIds.Contains(exam.Id))
            {
                examIds.Add(exam.Id);
            }

            return await ResultRanksAsync("level", null, examIds, cancellationToken);
        }

        return await ResultRanksAsync("batch", null, [exam.Id], cancellationToken);
    }

    public async Task<IReadOnlyList<GradeDistributionDto>> GetDistributionAsync(Guid examId, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var exam = await RequireExamAsync(examId, cancellationToken);
        var results = await db.ExamResults.AsNoTracking().Where(r => r.ExamId == exam.Id && r.Grade != "").ToListAsync(cancellationToken);
        var total = results.Count;
        var bands = await LoadBandsAsync(exam.GradingSchemeId, cancellationToken);
        return bands
            .OrderBy(b => b.SortOrder)
            .Select(b =>
            {
                var count = results.Count(r => string.Equals(r.Grade, b.Grade, StringComparison.OrdinalIgnoreCase));
                var percent = total == 0 ? 0 : decimal.Round(count * 100m / total, 2, MidpointRounding.AwayFromZero);
                return new GradeDistributionDto(b.Grade, count, percent);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<StudentTrendPointDto>> GetStudentTrendsAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        await EnsurePerformanceReportsEnabledAsync(cancellationToken);
        var results = await db.ExamResults.AsNoTracking()
            .Where(r => r.StudentId == studentId && r.Grade != "")
            .ToListAsync(cancellationToken);
        if (results.Count == 0)
        {
            return [];
        }

        var examIds = results.Select(r => r.ExamId).Distinct().ToList();
        var exams = await db.Exams.AsNoTracking()
            .Where(e => examIds.Contains(e.Id) && e.Status == ExamStatus.Published)
            .ToDictionaryAsync(e => e.Id, cancellationToken);
        return results
            .Where(r => exams.ContainsKey(r.ExamId))
            .Select(r =>
            {
                var exam = exams[r.ExamId];
                return new StudentTrendPointDto(exam.Id, exam.Name, exam.Kind.ToString(), exam.ScheduledAt, r.Percentage, r.Grade, r.BatchRank, r.LevelRank);
            })
            .OrderBy(p => p.ScheduledAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public async Task<StoredDownload> GetReportCardsPdfAsync(Guid examId, Guid? studentId, CancellationToken cancellationToken = default)
    {
        var tenantId = await EnsurePerformanceReportsEnabledAsync(cancellationToken);

        var exam = await RequireExamAsync(examId, cancellationToken);
        if (exam.Status != ExamStatus.Published)
        {
            throw new DomainValidationException("RESULTS_NOT_PUBLISHED", "Publish results before printing report cards.");
        }

        var results = await db.ExamResults.AsNoTracking()
            .Where(r => r.ExamId == exam.Id && r.Grade != "" && (studentId == null || r.StudentId == studentId))
            .ToListAsync(cancellationToken);
        if (results.Count == 0)
        {
            throw new DomainValidationException("REPORT_EMPTY", "No published results match this report card.");
        }

        var students = await db.Students.AsNoTracking()
            .Where(s => results.Select(r => r.StudentId).Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        var papers = await db.ExamSubjects.AsNoTracking().Where(s => s.ExamId == exam.Id).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        var subjectNames = await db.Subjects.AsNoTracking()
            .Where(s => papers.Select(p => p.SubjectId).Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var marks = await db.ExamMarks.AsNoTracking().Where(m => m.ExamId == exam.Id).ToListAsync(cancellationToken);
        var batch = await db.AcademicBatches.AsNoTracking().FirstAsync(b => b.Id == exam.BatchId, cancellationToken);
        var level = await db.EducationLevels.AsNoTracking().FirstAsync(l => l.Id == exam.EducationLevelId, cancellationToken);
        var branding = await LoadBrandingAsync(tenantId, cancellationToken);
        var cards = new List<ReportCardPdfContent>();
        foreach (var result in results.OrderBy(r => students[r.StudentId].FullName))
        {
            var student = students[result.StudentId];
            var lines = papers.Select(paper =>
            {
                var mark = marks.FirstOrDefault(m => m.ExamSubjectId == paper.Id && m.StudentId == student.Id);
                return new ReportCardSubjectLine(
                    subjectNames.GetValueOrDefault(paper.SubjectId, "Subject"),
                    paper.MaxMarks,
                    mark?.Marks,
                    mark?.IsAbsent ?? false,
                    mark?.Grade,
                    mark?.SubjectRank);
            }).ToList();
            cards.Add(new ReportCardPdfContent(
                branding.Name,
                branding.Address,
                branding.Phone,
                branding.PrimaryHex,
                branding.Logo,
                branding.ShowProductName,
                await ReadImageAsync(student.PhotoUrl, cancellationToken),
                student.FullName,
                student.StudentCode,
                batch.Name,
                level.Name,
                exam.Name,
                exam.Kind.ToString(),
                exam.ScheduledAt is null ? null : ExamRules.FormatExamWhen(exam.ScheduledAt.Value),
                exam.Room,
                lines,
                result.TotalMarks,
                result.MaxMarks,
                result.Percentage,
                result.Grade,
                result.BatchRank,
                result.LevelRank,
                result.AttendancePercent,
                result.TeacherRemarks,
                result.PrincipalRemarks));
        }

        var fileName = studentId is null
            ? $"report-cards-{Sanitize(exam.Name)}.pdf"
            : $"report-card-{Sanitize(students[studentId.Value].StudentCode)}.pdf";
        return new StoredDownload(documents.BuildReportCards(cards), "application/pdf", fileName);
    }

    public async Task<ExamReminderRunDto> ProcessExamRemindersAsync(
        DateTimeOffset? nowOverride = null,
        Guid? tenantIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var now = nowOverride ?? DateTimeOffset.UtcNow;
        if (tenantIdOverride is Guid forced)
        {
            using (tenantContext.BeginSystemScope(forced))
            {
                return await ProcessRemindersCoreAsync(now, forced, cancellationToken);
            }
        }

        var tenantId = TenantGuard.Require(tenantContext);
        return await ProcessRemindersCoreAsync(now, tenantId, cancellationToken);
    }

    private async Task<ExamReminderRunDto> ProcessRemindersCoreAsync(DateTimeOffset now, Guid tenantId, CancellationToken cancellationToken)
    {
        var config = await db.TenantNotificationConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        var exams = await db.Exams
            .Where(e => e.ScheduledAt != null && e.ReminderSentAt == null && (e.Status == ExamStatus.Scheduled || e.Status == ExamStatus.Locked))
            .ToListAsync(cancellationToken);
        var reminded = 0;
        var enqueued = 0;
        var skipped = 0;
        foreach (var exam in exams)
        {
            var days = exam.ReminderDaysBefore ?? config?.ExamReminderDaysBefore ?? 1;
            var hours = exam.ReminderHoursBefore ?? config?.ExamReminderHoursBefore ?? 0;
            var due = ExamRules.IsReminderDue(now, exam.ScheduledAt!.Value, days, hours, alreadySent: false);
            if (!due)
            {
                if (exam.ScheduledAt <= now)
                {
                    exam.ReminderSentAt = now;
                }

                continue;
            }

            var sent = await NotifyStudentsAsync(
                tenantId,
                exam,
                NotificationMessageType.ExamReminder,
                (examId, studentId, scheduled) => $"exam-reminder:{examId:N}:{studentId:N}:{scheduled.UtcTicks}",
                includeMarks: false,
                cancellationToken);
            enqueued += sent.Enqueued;
            skipped += sent.Skipped;
            if (!sent.FeatureBlocked)
            {
                exam.ReminderSentAt = now;
                reminded++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ExamReminderRunDto(exams.Count, reminded, enqueued, skipped);
    }

    private async Task<(int Enqueued, int Skipped, bool FeatureBlocked)> NotifyStudentsAsync(
        Guid tenantId,
        Exam exam,
        NotificationMessageType messageType,
        Func<Guid, Guid, DateTimeOffset, string> idempotency,
        bool includeMarks,
        CancellationToken cancellationToken)
    {
        var students = await LoadEnrolledStudentsAsync(exam.BatchId, cancellationToken);
        var batch = await db.AcademicBatches.AsNoTracking().FirstAsync(b => b.Id == exam.BatchId, cancellationToken);
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
        var config = await db.TenantNotificationConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        var language = config?.DefaultLanguage ?? "en";
        var when = exam.ScheduledAt is null ? string.Empty : ExamRules.FormatExamWhen(exam.ScheduledAt.Value);
        var results = includeMarks
            ? await db.ExamResults.Where(r => r.ExamId == exam.Id).ToDictionaryAsync(r => r.StudentId, cancellationToken)
            : [];
        var enqueued = 0;
        var skipped = 0;
        var blocked = 0;
        var attempted = 0;
        foreach (var student in students)
        {
            if (string.IsNullOrWhiteSpace(student.GuardianPhone))
            {
                skipped++;
                continue;
            }

            attempted++;
            var marksText = "results published";
            var gradeText = string.Empty;
            if (includeMarks && results.TryGetValue(student.Id, out var result))
            {
                if (exam.IncludeMarksInNotification)
                {
                    marksText = string.Create(CultureInfo.InvariantCulture, $"{result.Percentage:0.##}% ({result.Grade})");
                    gradeText = result.Grade;
                }
            }

            var enqueue = await notificationService.EnqueueAsync(
                new EnqueueNotificationRequest(
                    MessageType: messageType,
                    RecipientPhone: student.GuardianPhone,
                    RecipientName: student.GuardianName,
                    StudentId: student.Id,
                    StudentName: student.FullName,
                    StudentCode: student.StudentCode,
                    Language: language,
                    IdempotencyKey: idempotency(exam.Id, student.Id, exam.ScheduledAt ?? DateTimeOffset.UnixEpoch),
                    Variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["studentName"] = student.FullName,
                        ["studentCode"] = student.StudentCode,
                        ["className"] = batch.Name,
                        ["batchName"] = batch.Name,
                        ["examName"] = exam.Name,
                        ["examDate"] = when,
                        ["marks"] = marksText,
                        ["grade"] = gradeText,
                        ["instituteName"] = tenant.Name
                    }),
                tenantId,
                cancellationToken);
            if (enqueue.SkippedReason == "Skipped:FeatureOff")
            {
                blocked++;
                skipped++;
                continue;
            }

            if (enqueue.EnqueuedCount > 0 || enqueue.WasDuplicate)
            {
                enqueued += Math.Max(enqueue.EnqueuedCount, enqueue.WasDuplicate ? 1 : 0);
            }
            else
            {
                skipped++;
            }
        }

        var featureBlocked = attempted > 0 && blocked == attempted;
        return (enqueued, skipped, featureBlocked);
    }

    private async Task<(int Enqueued, int Skipped)> NotifyMarksReleasedAsync(Guid tenantId, Exam exam, CancellationToken cancellationToken)
    {
        var results = await db.ExamResults.Where(r => r.ExamId == exam.Id && !r.MarksReleasedNotified).ToListAsync(cancellationToken);
        if (results.Count == 0)
        {
            return (0, 0);
        }

        var pendingIds = results.Select(r => r.StudentId).ToHashSet();
        var students = (await LoadEnrolledStudentsAsync(exam.BatchId, cancellationToken)).Where(s => pendingIds.Contains(s.Id)).ToList();
        var batch = await db.AcademicBatches.AsNoTracking().FirstAsync(b => b.Id == exam.BatchId, cancellationToken);
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
        var config = await db.TenantNotificationConfigs.AsNoTracking().FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);
        var language = config?.DefaultLanguage ?? "en";
        var when = exam.ScheduledAt is null ? string.Empty : ExamRules.FormatExamWhen(exam.ScheduledAt.Value);
        var resultByStudent = results.ToDictionary(r => r.StudentId);
        var enqueued = 0;
        var skipped = 0;
        foreach (var student in students)
        {
            var result = resultByStudent[student.Id];
            if (string.IsNullOrWhiteSpace(student.GuardianPhone))
            {
                skipped++;
                continue;
            }

            var marksText = exam.IncludeMarksInNotification
                ? string.Create(CultureInfo.InvariantCulture, $"{result.Percentage:0.##}% ({result.Grade})")
                : "results published";
            var gradeText = exam.IncludeMarksInNotification ? result.Grade : string.Empty;
            var enqueue = await notificationService.EnqueueAsync(
                new EnqueueNotificationRequest(
                    MessageType: NotificationMessageType.MarksReleased,
                    RecipientPhone: student.GuardianPhone,
                    RecipientName: student.GuardianName,
                    StudentId: student.Id,
                    StudentName: student.FullName,
                    StudentCode: student.StudentCode,
                    Language: language,
                    IdempotencyKey: $"marks-released:{exam.Id:N}:{student.Id:N}",
                    Variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["studentName"] = student.FullName,
                        ["studentCode"] = student.StudentCode,
                        ["className"] = batch.Name,
                        ["batchName"] = batch.Name,
                        ["examName"] = exam.Name,
                        ["examDate"] = when,
                        ["marks"] = marksText,
                        ["grade"] = gradeText,
                        ["instituteName"] = tenant.Name
                    }),
                tenantId,
                cancellationToken);
            if (enqueue.SkippedReason == "Skipped:FeatureOff")
            {
                skipped++;
                continue;
            }

            if (enqueue.EnqueuedCount > 0 || enqueue.WasDuplicate)
            {
                result.MarksReleasedNotified = true;
                enqueued += enqueue.EnqueuedCount;
            }
            else
            {
                skipped++;
            }
        }

        return (enqueued, skipped);
    }

    private async Task RecomputeLevelRanksAsync(string seriesKey, Guid educationLevelId, CancellationToken cancellationToken)
    {
        var series = await db.Exams.AsNoTracking()
            .Where(e => e.SeriesKey == seriesKey && e.EducationLevelId == educationLevelId)
            .Select(e => new { e.Id, e.Status })
            .ToListAsync(cancellationToken);
        var publishedIds = series.Where(e => e.Status == ExamStatus.Published).Select(e => e.Id).ToList();
        var results = await db.ExamResults.Where(r => publishedIds.Contains(r.ExamId) && r.Grade != "").ToListAsync(cancellationToken);
        var assignments = ExamRules.AssignCompetitionRanks(results.Select(r => new ScoreRow(r.Id, r.Percentage, true)).ToList());
        var map = assignments.ToDictionary(a => a.Id, a => a.Rank);
        foreach (var result in results)
        {
            result.LevelRank = map[result.Id];
        }

        var unpublishedIds = series.Where(e => e.Status != ExamStatus.Published).Select(e => e.Id).ToList();
        if (unpublishedIds.Count == 0)
        {
            return;
        }

        var stale = await db.ExamResults.Where(r => unpublishedIds.Contains(r.ExamId) && r.LevelRank != null).ToListAsync(cancellationToken);
        foreach (var result in stale)
        {
            result.LevelRank = null;
        }
    }

    private async Task<RankListDto> ResultRanksAsync(string scope, string? subjectName, IReadOnlyList<Guid> examIds, CancellationToken cancellationToken)
    {
        var results = await db.ExamResults.AsNoTracking().Where(r => examIds.Contains(r.ExamId) && r.Grade != "").ToListAsync(cancellationToken);
        var exams = await db.Exams.AsNoTracking().Where(e => examIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, cancellationToken);
        var batches = await db.AcademicBatches.AsNoTracking().Where(b => exams.Values.Select(e => e.BatchId).Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
        var students = await db.Students.AsNoTracking().Where(s => results.Select(r => r.StudentId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var rows = results
            .Where(r => students.ContainsKey(r.StudentId) && exams.ContainsKey(r.ExamId))
            .Select(r =>
            {
                var student = students[r.StudentId];
                var exam = exams[r.ExamId];
                var batchName = batches.TryGetValue(exam.BatchId, out var batch) ? batch.Name : string.Empty;
                var rank = scope == "level" ? r.LevelRank : r.BatchRank;
                return new RankRowDto(student.Id, student.FullName, student.StudentCode, batchName, r.TotalMarks, r.MaxMarks, r.Percentage, r.Grade, rank, false);
            })
            .OrderBy(r => r.Rank ?? int.MaxValue)
            .ThenBy(r => r.StudentName)
            .ToList();
        return new RankListDto(scope, subjectName, rows);
    }

    private async Task<RankListDto> SubjectRanksAsync(Exam exam, Guid? subjectId, CancellationToken cancellationToken)
    {
        var seriesExamIds = await db.Exams.AsNoTracking()
            .Where(e => e.SeriesKey == exam.SeriesKey && e.EducationLevelId == exam.EducationLevelId && (e.Status == ExamStatus.Published || e.Id == exam.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        var papers = await db.ExamSubjects.AsNoTracking()
            .Where(s => seriesExamIds.Contains(s.ExamId) && (subjectId == null || s.SubjectId == subjectId))
            .ToListAsync(cancellationToken);
        if (subjectId is null && papers.Count > 0)
        {
            var firstSubject = papers.Where(p => p.ExamId == exam.Id).OrderBy(p => p.SortOrder).Select(p => p.SubjectId).FirstOrDefault();
            papers = papers.Where(p => p.SubjectId == firstSubject).ToList();
            subjectId = firstSubject == Guid.Empty ? null : firstSubject;
        }

        var subjectName = subjectId is null
            ? null
            : await db.Subjects.AsNoTracking().Where(s => s.Id == subjectId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken);
        var paperIds = papers.Select(p => p.Id).ToList();
        var paperById = papers.ToDictionary(p => p.Id);
        var marks = await db.ExamMarks.AsNoTracking().Where(m => paperIds.Contains(m.ExamSubjectId)).ToListAsync(cancellationToken);
        var exams = await db.Exams.AsNoTracking().Where(e => seriesExamIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, cancellationToken);
        var batches = await db.AcademicBatches.AsNoTracking().Where(b => exams.Values.Select(e => e.BatchId).Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
        var students = await db.Students.AsNoTracking().Where(s => marks.Select(m => m.StudentId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        var ranked = ExamRules.AssignCompetitionRanks(marks
            .Select(m =>
            {
                var max = paperById[m.ExamSubjectId].MaxMarks;
                var score = m.IsAbsent || m.Marks is null ? 0 : ExamRules.Percentage(m.Marks.Value, max);
                return new ScoreRow(m.Id, score, !m.IsAbsent && m.Marks is not null);
            })
            .ToList());
        var rankById = ranked.ToDictionary(a => a.Id, a => a.Rank);
        var rows = marks
            .Where(m => students.ContainsKey(m.StudentId))
            .Select(m =>
            {
                var student = students[m.StudentId];
                var paper = paperById[m.ExamSubjectId];
                var owner = exams[paper.ExamId];
                var batchName = batches.TryGetValue(owner.BatchId, out var batch) ? batch.Name : string.Empty;
                var percentage = m.IsAbsent || m.Marks is null ? 0 : ExamRules.Percentage(m.Marks.Value, paper.MaxMarks);
                return new RankRowDto(student.Id, student.FullName, student.StudentCode, batchName, m.Marks ?? 0, paper.MaxMarks, percentage, m.Grade, rankById[m.Id], m.IsAbsent);
            })
            .OrderBy(r => r.Rank ?? int.MaxValue)
            .ThenBy(r => r.StudentName)
            .ToList();
        return new RankListDto("subject", subjectName, rows);
    }

    private async Task<MarksGridDto> BuildGridAsync(Exam exam, CancellationToken cancellationToken)
    {
        var students = await LoadEnrolledStudentsAsync(exam.BatchId, cancellationToken);
        var papers = await db.ExamSubjects.AsNoTracking().Where(s => s.ExamId == exam.Id).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        var names = await db.Subjects.AsNoTracking().Where(s => papers.Select(p => p.SubjectId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var cells = await db.ExamMarks.AsNoTracking().Where(m => m.ExamId == exam.Id).ToListAsync(cancellationToken);
        var remarks = await db.ExamResults.AsNoTracking().Where(r => r.ExamId == exam.Id).ToListAsync(cancellationToken);
        return new MarksGridDto(
            exam.Id,
            exam.Status.ToString(),
            exam.Status is ExamStatus.Draft or ExamStatus.Scheduled,
            students.Select(s => new MarksStudentDto(s.Id, s.FullName, s.StudentCode)).ToList(),
            papers.Select(p => new MarksSubjectDto(p.Id, p.SubjectId, names.GetValueOrDefault(p.SubjectId, "Subject"), p.MaxMarks)).ToList(),
            cells.Select(c => new MarkCellDto(c.StudentId, c.ExamSubjectId, c.Marks, c.IsAbsent, c.Grade, c.SubjectRank)).ToList(),
            remarks.Select(r => new StudentRemarkDto(r.StudentId, r.TeacherRemarks, r.PrincipalRemarks)).ToList());
    }

    private async Task<List<Student>> LoadEnrolledStudentsAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var studentIds = await db.StudentEnrollments.AsNoTracking()
            .Where(e => e.BatchId == batchId && e.IsActive)
            .Select(e => e.StudentId)
            .ToListAsync(cancellationToken);
        return await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id) && s.Status == StudentStatus.Active)
            .OrderBy(s => s.FullName)
            .ThenBy(s => s.StudentCode)
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, decimal>> LoadAttendanceAsync(IReadOnlyList<Guid> studentIds, DateOnly cutoff, CancellationToken cancellationToken)
    {
        var rows = await (
            from mark in db.AttendanceMarks.AsNoTracking()
            join session in db.ClassSessions.AsNoTracking() on mark.ClassSessionId equals session.Id
            where studentIds.Contains(mark.StudentId) && !mark.IsUndone && session.SessionDate <= cutoff
            select new { mark.StudentId, mark.Status }).ToListAsync(cancellationToken);
        return rows
            .GroupBy(r => r.StudentId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var present = g.Count(x => x.Status is AttendanceMarkStatus.Present or AttendanceMarkStatus.Late);
                    return decimal.Round(present * 100m / g.Count(), 2, MidpointRounding.AwayFromZero);
                });
    }

    private async Task CreateDefaultSchemeAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var scheme = new GradingScheme { TenantId = tenantId, Name = "Standard grades", IsDefault = true };
        db.GradingSchemes.Add(scheme);
        AddBoundaries(tenantId, scheme.Id, ExamRules.DefaultBands);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid> EnsureDefaultSchemeIdAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var existing = await db.GradingSchemes.OrderByDescending(s => s.IsDefault).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        await CreateDefaultSchemeAsync(tenantId, cancellationToken);
        return await db.GradingSchemes.Where(s => s.IsDefault).Select(s => s.Id).FirstAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<GradingSchemeDto>> MapSchemesAsync(CancellationToken cancellationToken)
    {
        var schemes = await db.GradingSchemes.AsNoTracking().OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name).ToListAsync(cancellationToken);
        var boundaries = await db.GradeBoundaries.AsNoTracking().Where(b => schemes.Select(s => s.Id).Contains(b.GradingSchemeId)).ToListAsync(cancellationToken);
        return schemes.Select(s => new GradingSchemeDto(
            s.Id,
            s.Name,
            s.IsDefault,
            boundaries.Where(b => b.GradingSchemeId == s.Id)
                .OrderBy(b => b.SortOrder)
                .Select(b => new GradeBoundaryDto(b.Id, b.Grade, b.MinPercent, b.MaxPercent, b.GradePoint, b.SortOrder))
                .ToList())).ToList();
    }

    private async Task<IReadOnlyList<ExamListItemDto>> MapListAsync(IReadOnlyList<Exam> exams, CancellationToken cancellationToken)
    {
        if (exams.Count == 0)
        {
            return [];
        }

        var batches = await db.AcademicBatches.AsNoTracking().Where(b => exams.Select(e => e.BatchId).Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
        var counts = await db.ExamSubjects.AsNoTracking()
            .Where(s => exams.Select(e => e.Id).Contains(s.ExamId))
            .GroupBy(s => s.ExamId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return exams.Select(e => new ExamListItemDto(
            e.Id,
            e.Name,
            e.Kind.ToString(),
            e.Status.ToString(),
            e.BatchId,
            batches.TryGetValue(e.BatchId, out var batch) ? batch.Name : string.Empty,
            e.EducationLevelId,
            e.SeriesKey,
            e.ScheduledAt,
            e.Room,
            counts.GetValueOrDefault(e.Id),
            e.IncludeMarksInNotification,
            e.PublishedAt)).ToList();
    }

    private async Task<ExamDetailDto> MapDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new DomainValidationException("EXAM_NOT_FOUND", "Exam was not found.");
        var batch = await db.AcademicBatches.AsNoTracking().FirstAsync(b => b.Id == exam.BatchId, cancellationToken);
        var level = await db.EducationLevels.AsNoTracking().FirstAsync(l => l.Id == exam.EducationLevelId, cancellationToken);
        var scheme = await db.GradingSchemes.AsNoTracking().FirstAsync(s => s.Id == exam.GradingSchemeId, cancellationToken);
        var papers = await db.ExamSubjects.AsNoTracking().Where(s => s.ExamId == exam.Id).OrderBy(s => s.SortOrder).ToListAsync(cancellationToken);
        var subjects = await db.Subjects.AsNoTracking().Where(s => papers.Select(p => p.SubjectId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
        return new ExamDetailDto(
            exam.Id,
            exam.Name,
            exam.Kind.ToString(),
            exam.Status.ToString(),
            exam.BatchId,
            batch.Name,
            exam.EducationLevelId,
            level.Name,
            exam.GradingSchemeId,
            scheme.Name,
            exam.SeriesKey,
            exam.ScheduledAt,
            exam.Room,
            exam.ReminderDaysBefore,
            exam.ReminderHoursBefore,
            exam.IncludeMarksInNotification,
            exam.ScheduledNotifiedAt,
            exam.ReminderSentAt,
            exam.LockedAt,
            exam.PublishedAt,
            papers.Select(p => new ExamSubjectDto(
                p.Id,
                p.SubjectId,
                subjects[p.SubjectId].Name,
                subjects[p.SubjectId].Code,
                p.MaxMarks,
                p.ScheduledAt,
                p.Room,
                p.SortOrder)).ToList());
    }

    private async Task<Exam> RequireExamAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Exams.FirstOrDefaultAsync(e => e.Id == id, cancellationToken)
        ?? throw new DomainValidationException("EXAM_NOT_FOUND", "Exam was not found.");

    private async Task<AcademicBatch> RequireBatchAsync(Guid id, CancellationToken cancellationToken)
    {
        var batch = await db.AcademicBatches.FirstOrDefaultAsync(b => b.Id == id && b.IsActive, cancellationToken)
            ?? throw new DomainValidationException("BATCH_NOT_FOUND", "Choose an active batch.");
        return batch;
    }

    private async Task RequireSchemeAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.GradingSchemes.AnyAsync(s => s.Id == id, cancellationToken))
        {
            throw new DomainValidationException("SCHEME_NOT_FOUND", "Choose a grading scheme.");
        }
    }

    private async Task<IReadOnlyList<GradeBandInput>> LoadBandsAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        var rows = await db.GradeBoundaries.AsNoTracking().Where(b => b.GradingSchemeId == schemeId).OrderBy(b => b.SortOrder).ToListAsync(cancellationToken);
        var bands = rows.Select(b => new GradeBandInput(b.Grade, b.MinPercent, b.MaxPercent, b.GradePoint, b.SortOrder)).ToList();
        ExamRules.ValidateBands(bands);
        return bands;
    }

    private async Task<List<UpsertExamSubjectRequest>> ValidateSubjectsAsync(UpsertExamRequest request, CancellationToken cancellationToken)
    {
        if (request.Subjects is null || request.Subjects.Count == 0)
        {
            throw new DomainValidationException("EXAM_SUBJECTS_REQUIRED", "Add at least one subject.");
        }

        if (request.Subjects.Select(s => s.SubjectId).Distinct().Count() != request.Subjects.Count)
        {
            throw new DomainValidationException("DUPLICATE_SUBJECT", "Each subject can appear once on an exam.");
        }

        foreach (var subject in request.Subjects)
        {
            if (subject.MaxMarks is < 0.01m or > 1000m)
            {
                throw new DomainValidationException("INVALID_MAX_MARKS", "Maximum marks must be between 0.01 and 1000.");
            }
        }

        var ids = request.Subjects.Select(s => s.SubjectId).ToList();
        var found = await db.Subjects.CountAsync(s => ids.Contains(s.Id) && s.IsActive, cancellationToken);
        if (found != ids.Count)
        {
            throw new DomainValidationException("SUBJECT_NOT_FOUND", "One of the subjects is not active.");
        }

        return request.Subjects.OrderBy(s => s.SortOrder).ToList();
    }

    private void AddSubjects(Guid tenantId, Guid examId, IReadOnlyList<UpsertExamSubjectRequest> subjects)
    {
        foreach (var subject in subjects)
        {
            db.ExamSubjects.Add(new ExamSubject
            {
                TenantId = tenantId,
                ExamId = examId,
                SubjectId = subject.SubjectId,
                MaxMarks = decimal.Round(subject.MaxMarks, 2, MidpointRounding.AwayFromZero),
                ScheduledAt = subject.ScheduledAt,
                Room = string.IsNullOrWhiteSpace(subject.Room) ? null : subject.Room.Trim(),
                SortOrder = subject.SortOrder
            });
        }
    }

    private async Task SyncSubjectsAsync(Guid tenantId, Guid examId, IReadOnlyList<UpsertExamSubjectRequest> subjects, CancellationToken cancellationToken)
    {
        var existing = await db.ExamSubjects.IgnoreQueryFilters().Where(s => s.TenantId == tenantId && s.ExamId == examId).ToListAsync(cancellationToken);
        var keep = subjects.Select(s => s.SubjectId).ToHashSet();
        foreach (var row in existing.Where(s => !keep.Contains(s.SubjectId)))
        {
            row.IsDeleted = true;
        }

        foreach (var subject in subjects)
        {
            var row = existing.FirstOrDefault(s => s.SubjectId == subject.SubjectId);
            if (row is null)
            {
                db.ExamSubjects.Add(new ExamSubject
                {
                    TenantId = tenantId,
                    ExamId = examId,
                    SubjectId = subject.SubjectId,
                    MaxMarks = decimal.Round(subject.MaxMarks, 2, MidpointRounding.AwayFromZero),
                    ScheduledAt = subject.ScheduledAt,
                    Room = string.IsNullOrWhiteSpace(subject.Room) ? null : subject.Room.Trim(),
                    SortOrder = subject.SortOrder
                });
                continue;
            }

            row.IsDeleted = false;
            row.MaxMarks = decimal.Round(subject.MaxMarks, 2, MidpointRounding.AwayFromZero);
            row.ScheduledAt = subject.ScheduledAt;
            row.Room = string.IsNullOrWhiteSpace(subject.Room) ? null : subject.Room.Trim();
            row.SortOrder = subject.SortOrder;
        }
    }

    private async Task SoftDeleteChildrenAsync(Guid examId, CancellationToken cancellationToken)
    {
        var subjects = await db.ExamSubjects.Where(s => s.ExamId == examId).ToListAsync(cancellationToken);
        var marks = await db.ExamMarks.Where(m => m.ExamId == examId).ToListAsync(cancellationToken);
        var results = await db.ExamResults.Where(r => r.ExamId == examId).ToListAsync(cancellationToken);
        foreach (var subject in subjects)
        {
            subject.IsDeleted = true;
        }

        foreach (var mark in marks)
        {
            mark.IsDeleted = true;
        }

        foreach (var result in results)
        {
            result.IsDeleted = true;
        }
    }

    private void AddBoundaries(Guid tenantId, Guid schemeId, IReadOnlyList<GradeBandInput> bands)
    {
        foreach (var band in bands)
        {
            db.GradeBoundaries.Add(new GradeBoundary
            {
                TenantId = tenantId,
                GradingSchemeId = schemeId,
                Grade = band.Grade.Trim(),
                MinPercent = band.MinPercent,
                MaxPercent = band.MaxPercent,
                GradePoint = band.GradePoint,
                SortOrder = band.SortOrder
            });
        }
    }

    private async Task ClearOtherDefaultsAsync(Guid keepId, CancellationToken cancellationToken)
    {
        var others = await db.GradingSchemes.Where(s => s.IsDefault && s.Id != keepId).ToListAsync(cancellationToken);
        foreach (var other in others)
        {
            other.IsDefault = false;
        }
    }

    private async Task<(string Name, string? Address, string? Phone, string PrimaryHex, byte[]? Logo, bool ShowProductName)> LoadBrandingAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var profile = await db.BrandingProfiles.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.TenantId == tenantId, cancellationToken);
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstAsync(t => t.Id == tenantId, cancellationToken);
        var name = string.IsNullOrWhiteSpace(profile?.InstituteDisplayName) ? tenant.Name : profile!.InstituteDisplayName;
        var primary = string.IsNullOrWhiteSpace(profile?.PrimaryColor) ? BrandingProfile.DefaultPrimaryColor : profile!.PrimaryColor;
        return (name, tenant.Address, tenant.ContactPhone, primary, await ReadImageAsync(profile?.LogoUrl, cancellationToken), profile?.RemoveProductBranding != true);
    }

    private async Task<byte[]?> ReadImageAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var opened = fileStorage.OpenRead(url);
        if (opened is null)
        {
            return null;
        }

        await using (opened)
        {
            using var memory = new MemoryStream();
            await opened.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var jpeg = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8;
            var png = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50;
            return jpeg || png ? bytes : null;
        }
    }

    private static (string Name, IReadOnlyList<GradeBandInput> Bands) ValidateScheme(UpsertGradingSchemeRequest request)
    {
        var name = RequireName(request.Name);
        var bands = request.Boundaries.Select(b => new GradeBandInput(b.Grade, b.MinPercent, b.MaxPercent, b.GradePoint, b.SortOrder)).ToList();
        ExamRules.ValidateBands(bands);
        return (name, bands);
    }

    private static string RequireName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is < 2 or > 150)
        {
            throw new DomainValidationException("INVALID_NAME", "Name must be between 2 and 150 characters.");
        }

        return trimmed;
    }

    private static int? NormalizeReminderDays(int? days)
    {
        if (days is null)
        {
            return null;
        }

        if (days is < 0 or > 60)
        {
            throw new DomainValidationException("INVALID_REMINDER_DAYS", "Reminder days must be between 0 and 60.");
        }

        return days;
    }

    private static int? NormalizeReminderHours(int? hours)
    {
        if (hours is null)
        {
            return null;
        }

        if (hours is < 0 or > 23)
        {
            throw new DomainValidationException("INVALID_REMINDER_HOURS", "Reminder hours must be between 0 and 23.");
        }

        return hours;
    }

    private static string? TrimRemark(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > 500)
        {
            throw new DomainValidationException("REMARK_TOO_LONG", "Remarks must be 500 characters or fewer.");
        }

        return trimmed;
    }

    private async Task<Guid> EnsurePerformanceReportsEnabledAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var evaluation = await featureService.EvaluateAsync(tenantId, FeatureKey.PerformanceReports, cancellationToken);
        if (!evaluation.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.PerformanceReports);
        }

        if (!evaluation.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.PerformanceReports);
        }

        return tenantId;
    }

    private static string Sanitize(string value)
    {
        var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var text = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(text) ? "exam" : text;
    }
}
