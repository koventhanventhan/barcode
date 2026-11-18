using EduFlow.Application.Academics;
using EduFlow.Application.Fees;

namespace EduFlow.Application.Parents;

public sealed record CreateParentAccountRequest(
    string FullName,
    string Phone,
    IReadOnlyList<Guid> StudentIds,
    string? Email = null,
    string? Password = null,
    bool SendInvite = false);

public sealed record UpdateParentStudentsRequest(IReadOnlyList<Guid> StudentIds);

public sealed record ParentLinkedStudentDto(Guid Id, string FullName, string StudentCode);

public sealed record ParentAccountDto(
    Guid Id,
    string FullName,
    string Phone,
    string? Email,
    bool IsActive,
    bool InvitePending,
    IReadOnlyList<ParentLinkedStudentDto> Students);

public sealed record ParentChildDto(
    Guid Id,
    string FullName,
    string StudentCode,
    string? PhotoUrl,
    string LevelName,
    string BatchNames);

public sealed record ParentHomeDto(
    IReadOnlyList<ParentChildDto> Children,
    bool CanSeeMarks,
    bool CanPayOnline,
    bool ReportCardsAvailable);

public sealed record ParentAttendanceItemDto(
    Guid Id,
    DateOnly SessionDate,
    string ClassName,
    string SubjectName,
    string Status,
    DateTimeOffset? MarkedAt);

public sealed record ParentClassItemDto(
    Guid Id,
    string Name,
    string SubjectName,
    string TeacherName,
    string BatchName,
    int Weekday,
    int StartMinutes,
    int EndMinutes,
    string? Room,
    DateOnly NextDate);

public sealed record ParentExamSubjectDto(
    string Subject,
    decimal MaxMarks,
    decimal? Marks,
    bool IsAbsent,
    string? Grade,
    int? Rank);

public sealed record ParentExamItemDto(
    Guid Id,
    string Name,
    string Kind,
    string Status,
    DateTimeOffset? ScheduledAt,
    string? Room,
    decimal? Percentage,
    string? Grade,
    int? BatchRank,
    int? LevelRank,
    IReadOnlyList<ParentExamSubjectDto>? Subjects);

public sealed record ParentInvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string Title,
    string BillingPeriod,
    decimal NetAmount,
    decimal PaidAmount,
    decimal BalanceAmount,
    string Currency,
    DateOnly DueDate,
    string Status,
    decimal CreditAppliedAmount = 0m);

public sealed record ParentPaymentDto(
    Guid Id,
    string ReceiptNumber,
    string InvoiceNumber,
    decimal Amount,
    string Currency,
    string Method,
    string Status,
    DateTimeOffset PaidAt);

public sealed record ParentMessageDto(
    Guid Id,
    string MessageType,
    string Channel,
    string Status,
    string? Subject,
    string Body,
    string? StudentName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

public sealed record ParentDashboardDto(
    ParentChildDto Child,
    IReadOnlyList<ParentAttendanceItemDto> Today,
    int MonthPresent,
    int MonthMarked,
    IReadOnlyList<ParentClassItemDto> UpcomingClasses,
    IReadOnlyList<ParentExamItemDto> UpcomingExams,
    int OpenInvoiceCount,
    decimal OutstandingAmount,
    string Currency,
    ParentMessageDto? LatestMessage);

public sealed record ParentPayInvoiceRequest(decimal? Amount = null);

public interface IParentPortalService
{
    Task<IReadOnlyList<ParentAccountDto>> ListParentsAsync(CancellationToken cancellationToken = default);
    Task<ParentAccountDto> CreateParentAsync(CreateParentAccountRequest request, CancellationToken cancellationToken = default);
    Task<ParentAccountDto> UpdateStudentsAsync(Guid parentUserId, UpdateParentStudentsRequest request, CancellationToken cancellationToken = default);
    Task<ParentAccountDto> ResendInviteAsync(Guid parentUserId, CancellationToken cancellationToken = default);
    Task DeactivateParentAsync(Guid parentUserId, CancellationToken cancellationToken = default);
    Task<ParentHomeDto> GetHomeAsync(CancellationToken cancellationToken = default);
    Task<ParentDashboardDto> GetDashboardAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParentAttendanceItemDto>> GetAttendanceAsync(Guid studentId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParentClassItemDto>> GetClassesAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParentExamItemDto>> GetExamsAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<StoredDownload> GetReportCardAsync(Guid studentId, Guid examId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParentInvoiceDto>> GetInvoicesAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParentPaymentDto>> GetPaymentsAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<StoredDownload> GetPaymentReceiptAsync(Guid studentId, Guid paymentId, bool reprint = false, CancellationToken cancellationToken = default);
    Task<TenantBankAccountDto> GetBankAccountAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BankPaymentSubmissionDto>> GetBankSlipsAsync(Guid studentId, CancellationToken cancellationToken = default);
    Task<BankPaymentSubmissionDto> SubmitBankSlipAsync(Guid studentId, Guid invoiceId, decimal amount, string? referenceNumber, DateOnly? transferDate, string? notes, Stream content, string fileName, string? contentType, long length, CancellationToken cancellationToken = default);
    Task<StoredDownload> GetBankSlipFileAsync(Guid studentId, Guid submissionId, CancellationToken cancellationToken = default);
    Task<PayHereCheckoutSessionDto> PayInvoiceAsync(Guid studentId, Guid invoiceId, ParentPayInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ParentMessageDto>> GetMessagesAsync(CancellationToken cancellationToken = default);
}
