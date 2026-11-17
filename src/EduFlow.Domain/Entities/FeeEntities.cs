using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class FeeStructure : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public FeeFrequency Frequency { get; set; } = FeeFrequency.Monthly;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "LKR";
    public Guid? EducationLevelId { get; set; }
    public Guid? BatchId { get; set; }
    public int DueDayOfMonth { get; set; } = 10;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StudentConcession : TenantEntity
{
    public Guid StudentId { get; set; }
    public Guid? FeeStructureId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;
    public decimal Value { get; set; }
    public string? Reason { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class FeeInvoice : TenantEntity, IBranchOwned
{
    public Guid? BranchId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid StudentId { get; set; }
    public Guid FeeStructureId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? EducationLevelId { get; set; }
    public string BillingPeriod { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public Guid? ConcessionId { get; set; }
    public string? ConcessionDescription { get; set; }
    public decimal NetAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
    public string Currency { get; set; } = "LKR";
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public FeeInvoiceStatus Status { get; set; } = FeeInvoiceStatus.Pending;
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? BeforeDueReminderSentAt { get; set; }
    public DateTimeOffset? OnDueReminderSentAt { get; set; }
    public DateTimeOffset? OverdueReminderSentAt { get; set; }
}

public sealed class FeePayment : TenantEntity, IBranchOwned
{
    public Guid? BranchId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid StudentId { get; set; }
    public int ReceiptSequence { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal RefundedAmount { get; set; }
    public string Currency { get; set; } = "LKR";
    public PaymentMethod Method { get; set; } = PaymentMethod.Cash;
    public PaymentRecordStatus Status { get; set; } = PaymentRecordStatus.Completed;
    public string? ReferenceNumber { get; set; }
    public string? AttachmentUrl { get; set; }
    public long AttachmentBytes { get; set; }
    public DateTimeOffset PaidAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? RecordedByUserId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? VoidedOrRefundedAt { get; set; }
    public string? VoidOrRefundReason { get; set; }
    public Guid? VoidedOrRefundedByUserId { get; set; }
    public bool ParentNotified { get; set; }
    public bool AdminNotified { get; set; }
}

public sealed class TenantPaymentGatewayConfig : TenantEntity
{
    public string MerchantId { get; set; } = string.Empty;
    public string EncryptedMerchantSecret { get; set; } = string.Empty;
    public bool IsSandbox { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public string? NotifyUrlOverride { get; set; }
    public string? ReturnUrlOverride { get; set; }
    public string? CancelUrlOverride { get; set; }
}

public sealed class PayHereCheckoutSession : TenantEntity
{
    public string OrderId { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    public Guid StudentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "LKR";
    public bool IsSandbox { get; set; } = true;
    public PayHereCheckoutStatus Status { get; set; } = PayHereCheckoutStatus.Initiated;
    public string? PayHerePaymentId { get; set; }
    public int? StatusCode { get; set; }
    public string? StatusMessage { get; set; }
    public Guid? FeePaymentId { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? LastWebhookAt { get; set; }
}

public sealed class PlatformSubscriptionInvoice : TenantEntity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid? SubscriptionId { get; set; }
    public Guid PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "LKR";
    public int PeriodMonths { get; set; } = 1;
    public int DaysToExtend { get; set; } = 30;
    public DateOnly IssueDate { get; set; }
    public DateOnly DueDate { get; set; }
    public PlatformInvoiceStatus Status { get; set; } = PlatformInvoiceStatus.Pending;
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentReference { get; set; }
    public string? Notes { get; set; }
}
