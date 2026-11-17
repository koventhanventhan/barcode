using EduFlow.Domain.Common;
using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Entities;

public sealed class StaffAttendanceRecord : TenantEntity
{
    public Guid StaffUserId { get; set; }
    public DateOnly Date { get; set; }
    public StaffAttendanceStatus Status { get; set; } = StaffAttendanceStatus.Present;
    public int? CheckInMinutes { get; set; }
    public int? CheckOutMinutes { get; set; }
    public string? Notes { get; set; }
}

public sealed class StaffLeaveRequest : TenantEntity
{
    public Guid StaffUserId { get; set; }
    public StaffLeaveType LeaveType { get; set; } = StaffLeaveType.Casual;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public StaffLeaveStatus Status { get; set; } = StaffLeaveStatus.Pending;
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNotes { get; set; }
}
