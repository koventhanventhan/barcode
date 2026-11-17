using EduFlow.Api.Authorization;
using EduFlow.Api.Filters;
using EduFlow.Application.Academics;
using EduFlow.Application.Backups;
using EduFlow.Application.Staff;
using EduFlow.Application.SuperAdmin;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/announcements/active", async (ISuperAdminService superAdminService, CancellationToken ct) =>
            Results.Ok(await superAdminService.GetAnnouncementsAsync(activeOnly: true, ct)))
            .WithTags("Announcements")
            .RequireAuthorization()
            .WithName("GetActiveAnnouncements");

        var backups = app.MapGroup("/api/backups")
            .WithTags("Backups")
            .RequireAuthorization()
            .RequireFeature(FeatureKey.AutoBackup);

        backups.MapGet("/", async (IBackupAndCleanupService service, CancellationToken ct) =>
            Results.Ok(await service.GetBackupWorkspaceAsync(ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("GetTenantBackupWorkspace");

        backups.MapPut("/policy", async (
            [FromBody] UpdateTenantBackupPolicyRequest request,
            IBackupAndCleanupService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateBackupPolicyAsync(request, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("UpdateTenantBackupPolicy");

        backups.MapPost("/run", async (IBackupAndCleanupService service, CancellationToken ct) =>
        {
            var created = await service.RunTenantBackupAsync(null, BackupTrigger.Manual, ct);
            return Results.Ok(created);
        })
        .RequirePermission(Permissions.SettingsManage)
        .WithName("RunTenantBackupNow");

        backups.MapGet("/{id:guid}/download", async (
            Guid id,
            IBackupAndCleanupService service,
            CancellationToken ct) =>
        {
            var file = await service.DownloadTenantBackupAsync(id, null, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        })
        .RequirePermission(Permissions.SettingsManage)
        .WithName("DownloadTenantBackup");

        var cleanup = app.MapGroup("/api/settings/cleanup")
            .WithTags("Report cleanup")
            .RequireAuthorization()
            .RequireFeature(FeatureKey.ReportCleanup);

        cleanup.MapGet("/", async (IBackupAndCleanupService service, CancellationToken ct) =>
            Results.Ok(await service.GetCleanupWorkspaceAsync(ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("GetReportCleanupWorkspace");

        cleanup.MapPut("/policy", async (
            [FromBody] UpdateTenantCleanupPolicyRequest request,
            IBackupAndCleanupService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateCleanupPolicyAsync(request, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("UpdateReportCleanupPolicy");

        cleanup.MapPost("/run", async (
            [FromBody] RunReportCleanupRequest request,
            IBackupAndCleanupService service,
            CancellationToken ct) =>
            Results.Ok(await service.RunReportCleanupAsync(request, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("RunReportCleanup");

        var timetable = app.MapGroup("/api/timetable")
            .WithTags("Timetable")
            .RequireAuthorization()
            .RequireFeature(FeatureKey.Timetable);

        timetable.MapGet("/", async (
            [FromQuery] Guid? batchId,
            [FromQuery] Guid? teacherUserId,
            [FromQuery] string? room,
            ITimetableService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetWeeklyTimetableAsync(batchId, teacherUserId, room, ct)))
            .RequirePermission(Permissions.AcademicsView)
            .WithName("GetWeeklyTimetable");

        timetable.MapPost("/check-clashes", async (
            [FromBody] CheckTimetableClashesRequest request,
            ITimetableService service,
            CancellationToken ct) =>
            Results.Ok(await service.CheckClashesAsync(request, ct)))
            .RequirePermission(Permissions.AcademicsView)
            .WithName("CheckTimetableClashes");

        timetable.MapPost("/slots", async (
            [FromBody] UpsertClassScheduleRequest request,
            ITimetableService service,
            CancellationToken ct) =>
        {
            var created = await service.CreateSlotAsync(request, ct);
            return Results.Created($"/api/timetable/slots/{created.Id}", created);
        })
        .RequirePermission(Permissions.AcademicsManage)
        .WithName("CreateTimetableSlot");

        timetable.MapPut("/slots/{id:guid}", async (
            Guid id,
            [FromBody] UpsertClassScheduleRequest request,
            ITimetableService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateSlotAsync(id, request, ct)))
            .RequirePermission(Permissions.AcademicsManage)
            .WithName("UpdateTimetableSlot");

        timetable.MapDelete("/slots/{id:guid}", async (
            Guid id,
            ITimetableService service,
            CancellationToken ct) =>
        {
            await service.DeleteSlotAsync(id, ct);
            return Results.NoContent();
        })
        .RequirePermission(Permissions.AcademicsManage)
        .WithName("DeleteTimetableSlot");

        var staffLeaveGroup = app.MapGroup("/api/staff")
            .WithTags("Staff & Leave")
            .RequireAuthorization()
            .RequireFeature(FeatureKey.StaffLeave);

        staffLeaveGroup.MapGet("/attendance", async (
            [FromQuery] DateOnly? date,
            IStaffOperationsService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetAttendanceSheetAsync(date, ct)))
            .RequirePermission(Permissions.UsersView)
            .WithName("GetStaffAttendanceSheet");

        staffLeaveGroup.MapPut("/attendance", async (
            [FromBody] SaveStaffAttendanceSheetRequest request,
            IStaffOperationsService service,
            CancellationToken ct) =>
            Results.Ok(await service.SaveAttendanceSheetAsync(request, ct)))
            .RequirePermission(Permissions.UsersManage)
            .WithName("SaveStaffAttendanceSheet");

        staffLeaveGroup.MapGet("/leaves", async (
            [FromQuery] StaffLeaveStatus? status,
            [FromQuery] Guid? staffUserId,
            IStaffOperationsService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetLeaveRequestsAsync(status, staffUserId, ct)))
            .RequirePermission(Permissions.UsersView)
            .WithName("GetStaffLeaveRequests");

        staffLeaveGroup.MapPost("/leaves", async (
            [FromBody] CreateStaffLeaveRequest request,
            IStaffOperationsService service,
            CancellationToken ct) =>
        {
            var created = await service.CreateLeaveRequestAsync(request, ct);
            return Results.Created($"/api/staff/leaves/{created.Id}", created);
        })
        .RequirePermission(Permissions.UsersView)
        .WithName("CreateStaffLeaveRequest");

        staffLeaveGroup.MapPost("/leaves/{id:guid}/review", async (
            Guid id,
            [FromBody] ReviewStaffLeaveRequest request,
            IStaffOperationsService service,
            CancellationToken ct) =>
            Results.Ok(await service.ReviewLeaveRequestAsync(id, request, ct)))
            .RequirePermission(Permissions.UsersManage)
            .WithName("ReviewStaffLeaveRequest");

        return app;
    }
}
