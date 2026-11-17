using EduFlow.Api.Authorization;
using EduFlow.Api.Filters;
using EduFlow.Application.Academics;
using EduFlow.Application.Exams;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;

namespace EduFlow.Api.Endpoints;

public static class ExamEndpoints
{
    public static IEndpointRouteBuilder MapExamEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/exams")
            .WithTags("Exams")
            .RequireAuthorization();

        group.MapGet("/grading-schemes", async (IExamService service, CancellationToken ct) =>
            Results.Ok(await service.GetGradingSchemesAsync(ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("GetGradingSchemes");

        group.MapPost("/grading-schemes", async (UpsertGradingSchemeRequest request, IExamService service, CancellationToken ct) =>
        {
            var created = await service.CreateGradingSchemeAsync(request, ct);
            return Results.Created($"/api/exams/grading-schemes/{created.Id}", created);
        }).RequirePermission(Permissions.ExamsManage).WithName("CreateGradingScheme");

        group.MapPut("/grading-schemes/{id:guid}", async (Guid id, UpsertGradingSchemeRequest request, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateGradingSchemeAsync(id, request, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("UpdateGradingScheme");

        group.MapDelete("/grading-schemes/{id:guid}", async (Guid id, IExamService service, CancellationToken ct) =>
        {
            await service.DeleteGradingSchemeAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ExamsManage).WithName("DeleteGradingScheme");

        group.MapGet("/summary", async (IExamService service, CancellationToken ct) =>
            Results.Ok(await service.GetSummaryAsync(ct)))
            .RequirePermission(Permissions.ReportsView)
            .WithName("GetExamSummary");

        group.MapGet(string.Empty, async (Guid? batchId, ExamStatus? status, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.GetExamsAsync(batchId, status, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("GetExams");

        group.MapGet("/students/{studentId:guid}/trends", async (Guid studentId, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.GetStudentTrendsAsync(studentId, ct)))
            .RequirePermission(Permissions.ReportsView)
            .WithName("GetStudentExamTrends");

        group.MapPost("/reminders/run", async (RunExamRemindersRequest? request, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.ProcessExamRemindersAsync(request?.NowUtc, tenantIdOverride: null, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("RunExamReminders");

        group.MapGet("/{id:guid}", async (Guid id, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.GetExamAsync(id, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("GetExamById");

        group.MapPost(string.Empty, async (UpsertExamRequest request, IExamService service, CancellationToken ct) =>
        {
            var created = await service.CreateExamAsync(request, ct);
            return Results.Created($"/api/exams/{created.Id}", created);
        }).RequirePermission(Permissions.ExamsManage).WithName("CreateExam");

        group.MapPut("/{id:guid}", async (Guid id, UpsertExamRequest request, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateExamAsync(id, request, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("UpdateExam");

        group.MapDelete("/{id:guid}", async (Guid id, IExamService service, CancellationToken ct) =>
        {
            await service.DeleteExamAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.ExamsManage).WithName("DeleteExam");

        group.MapPost("/{id:guid}/schedule", async (Guid id, ScheduleExamRequest request, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.ScheduleExamAsync(id, request, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("ScheduleExam");

        group.MapPost("/{id:guid}/lock", async (Guid id, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.LockExamAsync(id, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("LockExam");

        group.MapPost("/{id:guid}/unlock", async (Guid id, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.UnlockExamAsync(id, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("UnlockExam");

        group.MapPost("/{id:guid}/publish", async (Guid id, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.PublishExamAsync(id, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("PublishExam");

        group.MapPost("/{id:guid}/unpublish", async (Guid id, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.UnpublishExamAsync(id, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("UnpublishExam");

        group.MapGet("/{id:guid}/marks", async (Guid id, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.GetMarksAsync(id, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("GetExamMarks");

        group.MapPut("/{id:guid}/marks", async (Guid id, SaveMarksRequest request, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.SaveMarksAsync(id, request, ct)))
            .RequirePermission(Permissions.ExamsManage)
            .WithName("SaveExamMarks");

        group.MapGet("/{id:guid}/ranks", async (Guid id, string? scope, Guid? subjectId, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.GetRanksAsync(id, scope ?? "batch", subjectId, ct)))
            .RequirePermission(Permissions.ReportsView)
            .WithName("GetExamRanks");

        group.MapGet("/{id:guid}/distribution", async (Guid id, IExamService service, CancellationToken ct) =>
            Results.Ok(await service.GetDistributionAsync(id, ct)))
            .RequirePermission(Permissions.ReportsView)
            .WithName("GetExamGradeDistribution");

        group.MapGet("/{id:guid}/report-cards", async (Guid id, Guid? studentId, IExamService service, CancellationToken ct) =>
            ToFileResult(await service.GetReportCardsPdfAsync(id, studentId, ct)))
            .RequirePermission(Permissions.ReportsView)
            .RequireFeature(FeatureKey.PerformanceReports)
            .WithName("GetExamReportCards");

        return app;
    }

    private static IResult ToFileResult(StoredDownload download) =>
        Results.File(download.Content, download.ContentType, download.FileName);
}
