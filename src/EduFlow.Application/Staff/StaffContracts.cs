using EduFlow.Domain.Enums;

namespace EduFlow.Application.Staff;

public sealed record StaffRoleTemplateDto(
    string Key,
    string Name,
    string RoleType,
    IReadOnlyList<string> Permissions);

public sealed record CreateStaffRequest(
    string Email,
    string FullName,
    string Password,
    string? TemplateKey,
    string? Phone = null,
    Guid? BranchId = null,
    string? RoleType = null,
    IReadOnlyList<string>? Permissions = null,
    string? RoleName = null);

public sealed record SetUserActiveRequest(bool IsActive);

public sealed record StaffAttendanceEntryDto(
    Guid StaffUserId,
    string FullName,
    string Email,
    string RoleName,
    string Status,
    int? CheckInMinutes,
    int? CheckOutMinutes,
    string? Notes,
    bool HasApprovedLeave);

public sealed record StaffAttendanceSheetDto(
    DateOnly Date,
    int PresentCount,
    int AbsentCount,
    int LateCount,
    int HalfDayCount,
    int OnLeaveCount,
    IReadOnlyList<StaffAttendanceEntryDto> Entries);

public sealed record StaffAttendanceEntryInput(
    Guid StaffUserId,
    StaffAttendanceStatus Status,
    int? CheckInMinutes = null,
    int? CheckOutMinutes = null,
    string? Notes = null);

public sealed record SaveStaffAttendanceSheetRequest(
    DateOnly Date,
    IReadOnlyList<StaffAttendanceEntryInput> Entries);

public sealed record StaffLeaveRequestDto(
    Guid Id,
    Guid StaffUserId,
    string StaffFullName,
    string StaffEmail,
    string LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    int DaysCount,
    string Reason,
    string Status,
    Guid? ReviewedByUserId,
    DateTimeOffset? ReviewedAt,
    string? ReviewNotes,
    DateTimeOffset CreatedAt);

public sealed record CreateStaffLeaveRequest(
    StaffLeaveType LeaveType,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason,
    Guid? StaffUserId = null);

public sealed record ReviewStaffLeaveRequest(
    bool Approved,
    string? ReviewNotes = null);

