namespace EduFlow.Domain.Enums;

public enum ClassSessionStatus
{
    Scheduled = 1,
    Started = 2,
    Stopped = 3,
    Cancelled = 4
}

public enum AttendanceMarkStatus
{
    Present = 1,
    Late = 2,
    Absent = 3
}

public enum AttendanceMarkSource
{
    Scan = 1,
    Manual = 2,
    Online = 3,
    AutoAbsent = 4
}
