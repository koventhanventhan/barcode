using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Staff;

public interface IStaffOperationsService
{
    Task<StaffAttendanceSheetDto> GetAttendanceSheetAsync(DateOnly? date = null, CancellationToken cancellationToken = default);
    Task<StaffAttendanceSheetDto> SaveAttendanceSheetAsync(SaveStaffAttendanceSheetRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StaffLeaveRequestDto>> GetLeaveRequestsAsync(StaffLeaveStatus? status = null, Guid? staffUserId = null, CancellationToken cancellationToken = default);
    Task<StaffLeaveRequestDto> CreateLeaveRequestAsync(CreateStaffLeaveRequest request, CancellationToken cancellationToken = default);
    Task<StaffLeaveRequestDto> ReviewLeaveRequestAsync(Guid leaveId, ReviewStaffLeaveRequest request, CancellationToken cancellationToken = default);
}

public sealed class StaffOperationsService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    IAuditService auditService) : IStaffOperationsService
{
    public async Task<StaffAttendanceSheetDto> GetAttendanceSheetAsync(
        DateOnly? date = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureStaffLeaveEnabledAsync(cancellationToken);
        var targetDate = date ?? ColomboClock.Today();

        var staffUsers = await db.Users
            .AsNoTracking()
            .Include(u => u.CustomRole)
            .Where(u => u.IsActive && u.RoleType != RoleType.Parent && u.RoleType != RoleType.SuperAdmin)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

        var records = await db.StaffAttendanceRecords
            .AsNoTracking()
            .Where(r => r.Date == targetDate)
            .ToDictionaryAsync(r => r.StaffUserId, cancellationToken);

        var approvedLeaveUserIds = (await db.StaffLeaveRequests
            .AsNoTracking()
            .Where(l => l.Status == StaffLeaveStatus.Approved && l.StartDate <= targetDate && l.EndDate >= targetDate)
            .Select(l => l.StaffUserId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var entries = staffUsers.Select(u =>
        {
            var hasLeave = approvedLeaveUserIds.Contains(u.Id);
            var status = records.TryGetValue(u.Id, out var rec)
                ? rec.Status
                : (hasLeave ? StaffAttendanceStatus.OnLeave : StaffAttendanceStatus.Present);

            return new StaffAttendanceEntryDto(
                u.Id,
                u.FullName,
                u.Email,
                u.CustomRole?.Name ?? u.RoleType.ToString(),
                status.ToString(),
                rec?.CheckInMinutes,
                rec?.CheckOutMinutes,
                rec?.Notes,
                hasLeave);
        }).ToList();

        return new StaffAttendanceSheetDto(
            targetDate,
            entries.Count(e => e.Status == nameof(StaffAttendanceStatus.Present)),
            entries.Count(e => e.Status == nameof(StaffAttendanceStatus.Absent)),
            entries.Count(e => e.Status == nameof(StaffAttendanceStatus.Late)),
            entries.Count(e => e.Status == nameof(StaffAttendanceStatus.HalfDay)),
            entries.Count(e => e.Status == nameof(StaffAttendanceStatus.OnLeave)),
            entries);
    }

    public async Task<StaffAttendanceSheetDto> SaveAttendanceSheetAsync(
        SaveStaffAttendanceSheetRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureStaffLeaveEnabledAsync(cancellationToken);
        if (request.Entries is null || request.Entries.Count == 0)
        {
            throw new DomainValidationException("NO_ENTRIES", "Provide at least one staff attendance entry.");
        }

        var userIds = request.Entries.Select(e => e.StaffUserId).Distinct().ToList();
        var validUserIds = (await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.RoleType != RoleType.Parent)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        if (validUserIds.Count != userIds.Count)
        {
            throw new EntityNotFoundException(nameof(User));
        }

        var existing = await db.StaffAttendanceRecords
            .Where(r => r.Date == request.Date && userIds.Contains(r.StaffUserId))
            .ToDictionaryAsync(r => r.StaffUserId, cancellationToken);

        foreach (var entry in request.Entries)
        {
            if (entry.CheckInMinutes is < 0 or > 1440 || entry.CheckOutMinutes is < 0 or > 1440)
            {
                throw new DomainValidationException("INVALID_TIME", "Check-in and check-out times must be between 00:00 and 24:00.");
            }

            if (entry.CheckInMinutes.HasValue && entry.CheckOutMinutes.HasValue && entry.CheckOutMinutes.Value < entry.CheckInMinutes.Value)
            {
                throw new DomainValidationException("INVALID_TIME", "Check-out time must be on or after check-in time.");
            }

            var notes = string.IsNullOrWhiteSpace(entry.Notes) ? null : entry.Notes.Trim();
            if (notes is { Length: > 300 })
            {
                notes = notes[..300];
            }

            if (existing.TryGetValue(entry.StaffUserId, out var rec))
            {
                rec.Status = entry.Status;
                rec.CheckInMinutes = entry.CheckInMinutes;
                rec.CheckOutMinutes = entry.CheckOutMinutes;
                rec.Notes = notes;
            }
            else
            {
                db.StaffAttendanceRecords.Add(new StaffAttendanceRecord
                {
                    StaffUserId = entry.StaffUserId,
                    Date = request.Date,
                    Status = entry.Status,
                    CheckInMinutes = entry.CheckInMinutes,
                    CheckOutMinutes = entry.CheckOutMinutes,
                    Notes = notes
                });
            }
        }

        await auditService.LogAsync(
            "StaffAttendance.Saved",
            nameof(StaffAttendanceRecord),
            request.Date.ToString("yyyy-MM-dd"),
            new { Date = request.Date, Count = request.Entries.Count },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetAttendanceSheetAsync(request.Date, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffLeaveRequestDto>> GetLeaveRequestsAsync(
        StaffLeaveStatus? status = null,
        Guid? staffUserId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureStaffLeaveEnabledAsync(cancellationToken);

        var query = db.StaffLeaveRequests.AsNoTracking().AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(l => l.Status == status.Value);
        }

        if (staffUserId.HasValue)
        {
            query = query.Where(l => l.StaffUserId == staffUserId.Value);
        }

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);

        return await MapLeavesAsync(items, cancellationToken);
    }

    public async Task<StaffLeaveRequestDto> CreateLeaveRequestAsync(
        CreateStaffLeaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureStaffLeaveEnabledAsync(cancellationToken);
        var staffUserId = request.StaffUserId ?? tenantContext.UserId
            ?? throw new DomainValidationException("STAFF_REQUIRED", "Specify a staff member.");

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == staffUserId && u.RoleType != RoleType.Parent, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(User), staffUserId.ToString());

        if (request.EndDate < request.StartDate)
        {
            throw new DomainValidationException("INVALID_DATE_RANGE", "Leave end date must be on or after the start date.");
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 2 or > 500)
        {
            throw new DomainValidationException("INVALID_REASON", "Leave reason must be between 2 and 500 characters.");
        }

        var overlap = await db.StaffLeaveRequests.AnyAsync(
            l => l.StaffUserId == user.Id
                 && (l.Status == StaffLeaveStatus.Pending || l.Status == StaffLeaveStatus.Approved)
                 && l.StartDate <= request.EndDate
                 && request.StartDate <= l.EndDate,
            cancellationToken);

        if (overlap)
        {
            throw new DomainValidationException(
                "LEAVE_OVERLAP",
                "This staff member already has a pending or approved leave request overlapping those dates.",
                409);
        }

        var leave = new StaffLeaveRequest
        {
            StaffUserId = user.Id,
            LeaveType = request.LeaveType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Reason = reason,
            Status = StaffLeaveStatus.Pending
        };

        db.StaffLeaveRequests.Add(leave);
        await auditService.LogAsync(
            "StaffLeave.Requested",
            nameof(StaffLeaveRequest),
            leave.Id.ToString(),
            new { StaffUserId = user.Id, LeaveType = leave.LeaveType.ToString(), leave.StartDate, leave.EndDate },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapLeavesAsync([leave], cancellationToken)).Single();
    }

    public async Task<StaffLeaveRequestDto> ReviewLeaveRequestAsync(
        Guid leaveId,
        ReviewStaffLeaveRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureStaffLeaveEnabledAsync(cancellationToken);

        var leave = await db.StaffLeaveRequests.FirstOrDefaultAsync(l => l.Id == leaveId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(StaffLeaveRequest), leaveId.ToString());

        if (leave.Status != StaffLeaveStatus.Pending)
        {
            throw new DomainValidationException("LEAVE_ALREADY_REVIEWED", "Only pending leave requests can be reviewed.");
        }

        leave.Status = request.Approved ? StaffLeaveStatus.Approved : StaffLeaveStatus.Rejected;
        leave.ReviewedByUserId = tenantContext.UserId;
        leave.ReviewedAt = DateTimeOffset.UtcNow;
        leave.ReviewNotes = string.IsNullOrWhiteSpace(request.ReviewNotes)
            ? null
            : request.ReviewNotes.Trim()[..Math.Min(500, request.ReviewNotes.Trim().Length)];

        if (request.Approved)
        {
            var maxDays = Math.Min(31, leave.EndDate.DayNumber - leave.StartDate.DayNumber + 1);
            var dates = Enumerable.Range(0, maxDays)
                .Select(offset => leave.StartDate.AddDays(offset))
                .ToList();

            var existingAttendance = await db.StaffAttendanceRecords
                .Where(r => r.StaffUserId == leave.StaffUserId && dates.Contains(r.Date))
                .ToDictionaryAsync(r => r.Date, cancellationToken);

            foreach (var d in dates)
            {
                if (existingAttendance.TryGetValue(d, out var att))
                {
                    att.Status = StaffAttendanceStatus.OnLeave;
                    att.Notes ??= $"Approved {leave.LeaveType} leave";
                }
                else
                {
                    db.StaffAttendanceRecords.Add(new StaffAttendanceRecord
                    {
                        StaffUserId = leave.StaffUserId,
                        Date = d,
                        Status = StaffAttendanceStatus.OnLeave,
                        Notes = $"Approved {leave.LeaveType} leave"
                    });
                }
            }
        }

        await auditService.LogAsync(
            request.Approved ? "StaffLeave.Approved" : "StaffLeave.Rejected",
            nameof(StaffLeaveRequest),
            leave.Id.ToString(),
            new { leave.StaffUserId, Status = leave.Status.ToString(), leave.ReviewNotes },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapLeavesAsync([leave], cancellationToken)).Single();
    }

    private async Task<IReadOnlyList<StaffLeaveRequestDto>> MapLeavesAsync(
        IReadOnlyList<StaffLeaveRequest> items,
        CancellationToken cancellationToken)
    {
        var userIds = items.Select(i => i.StaffUserId).Distinct().ToList();
        var users = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return items.Select(l =>
        {
            users.TryGetValue(l.StaffUserId, out var u);
            var days = Math.Max(1, l.EndDate.DayNumber - l.StartDate.DayNumber + 1);
            return new StaffLeaveRequestDto(
                l.Id,
                l.StaffUserId,
                u?.FullName ?? string.Empty,
                u?.Email ?? string.Empty,
                l.LeaveType.ToString(),
                l.StartDate,
                l.EndDate,
                days,
                l.Reason,
                l.Status.ToString(),
                l.ReviewedByUserId,
                l.ReviewedAt,
                l.ReviewNotes,
                l.CreatedAt);
        }).ToList();
    }

    private async Task EnsureStaffLeaveEnabledAsync(CancellationToken cancellationToken)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var eval = await featureService.EvaluateAsync(tenantId, FeatureKey.StaffLeave, cancellationToken);
        if (!eval.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.StaffLeave);
        }

        if (!eval.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.StaffLeave);
        }
    }
}
