namespace EduFlow.Domain.Enums;

public enum StaffRegistrationStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3
}

public enum SupportTicketCategory
{
    General = 1,
    Technical = 2,
    Billing = 3,
    BackupRestore = 4,
    FeatureRequest = 5
}

public enum SupportTicketStatus
{
    Open = 1,
    InProgress = 2,
    WaitingOnInstitute = 3,
    Resolved = 4,
    Closed = 5
}
