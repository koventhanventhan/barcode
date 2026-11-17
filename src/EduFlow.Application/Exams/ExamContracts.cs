using EduFlow.Application.Academics;
using EduFlow.Domain.Enums;

namespace EduFlow.Application.Exams;

public sealed record GradeBoundaryDto(
    Guid Id,
    string Grade,
    decimal MinPercent,
    decimal MaxPercent,
    decimal? GradePoint,
    int SortOrder);

public sealed record GradingSchemeDto(
    Guid Id,
    string Name,
    bool IsDefault,
    IReadOnlyList<GradeBoundaryDto> Boundaries);

public sealed record UpsertGradeBoundaryRequest(
    string Grade,
    decimal MinPercent,
    decimal MaxPercent,
    decimal? GradePoint,
    int SortOrder);

public sealed record UpsertGradingSchemeRequest(
    string Name,
    bool IsDefault,
    IReadOnlyList<UpsertGradeBoundaryRequest> Boundaries);

public sealed record ExamSubjectDto(
    Guid Id,
    Guid SubjectId,
    string SubjectName,
    string SubjectCode,
    decimal MaxMarks,
    DateTimeOffset? ScheduledAt,
    string? Room,
    int SortOrder);

public sealed record ExamListItemDto(
    Guid Id,
    string Name,
    string Kind,
    string Status,
    Guid BatchId,
    string BatchName,
    Guid EducationLevelId,
    string SeriesKey,
    DateTimeOffset? ScheduledAt,
    string? Room,
    int SubjectCount,
    bool IncludeMarksInNotification,
    DateTimeOffset? PublishedAt);

public sealed record ExamDetailDto(
    Guid Id,
    string Name,
    string Kind,
    string Status,
    Guid BatchId,
    string BatchName,
    Guid EducationLevelId,
    string EducationLevelName,
    Guid GradingSchemeId,
    string GradingSchemeName,
    string SeriesKey,
    DateTimeOffset? ScheduledAt,
    string? Room,
    int? ReminderDaysBefore,
    int? ReminderHoursBefore,
    bool IncludeMarksInNotification,
    DateTimeOffset? ScheduledNotifiedAt,
    DateTimeOffset? ReminderSentAt,
    DateTimeOffset? LockedAt,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ExamSubjectDto> Subjects);

public sealed record UpsertExamSubjectRequest(
    Guid? Id,
    Guid SubjectId,
    decimal MaxMarks,
    DateTimeOffset? ScheduledAt,
    string? Room,
    int SortOrder);

public sealed record UpsertExamRequest(
    string Name,
    ExamKind Kind,
    Guid BatchId,
    Guid? GradingSchemeId,
    string? SeriesKey,
    bool IncludeMarksInNotification,
    int? ReminderDaysBefore,
    int? ReminderHoursBefore,
    IReadOnlyList<UpsertExamSubjectRequest> Subjects);

public sealed record ScheduleExamRequest(DateTimeOffset ScheduledAt, string? Room);

public sealed record MarkEntryRequest(Guid StudentId, Guid ExamSubjectId, decimal? Marks, bool IsAbsent);

public sealed record StudentRemarkRequest(Guid StudentId, string? TeacherRemarks, string? PrincipalRemarks);

public sealed record SaveMarksRequest(
    IReadOnlyList<MarkEntryRequest> Entries,
    IReadOnlyList<StudentRemarkRequest>? Remarks = null);

public sealed record MarksStudentDto(Guid Id, string FullName, string StudentCode);

public sealed record MarksSubjectDto(Guid Id, Guid SubjectId, string Name, decimal MaxMarks);

public sealed record MarkCellDto(
    Guid StudentId,
    Guid ExamSubjectId,
    decimal? Marks,
    bool IsAbsent,
    string? Grade,
    int? SubjectRank);

public sealed record StudentRemarkDto(Guid StudentId, string? TeacherRemarks, string? PrincipalRemarks);

public sealed record MarksGridDto(
    Guid ExamId,
    string Status,
    bool CanEdit,
    IReadOnlyList<MarksStudentDto> Students,
    IReadOnlyList<MarksSubjectDto> Subjects,
    IReadOnlyList<MarkCellDto> Cells,
    IReadOnlyList<StudentRemarkDto> Remarks);

