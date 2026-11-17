using EduFlow.Api.Authorization;
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

        group.MapGet("/tenants", async (ISuperAdminService service, CancellationToken ct) =>
            Results.Ok(await service.GetTenantsAsync(ct)))
            .WithName("SuperAdminGetTenants");

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
            ISuperAdminService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetAuditLogsAsync(tenantId, action, take ?? 100, ct)))
            .WithName("SuperAdminGetAuditLogs");

        return app;
    }
}
