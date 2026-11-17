using EduFlow.Application.Academics;
using EduFlow.Domain.Enums;

namespace EduFlow.Application.Fees;

public sealed record FeeStructureDto(
    Guid Id,
    string Name,
    string Frequency,
    decimal Amount,
    string Currency,
    Guid? EducationLevelId,
    string? EducationLevelName,
    Guid? BatchId,
    string? BatchName,
    int DueDayOfMonth,
    string? Description,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record UpsertFeeStructureRequest(
    string Name,
    FeeFrequency Frequency,
    decimal Amount,
    Guid? EducationLevelId = null,
    Guid? BatchId = null,
    int DueDayOfMonth = 10,
    string? Description = null,
    bool IsActive = true,
    string Currency = "LKR");

public sealed record StudentConcessionDto(
    Guid Id,
    Guid StudentId,
    string StudentCode,
    string StudentName,
    Guid? FeeStructureId,
    string? FeeStructureName,
    string Name,
    string DiscountType,
    decimal Value,
    string? Reason,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    DateTimeOffset CreatedAt);

public sealed record UpsertStudentConcessionRequest(
    Guid StudentId,
    string Name,
    DiscountType DiscountType,
    decimal Value,
    Guid? FeeStructureId = null,
    string? Reason = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    bool IsActive = true);

public sealed record GenerateMonthlyInvoicesRequest(
    string? BillingPeriod = null,
    Guid? BatchId = null,
    Guid? EducationLevelId = null,
    Guid? FeeStructureId = null,
    DateOnly? DueDateOverride = null);

public sealed record CreateSingleInvoiceRequest(
    Guid StudentId,
    Guid FeeStructureId,
    string? BillingPeriod = null,
    DateOnly? DueDate = null,
    string? Notes = null);

public sealed record GenerateInvoicesResultDto(
    string BillingPeriod,
    int CreatedCount,
    int SkippedExistingCount,
    decimal TotalNetAmount,
    IReadOnlyList<FeeInvoiceDto> CreatedInvoices);

public sealed record FeeInvoiceListQuery(
    string? Search = null,
    string? Status = null,
    Guid? BatchId = null,
    Guid? StudentId = null,
    string? BillingPeriod = null,
    bool? OverdueOnly = null,
    int Page = 1,
    int PageSize = 50);

public sealed record FeeInvoicePageDto(
    IReadOnlyList<FeeInvoiceDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record FeePaymentDto(
    Guid Id,
    Guid InvoiceId,
    string InvoiceNumber,
    Guid StudentId,
    string StudentCode,
    string StudentName,
    int ReceiptSequence,
    string ReceiptNumber,
    decimal Amount,
    decimal RefundedAmount,
    decimal EffectiveAmount,
    string Currency,
    string Method,
    string Status,
    string? ReferenceNumber,
    string? AttachmentUrl,
    DateTimeOffset PaidAt,
    string? Notes,
    DateTimeOffset? VoidedOrRefundedAt,
    string? VoidOrRefundReason,
    DateTimeOffset CreatedAt);

public sealed record FeeInvoiceDto(
    Guid Id,
    string InvoiceNumber,
    Guid StudentId,
    string StudentCode,
    string StudentName,
    string GuardianName,
    string GuardianPhone,
    Guid FeeStructureId,
    string FeeStructureName,
    string Frequency,
    Guid? BatchId,
    string? BatchName,
    Guid? EducationLevelId,
    string? EducationLevelName,
    string BillingPeriod,
    string Title,
    decimal GrossAmount,
    decimal DiscountAmount,
    Guid? ConcessionId,
    string? ConcessionDescription,
    decimal NetAmount,
    decimal PaidAmount,
    decimal BalanceAmount,
    string Currency,
    DateOnly IssueDate,
    DateOnly DueDate,
    string Status,
    bool IsOverdue,
    int DaysOverdue,
    DateTimeOffset? PaidAt,
    DateTimeOffset? VoidedAt,
    string? VoidReason,
    string? Notes,
    IReadOnlyList<FeePaymentDto> Payments,
    DateTimeOffset CreatedAt);

public sealed record VoidInvoiceRequest(string Reason);

public sealed record RecordManualPaymentRequest(
    Guid InvoiceId,
    decimal Amount,
    PaymentMethod Method = PaymentMethod.Cash,
    string? ReferenceNumber = null,
    DateTimeOffset? PaidAt = null,
    string? Notes = null);

public sealed record RefundOrVoidPaymentRequest(
    string Reason,
    decimal? RefundAmount = null);

public sealed record RunFeeRemindersRequest(
    DateOnly? DateOverride = null);

public sealed record RunFeeRemindersResultDto(
    DateOnly EvaluatedDate,
    int CheckedInvoices,
    int BeforeDueSent,
    int OnDueSent,
    int OverdueSent,
    int SkippedCount);

public sealed record PayHereGatewaySettingsDto(
    string MerchantId,
    bool HasMerchantSecret,
    string? MaskedMerchantSecret,
    bool IsSandbox,
    bool IsEnabled,
    string? NotifyUrlOverride,
    string? ReturnUrlOverride,
    string? CancelUrlOverride,
    bool IsOnlinePaymentsFeatureEnabled,
    bool IsOnlinePaymentsInPlan);

public sealed record UpdatePayHereGatewaySettingsRequest(
    string MerchantId,
    string? MerchantSecret = null,
    bool IsSandbox = true,
    bool IsEnabled = true,
    string? NotifyUrlOverride = null,
    string? ReturnUrlOverride = null,
    string? CancelUrlOverride = null);

public sealed record InitiatePayHereCheckoutRequest(
    Guid InvoiceId,
    decimal? Amount = null,
    string? ReturnUrl = null,
    string? CancelUrl = null);

public sealed record PayHereCheckoutSessionDto(
    Guid SessionId,
    string OrderId,
    Guid InvoiceId,
    string InvoiceNumber,
    string InvoiceTitle,
    Guid StudentId,
    string StudentName,
    string StudentCode,
    decimal Amount,
    decimal InvoiceNetAmount,
    decimal InvoiceBalanceAmount,
    string Currency,
    bool IsSandbox,
    string CheckoutUrl,
    string Status,
    string? PayHerePaymentId,
    Guid? FeePaymentId,
    string? ReceiptNumber,
    IReadOnlyDictionary<string, string> FormFields,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt);

public sealed record PayHereWebhookPayload(
    string MerchantId,
    string OrderId,
    string? PaymentId,
    string PayHereAmount,
    string PayHereCurrency,
    int StatusCode,
    string Md5Sig,
    string? Method = null,
    string? StatusMessage = null,
    string? Custom1 = null,
    string? Custom2 = null);

public sealed record PayHereWebhookResultDto(
    string OrderId,
    string Outcome,
    bool WasReplay,
    string Status,
    Guid? InvoiceId,
    string? InvoiceStatus,
    Guid? PaymentId,
    string? ReceiptNumber);

public sealed record SimulatePayHereSandboxRequest(
    string OrderId,
    int StatusCode = 2,
    string? PaymentId = null);

public sealed record FeeDashboardQuery(
    string? BillingPeriod = null,
    Guid? BatchId = null,
    DateOnly? AsOfDate = null);

public sealed record OverdueAgingBucketDto(
    string Bucket,
    int InvoiceCount,
    decimal BalanceAmount);

public sealed record BatchFeeSummaryDto(
    Guid? BatchId,
    string BatchName,
    int InvoiceCount,
    decimal ExpectedAmount,
    decimal CollectedAmount,
    decimal OutstandingAmount,
    decimal CollectionRatePercent);

public sealed record DefaulterStudentDto(
    Guid StudentId,
    string StudentCode,
    string StudentName,
    string GuardianName,
    string GuardianPhone,
    string BatchNames,
    int OverdueInvoiceCount,
    decimal TotalOverdueBalance,
    DateOnly OldestDueDate,
    int MaxDaysOverdue);

public sealed record FeeDashboardDto(
    decimal ExpectedTotal,
    decimal CollectedTotal,
    decimal OutstandingTotal,
    decimal OverdueTotal,
    decimal CollectionRatePercent,
    int PaidInvoiceCount,
    int PartiallyPaidInvoiceCount,
    int PendingInvoiceCount,
    int OverdueInvoiceCount,
    IReadOnlyList<OverdueAgingBucketDto> OverdueAging,
    IReadOnlyList<BatchFeeSummaryDto> ByBatch,
    IReadOnlyList<DefaulterStudentDto> TopDefaulters,
    IReadOnlyList<FeePaymentDto> RecentPayments);

public sealed record PlatformSubscriptionInvoiceDto(
    Guid Id,
    Guid TenantId,
    string TenantName,
    string TenantSubdomain,
    string InvoiceNumber,
    Guid PlanId,
    string PlanName,
    decimal Amount,
    string Currency,
    int PeriodMonths,
    int DaysToExtend,
    DateOnly IssueDate,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? PaidAt,
    string? PaymentReference,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record CreatePlatformInvoiceRequest(
    decimal Amount,
    int PeriodMonths = 1,
    int DaysToExtend = 30,
    DateOnly? DueDate = null,
    string? Notes = null,
    string Currency = "LKR");

public sealed record MarkPlatformInvoicePaidRequest(
    string? PaymentReference = null,
    string? Notes = null);

public sealed record ReceiptPdfContent(
    string InstituteName,
    string? InstituteAddress,
    string? InstitutePhone,
    string PrimaryHex,
    byte[]? Logo,
    string ReceiptNumber,
    string InvoiceNumber,
    string Title,
    string BillingPeriod,
    string StudentName,
    string StudentCode,
    string GuardianName,
    string? BatchName,
    decimal InvoiceNetAmount,
    decimal AmountPaid,
    decimal InvoiceBalanceAfter,
    string Currency,
    string PaymentMethod,
    string? ReferenceNumber,
    DateTimeOffset PaidAt,
    string Status,
    string? Notes);

public sealed record PlatformInvoicePdfContent(
    string PlatformName,
    string PrimaryHex,
    string InvoiceNumber,
    string TenantName,
    string TenantSubdomain,
    string? TenantEmail,
    string PlanName,
    int PeriodMonths,
    int DaysToExtend,
    decimal Amount,
    string Currency,
    DateOnly IssueDate,
    DateOnly DueDate,
    string Status,
    DateTimeOffset? PaidAt,
    string? PaymentReference,
    string? Notes);

public sealed record FeeReportExportRow(
    string InvoiceNumber,
    string StudentCode,
    string StudentName,
    string BatchName,
    string BillingPeriod,
    decimal NetAmount,
    decimal PaidAmount,
    decimal BalanceAmount,
    DateOnly DueDate,
    string Status);

public interface IFeeDocumentService
{
    byte[] BuildReceiptPdf(ReceiptPdfContent content);
    byte[] BuildPlatformInvoicePdf(PlatformInvoicePdfContent content);
    byte[] BuildFeeReportPdf(string instituteName, string primaryHex, IReadOnlyList<FeeReportExportRow> rows);
}

public interface IFeeService
{
    Task<IReadOnlyList<FeeStructureDto>> GetFeeStructuresAsync(CancellationToken cancellationToken = default);
    Task<FeeStructureDto> CreateFeeStructureAsync(UpsertFeeStructureRequest request, CancellationToken cancellationToken = default);
    Task<FeeStructureDto> UpdateFeeStructureAsync(Guid id, UpsertFeeStructureRequest request, CancellationToken cancellationToken = default);
    Task DeleteFeeStructureAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudentConcessionDto>> GetConcessionsAsync(Guid? studentId = null, CancellationToken cancellationToken = default);
    Task<StudentConcessionDto> CreateConcessionAsync(UpsertStudentConcessionRequest request, CancellationToken cancellationToken = default);
    Task<StudentConcessionDto> UpdateConcessionAsync(Guid id, UpsertStudentConcessionRequest request, CancellationToken cancellationToken = default);
    Task DeleteConcessionAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GenerateInvoicesResultDto> GenerateMonthlyInvoicesAsync(GenerateMonthlyInvoicesRequest request, Guid? tenantIdOverride = null, CancellationToken cancellationToken = default);
    Task<FeeInvoiceDto> CreateSingleInvoiceAsync(CreateSingleInvoiceRequest request, CancellationToken cancellationToken = default);
    Task<FeeInvoicePageDto> GetInvoicesAsync(FeeInvoiceListQuery query, CancellationToken cancellationToken = default);
    Task<FeeInvoiceDto> GetInvoiceAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FeeInvoiceDto> VoidInvoiceAsync(Guid id, VoidInvoiceRequest request, CancellationToken cancellationToken = default);

    Task<FeePaymentDto> RecordManualPaymentAsync(RecordManualPaymentRequest request, CancellationToken cancellationToken = default);
    Task<FeePaymentDto> UploadPaymentAttachmentAsync(Guid paymentId, Stream content, string fileName, string? contentType, long length, CancellationToken cancellationToken = default);
    Task<FeePaymentDto> RefundOrVoidPaymentAsync(Guid paymentId, RefundOrVoidPaymentRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FeePaymentDto>> GetPaymentsAsync(Guid? invoiceId = null, Guid? studentId = null, int take = 100, CancellationToken cancellationToken = default);
    Task<StoredDownload> GetReceiptPdfAsync(Guid paymentId, CancellationToken cancellationToken = default);

    Task<RunFeeRemindersResultDto> ProcessFeeRemindersAsync(DateOnly? dateOverride = null, Guid? tenantIdOverride = null, CancellationToken cancellationToken = default);

    Task<PayHereGatewaySettingsDto> GetPayHereSettingsAsync(CancellationToken cancellationToken = default);
    Task<PayHereGatewaySettingsDto> UpdatePayHereSettingsAsync(UpdatePayHereGatewaySettingsRequest request, CancellationToken cancellationToken = default);
    Task<PayHereCheckoutSessionDto> InitiatePayHereCheckoutAsync(InitiatePayHereCheckoutRequest request, CancellationToken cancellationToken = default);
    Task<PayHereCheckoutSessionDto> GetPayHereOrderStatusAsync(string orderId, CancellationToken cancellationToken = default);
    Task<StoredDownload> GetPayHereOrderReceiptPdfAsync(string orderId, CancellationToken cancellationToken = default);
    Task<PayHereWebhookResultDto> ProcessPayHereWebhookAsync(PayHereWebhookPayload payload, CancellationToken cancellationToken = default);
    Task<PayHereWebhookResultDto> SimulatePayHereSandboxAsync(SimulatePayHereSandboxRequest request, CancellationToken cancellationToken = default);

    Task<FeeDashboardDto> GetDashboardAsync(FeeDashboardQuery query, CancellationToken cancellationToken = default);
    Task<StoredDownload> ExportFeeReportAsync(string? format, FeeInvoiceListQuery query, CancellationToken cancellationToken = default);
}