public sealed record RankRowDto(
    Guid StudentId,
    string StudentName,
    string StudentCode,
    string BatchName,
    decimal Score,
    decimal MaxScore,
    decimal Percentage,
    string? Grade,
    int? Rank,
    bool IsAbsent);

public sealed record RankListDto(string Scope, string? SubjectName, IReadOnlyList<RankRowDto> Rows);

public sealed record GradeDistributionDto(string Grade, int Count, decimal PercentOfCohort);

public sealed record StudentTrendPointDto(
    Guid ExamId,
    string ExamName,
    string Kind,
    DateTimeOffset? ScheduledAt,
    decimal Percentage,
    string Grade,
    int? BatchRank,
    int? LevelRank);

public sealed record ExamSummaryDto(
    int UpcomingCount,
    int PublishedCount,
    IReadOnlyList<ExamListItemDto> Upcoming);

public sealed record PublishExamResultDto(int StudentsRanked, int MessagesEnqueued, int MessagesSkipped);

public sealed record RunExamRemindersRequest(DateTimeOffset? NowUtc);

public sealed record ExamReminderRunDto(int ExamsChecked, int ExamsReminded, int MessagesEnqueued, int MessagesSkipped);

public sealed record ReportCardSubjectLine(
    string SubjectName,
    decimal MaxMarks,
    decimal? Marks,
    bool IsAbsent,
    string? Grade,
    int? Rank);

public sealed record ReportCardPdfContent(
    string InstituteName,
    string? InstituteAddress,
    string? InstitutePhone,
    string PrimaryHex,
    byte[]? Logo,
    bool ShowProductName,
    byte[]? Photo,
    string StudentName,
    string StudentCode,
    string BatchName,
    string LevelName,
    string ExamName,
    string ExamKind,
    string? ExamWhen,
    string? Room,
    IReadOnlyList<ReportCardSubjectLine> Subjects,
    decimal TotalMarks,
    decimal MaxMarks,
    decimal Percentage,
    string Grade,
    int? BatchRank,
    int? LevelRank,
    decimal? AttendancePercent,
    string? TeacherRemarks,
    string? PrincipalRemarks);

public interface IExamDocumentService
{
    byte[] BuildReportCards(IReadOnlyList<ReportCardPdfContent> cards);
}

public interface IExamService
{
    Task<IReadOnlyList<GradingSchemeDto>> GetGradingSchemesAsync(CancellationToken cancellationToken = default);
    Task<GradingSchemeDto> CreateGradingSchemeAsync(UpsertGradingSchemeRequest request, CancellationToken cancellationToken = default);
    Task<GradingSchemeDto> UpdateGradingSchemeAsync(Guid id, UpsertGradingSchemeRequest request, CancellationToken cancellationToken = default);
    Task DeleteGradingSchemeAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExamListItemDto>> GetExamsAsync(Guid? batchId, ExamStatus? status, CancellationToken cancellationToken = default);
    Task<ExamSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<ExamDetailDto> GetExamAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ExamDetailDto> CreateExamAsync(UpsertExamRequest request, CancellationToken cancellationToken = default);
    Task<ExamDetailDto> UpdateExamAsync(Guid id, UpsertExamRequest request, CancellationToken cancellationToken = default);
    Task DeleteExamAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ExamDetailDto> ScheduleExamAsync(Guid id, ScheduleExamRequest request, CancellationToken cancellationToken = default);
    Task<ExamDetailDto> LockExamAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ExamDetailDto> UnlockExamAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PublishExamResultDto> PublishExamAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ExamDetailDto> UnpublishExamAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MarksGridDto> GetMarksAsync(Guid examId, CancellationToken cancellationToken = default);
    Task<MarksGridDto> SaveMarksAsync(Guid examId, SaveMarksRequest request, CancellationToken cancellationToken = default);
    Task<RankListDto> GetRanksAsync(Guid examId, string scope, Guid? subjectId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GradeDistributionDto>> GetDistributionAsync(Guid examId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentTrendPointDto>> GetStudentTrendsAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<StoredDownload> GetReportCardsPdfAsync(Guid examId, Guid? studentId, CancellationToken cancellationToken = default);

    Task<ExamReminderRunDto> ProcessExamRemindersAsync(
        DateTimeOffset? nowOverride = null,
        Guid? tenantIdOverride = null,
        CancellationToken cancellationToken = default);
}
