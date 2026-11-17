namespace EduFlow.Domain.Enums;

public enum FeeFrequency
{
    Monthly = 1,
    OneTime = 2,
    Admission = 3
}

public enum DiscountType
{
    Percentage = 1,
    FixedAmount = 2
}

public enum FeeInvoiceStatus
{
    Pending = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Voided = 4
}

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    PayHere = 3
}

public enum PaymentRecordStatus
{
    Completed = 1,
    Refunded = 2,
    Voided = 3
}

public enum PayHereCheckoutStatus
{
    Initiated = 1,
    Success = 2,
    Pending = 3,
    Cancelled = 4,
    Failed = 5,
    ChargedBack = 6
}

public enum PlatformInvoiceStatus
{
    Pending = 1,
    Paid = 2,
    Voided = 3
}

public enum FeeReminderStage
{
    BeforeDue = 1,
    OnDueDate = 2,
    Overdue = 3
}
