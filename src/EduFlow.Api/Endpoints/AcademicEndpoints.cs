using EduFlow.Api.Authorization;
using EduFlow.Api.Filters;
using EduFlow.Application.Academics;
using EduFlow.Application.Marketing;
using EduFlow.Application.Staff;
using EduFlow.Application.Students;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;

namespace EduFlow.Api.Endpoints;

public static class AcademicEndpoints
{
    public static IEndpointRouteBuilder MapAcademicEndpoints(this IEndpointRouteBuilder app)
    {
        var levels = app.MapGroup("/api/education-levels").WithTags("Education levels").RequireAuthorization();
        levels.MapGet("/", async (IAcademicService service, CancellationToken ct) => Results.Ok(await service.GetLevelsAsync(ct)))
            .RequirePermission(Permissions.AcademicsView)
            .WithName("GetEducationLevels");
        levels.MapPut("/selection", async (UpdateLevelSelectionRequest request, IAcademicService service, CancellationToken ct) =>
            Results.Ok(await service.SetEnabledLevelsAsync(request, ct)))
            .RequirePermission(Permissions.AcademicsManage)
            .WithName("SetEducationLevelSelection");

        var groups = app.MapGroup("/api/batch-groups").WithTags("Batch groups").RequireAuthorization().RequireFeature(FeatureKey.BatchGroups);
        groups.MapGet("/", async (IAcademicService service, CancellationToken ct) => Results.Ok(await service.GetBatchGroupsAsync(ct)))
            .RequirePermission(Permissions.AcademicsView)
            .WithName("GetBatchGroups");
        groups.MapPost("/", async (UpsertBatchGroupRequest request, IAcademicService service, CancellationToken ct) =>
        {
            var created = await service.CreateBatchGroupAsync(request, ct);
            return Results.Created($"/api/batch-groups/{created.Id}", created);
        }).RequirePermission(Permissions.AcademicsManage).WithName("CreateBatchGroup");
        groups.MapPut("/{id:guid}", async (Guid id, UpsertBatchGroupRequest request, IAcademicService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateBatchGroupAsync(id, request, ct)))
            .RequirePermission(Permissions.AcademicsManage)
            .WithName("UpdateBatchGroup");
        groups.MapDelete("/{id:guid}", async (Guid id, IAcademicService service, CancellationToken ct) =>
        {
            await service.DeleteBatchGroupAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AcademicsManage).WithName("DeleteBatchGroup");

        var batches = app.MapGroup("/api/batches").WithTags("Batches").RequireAuthorization();
        batches.MapGet("/", async (IAcademicService service, CancellationToken ct) => Results.Ok(await service.GetBatchesAsync(ct)))
            .RequirePermission(Permissions.AcademicsView)
            .WithName("GetBatches");
        batches.MapPost("/", async (UpsertBatchRequest request, IAcademicService service, CancellationToken ct) =>
        {
            var created = await service.CreateBatchAsync(request, ct);
            return Results.Created($"/api/batches/{created.Id}", created);
        }).RequirePermission(Permissions.AcademicsManage).WithName("CreateBatch");
        batches.MapPut("/{id:guid}", async (Guid id, UpsertBatchRequest request, IAcademicService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateBatchAsync(id, request, ct)))
            .RequirePermission(Permissions.AcademicsManage)
            .WithName("UpdateBatch");
        batches.MapDelete("/{id:guid}", async (Guid id, IAcademicService service, CancellationToken ct) =>
        {
            await service.DeleteBatchAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AcademicsManage).WithName("DeleteBatch");

        var subjects = app.MapGroup("/api/subjects").WithTags("Subjects").RequireAuthorization();
        subjects.MapGet("/", async (IAcademicService service, CancellationToken ct) => Results.Ok(await service.GetSubjectsAsync(ct)))
            .RequirePermission(Permissions.AcademicsView)
            .WithName("GetSubjects");
        subjects.MapPost("/", async (UpsertSubjectRequest request, IAcademicService service, CancellationToken ct) =>
        {
            var created = await service.CreateSubjectAsync(request, ct);
            return Results.Created($"/api/subjects/{created.Id}", created);
        }).RequirePermission(Permissions.AcademicsManage).WithName("CreateSubject");
        subjects.MapPut("/{id:guid}", async (Guid id, UpsertSubjectRequest request, IAcademicService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateSubjectAsync(id, request, ct)))
            .RequirePermission(Permissions.AcademicsManage)
            .WithName("UpdateSubject");
        subjects.MapDelete("/{id:guid}", async (Guid id, IAcademicService service, CancellationToken ct) =>
        {
            await service.DeleteSubjectAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AcademicsManage).WithName("DeleteSubject");

        var classes = app.MapGroup("/api/class-schedules").WithTags("Class schedules").RequireAuthorization();
        classes.MapGet("/", async (IAcademicService service, CancellationToken ct) => Results.Ok(await service.GetSchedulesAsync(ct)))
            .RequirePermission(Permissions.AcademicsView)
            .WithName("GetClassSchedules");
        classes.MapGet("/teachers", async (IAcademicService service, CancellationToken ct) => Results.Ok(await service.GetTeachersAsync(ct)))
            .RequirePermission(Permissions.AcademicsView)
            .WithName("GetClassTeachers");
        classes.MapPost("/", async (UpsertClassScheduleRequest request, IAcademicService service, CancellationToken ct) =>
        {
            var created = await service.CreateScheduleAsync(request, ct);
            return Results.Created($"/api/class-schedules/{created.Id}", created);
        }).RequirePermission(Permissions.AcademicsManage).WithName("CreateClassSchedule");
        classes.MapPut("/{id:guid}", async (Guid id, UpsertClassScheduleRequest request, IAcademicService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateScheduleAsync(id, request, ct)))
            .RequirePermission(Permissions.AcademicsManage)
            .WithName("UpdateClassSchedule");
        classes.MapDelete("/{id:guid}", async (Guid id, IAcademicService service, CancellationToken ct) =>
        {
            await service.DeleteScheduleAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.AcademicsManage).WithName("DeleteClassSchedule");

        var students = app.MapGroup("/api/students").WithTags("Students").RequireAuthorization();
        students.MapGet("/", async (
            string? search,
            string? status,
            Guid? educationLevelId,
            Guid? batchId,
            int? page,
            int? pageSize,
            IStudentService service,
            CancellationToken ct) =>
            Results.Ok(await service.ListAsync(new StudentListQuery(search, status, educationLevelId, batchId, page ?? 1, pageSize ?? 50), ct)))
            .RequirePermission(Permissions.StudentsView)
            .WithName("GetStudents");
        students.MapGet("/export", async (string? format, IStudentService service, CancellationToken ct) =>
            File(await service.ExportAsync(format, ct)))
            .RequirePermission(Permissions.StudentsView)
            .RequireFeature(FeatureKey.DataExport)
            .WithName("ExportStudents");
        students.MapGet("/import-template", async (string? format, IStudentService service, CancellationToken ct) =>
            File(await service.TemplateAsync(format, ct)))
            .RequirePermission(Permissions.StudentsEdit)
            .WithName("GetStudentImportTemplate");
        students.MapPost("/import", async (IFormFile file, IStudentService service, CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                throw new DomainValidationException("IMPORT_EMPTY", "The file is empty.");
            }

            await using var stream = file.OpenReadStream();
            return Results.Ok(await service.ImportAsync(stream, file.FileName, ct));
        }).RequirePermission(Permissions.StudentsEdit).DisableAntiforgery().WithName("ImportStudents");
        students.MapPost("/id-cards", async (BulkStudentRequest request, IStudentService service, IPublicSiteService site, CancellationToken ct) =>
        {
            var file = await service.IdCardsAsync(request, ct);
            await site.MarkCardsPrintedAsync(ct);
            return File(file);
        })
            .RequirePermission(Permissions.StudentsView)
            .RequireFeature(FeatureKey.BarcodeGenerator)
            .WithName("PrintStudentIdCards");
        students.MapPost("/labels", async (BulkStudentRequest request, IStudentService service, IPublicSiteService site, CancellationToken ct) =>
        {
            var file = await service.LabelsAsync(request, ct);
            await site.MarkCardsPrintedAsync(ct);
            return File(file);
        })
            .RequirePermission(Permissions.StudentsView)
            .RequireFeature(FeatureKey.BarcodeGenerator)
            .WithName("PrintStudentBarcodeLabels");
        students.MapGet("/{id:guid}", async (Guid id, IStudentService service, CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)))
            .RequirePermission(Permissions.StudentsView)
            .WithName("GetStudent");
        students.MapPost("/", async (UpsertStudentRequest request, IStudentService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/api/students/{created.Id}", created);
        }).RequirePermission(Permissions.StudentsEdit).WithName("CreateStudent");
        students.MapPut("/{id:guid}", async (Guid id, UpsertStudentRequest request, IStudentService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateAsync(id, request, ct)))
            .RequirePermission(Permissions.StudentsEdit)
            .WithName("UpdateStudent");
        students.MapDelete("/{id:guid}", async (Guid id, IStudentService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(id, ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.StudentsEdit).WithName("DeleteStudent");
        students.MapPost("/{id:guid}/photo", async (Guid id, IFormFile file, IStudentService service, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return Results.Ok(await service.SavePhotoAsync(id, stream, file.FileName, file.ContentType, file.Length, ct));
        }).RequirePermission(Permissions.StudentsEdit).DisableAntiforgery().WithName("UploadStudentPhoto");
        students.MapGet("/{id:guid}/id-card", async (Guid id, IStudentService service, IPublicSiteService site, CancellationToken ct) =>
        {
            var file = await service.IdCardAsync(id, ct);
            await site.MarkCardsPrintedAsync(ct);
            return File(file);
        })
            .RequirePermission(Permissions.StudentsView)
            .RequireFeature(FeatureKey.BarcodeGenerator)
            .WithName("GetStudentIdCard");

        var staff = app.MapGroup("/api/staff").WithTags("Staff").RequireAuthorization();
        staff.MapGet("/role-templates", (IStaffDirectoryService service) => Results.Ok(service.GetTemplates()))
            .RequirePermission(Permissions.UsersView)
            .WithName("GetStaffRoleTemplates");
        staff.MapPost("/", async (CreateStaffRequest request, IStaffDirectoryService service, CancellationToken ct) =>
        {
            var created = await service.CreateAsync(request, ct);
            return Results.Created($"/api/users/{created.Id}", created);
        }).RequirePermission(Permissions.UsersManage).WithName("CreateStaffMember");

        app.MapPut("/api/users/{id:guid}/status", async (Guid id, SetUserActiveRequest request, IStaffDirectoryService service, CancellationToken ct) =>
            Results.Ok(await service.SetActiveAsync(id, request.IsActive, ct)))
            .WithTags("Users")
            .RequireAuthorization()
            .RequirePermission(Permissions.UsersManage)
            .WithName("SetUserActive");

        return app;
    }

    private static IResult File(StoredDownload download) =>
        Results.File(download.Content, download.ContentType, download.FileName);
}
