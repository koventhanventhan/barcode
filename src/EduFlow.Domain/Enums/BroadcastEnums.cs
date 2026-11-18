namespace EduFlow.Domain.Enums;

public enum BroadcastAudienceKind
{
    All = 1,
    Level = 2,
    Batch = 3,
    BatchGroup = 4,
    SelectedStudents = 5,
    FeeOverdue = 6
}

public enum BroadcastCampaignStatus
{
    Scheduled = 1,
    Queued = 2
}
