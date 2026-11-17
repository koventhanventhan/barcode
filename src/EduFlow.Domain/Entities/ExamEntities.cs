using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class GradingScheme : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public sealed class GradeBoundary : TenantEntity
{
    public Guid GradingSchemeId { get; set; }
    public string Grade { get; set; } = string.Empty;
    public decimal MinPercent { get; set; }
    public decimal MaxPercent { get; set; }
    public decimal? GradePoint { get; set; }
    public int SortOrder { get; set; }
}

public sealed class Exam : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public ExamKind Kind { get; set; } = ExamKind.Term;
    public Guid BatchId { get; set; }
    public Guid EducationLevelId { get; set; }
    public Guid GradingSchemeId { get; set; }
    public string SeriesKey { get; set; } = string.Empty;
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public DateTimeOffset? ScheduledAt { get; set; }
    public string? Room { get; set; }
    public int? ReminderDaysBefore { get; set; }
    public int? ReminderHoursBefore { get; set; }
    public bool IncludeMarksInNotification { get; set; } = true;
    public DateTimeOffset? ScheduledNotifiedAt { get; set; }
    public DateTimeOffset? ReminderSentAt { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public sealed class ExamSubject : TenantEntity
{
    public Guid ExamId { get; set; }
    public Guid SubjectId { get; set; }
    public decimal MaxMarks { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public string? Room { get; set; }
    public int SortOrder { get; set; }
}

public sealed class ExamMark : TenantEntity
{
    public Guid ExamId { get; set; }
    public Guid ExamSubjectId { get; set; }
    public Guid StudentId { get; set; }
    public decimal? Marks { get; set; }
    public bool IsAbsent { get; set; }
    public string? Grade { get; set; }
    public int? SubjectRank { get; set; }
}

public sealed class ExamResult : TenantEntity
{
    public Guid ExamId { get; set; }
    public Guid StudentId { get; set; }
    public decimal TotalMarks { get; set; }
    public decimal MaxMarks { get; set; }
    public decimal Percentage { get; set; }
    public string Grade { get; set; } = string.Empty;
    public int? BatchRank { get; set; }
    public int? LevelRank { get; set; }
    public decimal? AttendancePercent { get; set; }
    public string? TeacherRemarks { get; set; }
    public string? PrincipalRemarks { get; set; }
    public bool MarksReleasedNotified { get; set; }
}
