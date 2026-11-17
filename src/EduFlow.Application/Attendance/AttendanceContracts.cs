using EduFlow.Domain.Enums;

namespace EduFlow.Application.Attendance;

public sealed record AttendancePolicyDto(int LateAfterMinutes, bool MarkAbsentOnStop);

public sealed record UpdateAttendancePolicyRequest(int LateAfterMinutes, bool MarkAbsentOnStop);

public sealed record OpenClassSessionRequest(Guid ClassScheduleId, DateOnly? SessionDate = null);

public sealed record CancelClassSessionRequest(string? Reason);

public sealed record ClassSessionDto(
    Guid Id,
    Guid ClassScheduleId,
    string ClassName,
    Guid BatchId,
    string BatchName,
    DateOnly SessionDate,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? StoppedAt,
    DateTimeOffset? CancelledAt,
    string? JoinCode,
    int EnrolledCount,
    int PresentCount,
    int LateCount,
    int AbsentCount);

public sealed record ScanAttendanceRequest(string Code, Guid? SessionId = null);

public sealed record ScanAttendanceResultDto(
    string Outcome,
    string Message,
    Guid? SessionId,
    Guid? StudentId,
    string? StudentName,
    string? StudentCode,
    string? Status,
    DateTimeOffset? MarkedAt,
    string? MarkedAtLabel);

public sealed record ManualAttendanceRequest(Guid StudentId, string Status, string? Reason);

public sealed record UndoAttendanceRequest(string Reason);

public sealed record JoinAttendanceRequest(string JoinCode, string StudentCode);

public sealed record AttendanceRegisterQuery(Guid? BatchId, DateOnly? Date);

public sealed record AttendanceRegisterStudentDto(
    Guid StudentId,
    string StudentCode,
    string FullName,
    string? Status,
    string? Source,
    DateTimeOffset? MarkedAt,
    string? Reason,
    bool IsUndone);

public sealed record AttendanceRegisterSessionDto(
    ClassSessionDto Session,
    IReadOnlyList<AttendanceRegisterStudentDto> Students);

public sealed record AttendanceReportQuery(DateOnly? From, DateOnly? To, Guid? BatchId);

public sealed record AttendanceDayPointDto(DateOnly Date, int Present, int Late, int Absent);

public sealed record AttendanceBatchPointDto(Guid BatchId, string BatchName, int Present, int Late, int Absent, decimal PresentRate);

public sealed record AttendanceReportDto(
    int Present,
    int Late,
    int Absent,
    decimal PresentRate,
    IReadOnlyList<AttendanceDayPointDto> ByDay,
    IReadOnlyList<AttendanceBatchPointDto> ByBatch);

public sealed record StudentAttendanceItemDto(
    Guid SessionId,
    DateOnly SessionDate,
    string ClassName,
    string Status,
    DateTimeOffset MarkedAt);

public sealed record AttendanceBoardScanDto(
    Guid StudentId,
    string StudentName,
    string StudentCode,
    string Status,
    DateTimeOffset MarkedAt,
    string MarkedAtLabel);

public sealed record AttendanceBoardDto(
    Guid SessionId,
    int EnrolledCount,
    int PresentCount,
    int LateCount,
    int AbsentCount,
    IReadOnlyList<AttendanceBoardScanDto> LastScans);

public interface IAttendanceBoard
{
    Task PublishAsync(Guid tenantId, AttendanceBoardDto board, CancellationToken cancellationToken = default);
}

public interface IAttendanceService
{
    Task<AttendancePolicyDto> GetPolicyAsync(CancellationToken cancellationToken = default);
    Task<AttendancePolicyDto> UpdatePolicyAsync(UpdateAttendancePolicyRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ClassSessionDto>> ListSessionsAsync(DateOnly? date, CancellationToken cancellationToken = default);
    Task<ClassSessionDto> OpenSessionAsync(OpenClassSessionRequest request, CancellationToken cancellationToken = default);
    Task<ClassSessionDto> StartSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<ClassSessionDto> StopSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<ClassSessionDto> CancelSessionAsync(Guid sessionId, CancelClassSessionRequest request, CancellationToken cancellationToken = default);
    Task<ScanAttendanceResultDto> ScanAsync(ScanAttendanceRequest request, CancellationToken cancellationToken = default);
    Task<ScanAttendanceResultDto> JoinOnlineAsync(JoinAttendanceRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceRegisterStudentDto> MarkManualAsync(Guid sessionId, ManualAttendanceRequest request, CancellationToken cancellationToken = default);
    Task<AttendanceRegisterStudentDto> UndoAsync(Guid sessionId, Guid studentId, UndoAttendanceRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AttendanceRegisterSessionDto>> GetRegisterAsync(AttendanceRegisterQuery query, CancellationToken cancellationToken = default);
    Task<AttendanceReportDto> GetReportAsync(AttendanceReportQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentAttendanceItemDto>> GetStudentHistoryAsync(Guid studentId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<AttendanceBoardDto> GetBoardAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
