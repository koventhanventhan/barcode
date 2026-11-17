using EduFlow.Api.Authorization;
using EduFlow.Application.Backups;
using EduFlow.Application.Fees;
using EduFlow.Application.SuperAdmin;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public static class SuperAdminEndpoints
{
    public static IEndpointRouteBuilder MapSuperAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/super-admin")
            .WithTags("SuperAdmin")
            .RequireSuperAdmin();

        group.MapGet("/tenants", async (
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext httpContext,
            ISuperAdminService service,
            CancellationToken ct) =>
        {
            var all = await service.GetTenantsAsync(ct);
            httpContext.Response.Headers["X-Total-Count"] = all.Count.ToString();
            if (page.HasValue || pageSize.HasValue)
            {
                var p = Math.Max(1, page ?? 1);
                var ps = Math.Clamp(pageSize ?? 50, 1, 200);
                httpContext.Response.Headers["X-Page"] = p.ToString();
                httpContext.Response.Headers["X-Page-Size"] = ps.ToString();
                return Results.Ok(all.Skip((p - 1) * ps).Take(ps).ToList());
            }

            return Results.Ok(all);
        }).WithName("SuperAdminGetTenants");

        group.MapGet("/tenants/{id:guid}", async (Guid id, ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.GetTenantByIdAsync(id, ct)))
            .WithName("SuperAdminGetTenantById");

        group.MapPost("/tenants", async ([FromBody] CreateTenantRequest request, ISuperAdminService service, CancellationToken ct) =>
        {
            var created = await service.CreateTenantAsync(request, ct);
            return Results.Created($"/api/super-admin/tenants/{created.Id}", created);
        }).WithName("SuperAdminCreateTenant");

        group.MapPut("/tenants/{id:guid}", async (Guid id, [FromBody] UpdateTenantRequest request, ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateTenantAsync(id, request, ct)))
            .WithName("SuperAdminUpdateTenant");

        group.MapDelete("/tenants/{id:guid}", async (Guid id, ISuperAdminService service, CancellationToken ct) =>
        {
            await service.DeleteTenantAsync(id, ct);
            return Results.NoContent();
        }).WithName("SuperAdminDeleteTenant");

        group.MapGet("/plans", async (ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.GetPlansAsync(activeOnly: false, ct)))
            .WithName("SuperAdminGetPlans");

        group.MapGet("/plans/{id:guid}", async (Guid id, ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.GetPlanByIdAsync(id, ct)))
            .WithName("SuperAdminGetPlanById");

        group.MapPost("/plans", async ([FromBody] CreatePlanRequest request, ISuperAdminService service, CancellationToken ct) =>
        {
            var created = await service.CreatePlanAsync(request, ct);
            return Results.Created($"/api/super-admin/plans/{created.Id}", created);
        }).WithName("SuperAdminCreatePlan");

        group.MapPut("/plans/{id:guid}", async (Guid id, [FromBody] UpdatePlanRequest request, ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.UpdatePlanAsync(id, request, ct)))
            .WithName("SuperAdminUpdatePlan");

        group.MapDelete("/plans/{id:guid}", async (Guid id, ISuperAdminService service, CancellationToken ct) =>
        {
            await service.DeletePlanAsync(id, ct);
            return Results.NoContent();
        }).WithName("SuperAdminDeletePlan");

        group.MapPost("/tenants/{id:guid}/subscription/assign-plan", async (
            Guid id,
            [FromBody] AssignPlanRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.AssignPlanAsync(id, request, ct)))
            .WithName("SuperAdminAssignPlan");

        group.MapPost("/tenants/{id:guid}/subscription/activate", async (
            Guid id,
            [FromBody] ActivateSubscriptionRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.ActivateSubscriptionAsync(id, request, ct)))
            .WithName("SuperAdminActivateSubscription");

        group.MapPost("/tenants/{id:guid}/subscription/suspend", async (
            Guid id,
            [FromBody] SuspendSubscriptionRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.SuspendSubscriptionAsync(id, request, ct)))
            .WithName("SuperAdminSuspendSubscription");

        group.MapPost("/tenants/{id:guid}/subscription/extend", async (
            Guid id,
            [FromBody] ExtendSubscriptionRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.ExtendSubscriptionAsync(id, request, ct)))
            .WithName("SuperAdminExtendSubscription");

        group.MapGet("/tenants/{id:guid}/feature-overrides", async (
            Guid id,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetFeatureOverridesAsync(id, ct)))
            .WithName("SuperAdminGetFeatureOverrides");

        group.MapPut("/tenants/{id:guid}/feature-overrides/{featureKey}", async (
            Guid id,
            string featureKey,
            [FromBody] SetFeatureOverrideRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<FeatureKey>(featureKey, ignoreCase: true, out var parsedKey))
            {
                throw new DomainValidationException("INVALID_FEATURE_KEY", $"Unknown feature key '{featureKey}'.");
            }

            return Results.Ok(await service.SetFeatureOverrideAsync(id, parsedKey, request, ct));
        }).WithName("SuperAdminSetFeatureOverride");

        group.MapDelete("/tenants/{id:guid}/feature-overrides/{featureKey}", async (
            Guid id,
            string featureKey,
            ISuperAdminService service,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<FeatureKey>(featureKey, ignoreCase: true, out var parsedKey))
            {
                throw new DomainValidationException("INVALID_FEATURE_KEY", $"Unknown feature key '{featureKey}'.");
            }

            await service.RemoveFeatureOverrideAsync(id, parsedKey, ct);
            return Results.NoContent();
        }).WithName("SuperAdminRemoveFeatureOverride");

        group.MapPost("/tenants/{id:guid}/impersonate", async (
            Guid id,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.ImpersonateTenantAsync(id, ct)))
            .WithName("SuperAdminImpersonateTenant");

        group.MapGet("/audit-logs", async (
            [FromQuery] Guid? tenantId,
            [FromQuery] string? action,
            [FromQuery] int? take,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext httpContext,
            ISuperAdminService service,
            CancellationToken ct) =>
        {
            var limit = take ?? (page.HasValue || pageSize.HasValue ? 500 : 100);
            var logs = await service.GetAuditLogsAsync(tenantId, action, limit, ct);
            httpContext.Response.Headers["X-Total-Count"] = logs.Count.ToString();
            if (page.HasValue || pageSize.HasValue)
            {
                var p = Math.Max(1, page ?? 1);
                var ps = Math.Clamp(pageSize ?? 50, 1, 200);
                httpContext.Response.Headers["X-Page"] = p.ToString();
                httpContext.Response.Headers["X-Page-Size"] = ps.ToString();
                return Results.Ok(logs.Skip((p - 1) * ps).Take(ps).ToList());
            }

            return Results.Ok(logs);
        }).WithName("SuperAdminGetAuditLogs");

        group.MapGet("/invoices", async (
            [FromQuery] Guid? tenantId,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetPlatformInvoicesAsync(tenantId, ct)))
            .WithName("SuperAdminGetPlatformInvoices");

        group.MapGet("/tenants/{id:guid}/invoices", async (
            Guid id,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetPlatformInvoicesAsync(id, ct)))
            .WithName("SuperAdminGetTenantPlatformInvoices");

        group.MapPost("/tenants/{id:guid}/invoices", async (
            Guid id,
            [FromBody] CreatePlatformInvoiceRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
        {
            var created = await service.CreatePlatformInvoiceAsync(id, request, ct);
            return Results.Created($"/api/super-admin/invoices/{created.Id}", created);
        }).WithName("SuperAdminCreatePlatformInvoice");

        group.MapPost("/invoices/{invoiceId:guid}/mark-paid", async (
            Guid invoiceId,
            [FromBody] MarkPlatformInvoicePaidRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.MarkPlatformInvoicePaidAsync(invoiceId, request, ct)))
            .WithName("SuperAdminMarkPlatformInvoicePaid");

        group.MapGet("/invoices/{invoiceId:guid}/pdf", async (
            Guid invoiceId,
            ISuperAdminService service,
            CancellationToken ct) =>
        {
            var pdf = await service.GetPlatformInvoicePdfAsync(invoiceId, ct);
            return Results.File(pdf.Content, pdf.ContentType, pdf.FileName);
        }).WithName("SuperAdminGetPlatformInvoicePdf");

        group.MapGet("/dashboard", async (ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.GetDashboardAsync(ct)))
            .WithName("SuperAdminGetDashboard");

        group.MapGet("/announcements", async (ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.GetAnnouncementsAsync(activeOnly: false, ct)))
            .WithName("SuperAdminGetAnnouncements");

        group.MapPost("/announcements", async (
            [FromBody] CreatePlatformAnnouncementRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
        {
            var created = await service.CreateAnnouncementAsync(request, ct);
            return Results.Created($"/api/super-admin/announcements/{created.Id}", created);
        }).WithName("SuperAdminCreateAnnouncement");

        group.MapPut("/announcements/{id:guid}/status", async (
            Guid id,
            [FromBody] SetAnnouncementActiveRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.SetAnnouncementActiveAsync(id, request.IsActive, ct)))
            .WithName("SuperAdminSetAnnouncementActive");

        group.MapDelete("/announcements/{id:guid}", async (
            Guid id,
            ISuperAdminService service,
            CancellationToken ct) =>
        {
            await service.DeleteAnnouncementAsync(id, ct);
            return Results.NoContent();
        }).WithName("SuperAdminDeleteAnnouncement");

        group.MapGet("/maintenance", async (ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.GetMaintenanceModeAsync(ct)))
            .WithName("SuperAdminGetMaintenanceMode");

        group.MapPut("/maintenance", async (
            [FromBody] UpdateMaintenanceModeRequest request,
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateMaintenanceModeAsync(request, ct)))
            .WithName("SuperAdminUpdateMaintenanceMode");

        group.MapGet("/platform-backups", async (IBackupAndCleanupService backupService, CancellationToken ct) =>
            Results.Ok(await backupService.GetPlatformBackupsAsync(ct)))
            .WithName("SuperAdminGetPlatformBackups");

        group.MapPut("/platform-backups/retention", async (
            [FromBody] UpdatePlatformBackupRetentionRequest request,
            IBackupAndCleanupService backupService,
            CancellationToken ct) =>
            Results.Ok(await backupService.UpdatePlatformBackupRetentionAsync(request.RetentionDays, ct)))
            .WithName("SuperAdminUpdatePlatformBackupRetention");

        group.MapPost("/platform-backups/run", async (
            [FromBody] RunPlatformBackupRequest? request,
            IBackupAndCleanupService backupService,
            CancellationToken ct) =>
            Results.Ok(await backupService.RunPlatformBackupAsync(BackupTrigger.Manual, request?.RetentionDays, ct)))
            .WithName("SuperAdminRunPlatformBackup");

        group.MapGet("/platform-backups/{backupId:guid}/download", async (
            Guid backupId,
            IBackupAndCleanupService backupService,
            CancellationToken ct) =>
        {
            var file = await backupService.DownloadPlatformBackupAsync(backupId, ct);
            return Results.File(file.Content, file.ContentType, file.FileName);
        }).WithName("SuperAdminDownloadPlatformBackup");

        group.MapGet("/tenants/{id:guid}/backups", async (
            Guid id,
            IBackupAndCleanupService backupService,
            CancellationToken ct) =>
            Results.Ok(await backupService.GetTenantBackupsForSupportAsync(id, ct)))
            .WithName("SuperAdminGetTenantBackups");

        group.MapPost("/tenants/{id:guid}/backups/{backupId:guid}/restore", async (
            Guid id,
            Guid backupId,
            [FromBody] RestoreTenantBackupRequest request,
            IBackupAndCleanupService backupService,
            CancellationToken ct) =>
            Results.Ok(await backupService.RestoreTenantBackupAsync(id, backupId, request, ct)))
            .WithName("SuperAdminRestoreTenantBackup");

        return app;
    }
}
