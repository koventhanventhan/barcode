using EduFlow.Api.Authorization;
using EduFlow.Api.Filters;
using EduFlow.Application.Attendance;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;

namespace EduFlow.Api.Endpoints;

public static class AttendanceEndpoints
{
    public static IEndpointRouteBuilder MapAttendanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/attendance").WithTags("Attendance").RequireAuthorization();

        group.MapGet("/policy", async (IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.GetPolicyAsync(ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("GetAttendancePolicy");
        group.MapPut("/policy", async (UpdateAttendancePolicyRequest request, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.UpdatePolicyAsync(request, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("UpdateAttendancePolicy");

        group.MapGet("/sessions", async (DateOnly? date, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.ListSessionsAsync(date, ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("ListClassSessions");
        group.MapPost("/sessions", async (OpenClassSessionRequest request, IAttendanceService service, CancellationToken ct) =>
        {
            var created = await service.OpenSessionAsync(request, ct);
            return Results.Created($"/api/attendance/sessions/{created.Id}", created);
        }).RequirePermission(Permissions.AttendanceMark).WithName("OpenClassSession");
        group.MapPost("/sessions/{id:guid}/start", async (Guid id, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.StartSessionAsync(id, ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("StartClassSession");
        group.MapPost("/sessions/{id:guid}/stop", async (Guid id, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.StopSessionAsync(id, ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("StopClassSession");
        group.MapPost("/sessions/{id:guid}/cancel", async (Guid id, CancelClassSessionRequest request, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.CancelSessionAsync(id, request, ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("CancelClassSession");
        group.MapGet("/sessions/{id:guid}/board", async (Guid id, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.GetBoardAsync(id, ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("GetAttendanceBoard");
        group.MapPost("/sessions/{id:guid}/marks", async (Guid id, ManualAttendanceRequest request, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.MarkManualAsync(id, request, ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("MarkAttendanceManual");
        group.MapPost("/sessions/{id:guid}/marks/{studentId:guid}/undo", async (Guid id, Guid studentId, UndoAttendanceRequest request, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.UndoAsync(id, studentId, request, ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("UndoAttendanceMark");

        group.MapPost("/scan", async (ScanAttendanceRequest request, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.ScanAsync(request, ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("ScanAttendance");
        group.MapGet("/register", async (Guid? batchId, DateOnly? date, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.GetRegisterAsync(new AttendanceRegisterQuery(batchId, date), ct)))
            .RequirePermission(Permissions.AttendanceMark)
            .WithName("GetAttendanceRegister");
        group.MapGet("/reports", async (DateOnly? from, DateOnly? to, Guid? batchId, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.GetReportAsync(new AttendanceReportQuery(from, to, batchId), ct)))
            .RequirePermission(Permissions.ReportsView)
            .WithName("GetAttendanceReport");
        group.MapGet("/students/{studentId:guid}", async (Guid studentId, DateOnly? from, DateOnly? to, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.GetStudentHistoryAsync(studentId, from, to, ct)))
            .RequirePermission(Permissions.StudentsView)
            .WithName("GetStudentAttendance");

        app.MapPost("/api/attendance/join", async (JoinAttendanceRequest request, IAttendanceService service, CancellationToken ct) =>
            Results.Ok(await service.JoinOnlineAsync(request, ct)))
            .RequireFeature(FeatureKey.OnlineAttendance)
            .WithTags("Attendance")
            .WithName("JoinOnlineAttendance");

        return app;
    }
}
