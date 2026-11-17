using EduFlow.Api.Authorization;
using EduFlow.Api.Filters;
using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Parents;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;

namespace EduFlow.Api.Endpoints;

public static class ParentEndpoints
{
    public static IEndpointRouteBuilder MapParentEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/parents")
            .WithTags("Parents")
            .RequireAuthorization();

        admin.MapGet("/", async (IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.ListParentsAsync(ct)))
            .RequirePermission(Permissions.StudentsView)
            .WithName("ListParentAccounts");

        admin.MapPost("/", async (CreateParentAccountRequest request, IParentPortalService service, CancellationToken ct) =>
        {
            var created = await service.CreateParentAsync(request, ct);
            return Results.Created($"/api/parents/{created.Id}", created);
        })
        .RequirePermission(Permissions.StudentsEdit)
        .WithName("CreateParentAccount");

        admin.MapPut("/{id:guid}/students", async (Guid id, UpdateParentStudentsRequest request, IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateStudentsAsync(id, request, ct)))
            .RequirePermission(Permissions.StudentsEdit)
            .WithName("UpdateParentStudents");

        admin.MapPost("/{id:guid}/invite", async (Guid id, IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.ResendInviteAsync(id, ct)))
            .RequirePermission(Permissions.StudentsEdit)
            .WithName("ResendParentInvite");

        admin.MapPost("/{id:guid}/deactivate", async (Guid id, IParentPortalService service, CancellationToken ct) =>
        {
            await service.DeactivateParentAsync(id, ct);
            return Results.NoContent();
        })
        .RequirePermission(Permissions.StudentsEdit)
        .WithName("DeactivateParentAccount");

        var parent = app.MapGroup("/api/parent")
            .WithTags("ParentPortal")
            .RequireAuthorization(AuthorizationPolicies.ParentOnly)
            .RequireFeature(FeatureKey.ParentPortal);

        parent.MapGet("/portal", [RequireFeature(FeatureKey.ParentPortal)] (ITenantContext tenantContext) =>
            Results.Ok(new { status = "Available", tenantId = tenantContext.TenantId }))
            .WithName("GetParentPortalStatus");

        parent.MapGet("/children", async (IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.GetHomeAsync(ct)))
            .WithName("GetParentChildren");

        parent.MapGet("/children/{studentId:guid}/dashboard", async (Guid studentId, IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.GetDashboardAsync(studentId, ct)))
            .WithName("GetParentDashboard");

        parent.MapGet("/children/{studentId:guid}/attendance", async (Guid studentId, DateOnly? from, DateOnly? to, IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.GetAttendanceAsync(studentId, from, to, ct)))
            .WithName("GetParentAttendance");

        parent.MapGet("/children/{studentId:guid}/classes", async (Guid studentId, IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.GetClassesAsync(studentId, ct)))
            .WithName("GetParentClasses");

        parent.MapGet("/children/{studentId:guid}/exams", async (Guid studentId, IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.GetExamsAsync(studentId, ct)))
            .WithName("GetParentExams");

        parent.MapGet("/children/{studentId:guid}/exams/{examId:guid}/report-card", async (Guid studentId, Guid examId, IParentPortalService service, CancellationToken ct) =>
            ToFile(await service.GetReportCardAsync(studentId, examId, ct)))
            .RequireFeature(FeatureKey.ParentCanSeeMarks)
            .WithName("GetParentReportCard");

        parent.MapGet("/children/{studentId:guid}/invoices", async (Guid studentId, IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.GetInvoicesAsync(studentId, ct)))
            .WithName("GetParentInvoices");

        parent.MapGet("/children/{studentId:guid}/payments", async (Guid studentId, IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.GetPaymentsAsync(studentId, ct)))
            .WithName("GetParentPayments");

        parent.MapPost("/children/{studentId:guid}/invoices/{invoiceId:guid}/pay", async (
            Guid studentId,
            Guid invoiceId,
            ParentPayInvoiceRequest? request,
            IParentPortalService service,
            CancellationToken ct) =>
            Results.Ok(await service.PayInvoiceAsync(studentId, invoiceId, request ?? new ParentPayInvoiceRequest(), ct)))
            .RequireFeature(FeatureKey.ParentCanPayOnline)
            .WithName("ParentPayInvoice");

        parent.MapGet("/messages", async (IParentPortalService service, CancellationToken ct) =>
            Results.Ok(await service.GetMessagesAsync(ct)))
            .WithName("GetParentMessages");

        return app;
    }

    private static IResult ToFile(StoredDownload download) =>
        Results.File(download.Content, download.ContentType, download.FileName);
}
