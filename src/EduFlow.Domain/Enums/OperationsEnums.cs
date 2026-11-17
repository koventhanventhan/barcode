namespace EduFlow.Domain.Enums;

public enum BackupTrigger
{
    Manual = 1,
    Scheduled = 2
}

public enum BackupStatus
{
    Completed = 1,
    Failed = 2,
    Purged = 3
}

public enum AnnouncementSeverity
{
    Info = 1,
    Warning = 2,
    Critical = 3
}

public enum StaffAttendanceStatus
{
    Present = 1,
    Absent = 2,
    Late = 3,
    HalfDay = 4,
    OnLeave = 5
}

public enum StaffLeaveType
{
    Casual = 1,
    Medical = 2,
    Annual = 3,
    Unpaid = 4,
    Other = 5
}

public enum StaffLeaveStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4
}

public enum TimetableClashType
{
    Teacher = 1,
    Batch = 2,
    Room = 3
}
