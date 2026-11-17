using EduFlow.Api.Authorization;
using EduFlow.Api.Filters;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public sealed record UpdateFeatureSwitchRequest(bool IsEnabled);

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/plans", async (HttpContext httpContext, ISuperAdminService superAdminService, CancellationToken ct) =>
            {
                httpContext.Response.Headers.CacheControl = "public, max-age=60, must-revalidate";
                return Results.Ok(await superAdminService.GetPlansAsync(activeOnly: true, ct));
            })
            .WithTags("Plans")
            .WithName("GetPublicPlans");

        var settingsGroup = app.MapGroup("/api/settings")
            .WithTags("Settings")
            .RequireAuthorization();

        settingsGroup.MapGet("/features", async (
            ITenantContext tenantContext,
            IFeatureService featureService,
            CancellationToken ct) =>
        {
            var tenantId = RequireTenantId(tenantContext);
            return Results.Ok(await featureService.GetAllFeaturesAsync(tenantId, ct));
        }).WithName("GetTenantFeatures");

        settingsGroup.MapPut("/features/{featureKey}", async (
            string featureKey,
            [FromBody] UpdateFeatureSwitchRequest request,
            ITenantContext tenantContext,
            IFeatureService featureService,
            CancellationToken ct) =>
        {
            var tenantId = RequireTenantId(tenantContext);
            if (!Enum.TryParse<FeatureKey>(featureKey, ignoreCase: true, out var parsedKey))
            {
                throw new DomainValidationException("INVALID_FEATURE_KEY", $"Unknown feature key '{featureKey}'.");
            }

            var updated = await featureService.SetTenantFeatureAsync(tenantId, parsedKey, request.IsEnabled, ct);
            return Results.Ok(updated);
        })
        .RequirePermission(Permissions.SettingsManage)
        .WithName("SetTenantFeatureSwitch");

        app.MapGet("/api/subscription", async (
            ITenantManagementService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSubscriptionSummaryAsync(ct)))
            .WithTags("Subscription")
            .RequireAuthorization()
            .WithName("GetTenantSubscriptionSummary");

        var branchesGroup = app.MapGroup("/api/branches")
            .WithTags("Branches")
            .RequireAuthorization();

        branchesGroup.MapGet("/", async (ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetBranchesAsync(ct)))
            .RequirePermission(Permissions.BranchesView)
            .WithName("GetBranches");

        branchesGroup.MapGet("/{id:guid}", async (Guid id, ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetBranchByIdAsync(id, ct)))
            .RequirePermission(Permissions.BranchesView)
            .WithName("GetBranchById");

        branchesGroup.MapPost("/", async ([FromBody] CreateBranchRequest request, ITenantManagementService service, CancellationToken ct) =>
        {
            var created = await service.CreateBranchAsync(request, ct);
            return Results.Created($"/api/branches/{created.Id}", created);
        })
        .RequirePermission(Permissions.BranchesManage)
        .WithName("CreateBranch");

        branchesGroup.MapPut("/{id:guid}", async (Guid id, [FromBody] UpdateBranchRequest request, ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateBranchAsync(id, request, ct)))
            .RequirePermission(Permissions.BranchesManage)
            .WithName("UpdateBranch");

        branchesGroup.MapDelete("/{id:guid}", async (Guid id, ITenantManagementService service, CancellationToken ct) =>
        {
            await service.DeleteBranchAsync(id, ct);
            return Results.NoContent();
        })
        .RequirePermission(Permissions.BranchesManage)
        .WithName("DeleteBranch");

        var rolesGroup = app.MapGroup("/api")
            .WithTags("Roles & Permissions")
            .RequireAuthorization();

        rolesGroup.MapGet("/permissions", async (ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetAvailablePermissionsAsync(ct)))
            .RequirePermission(Permissions.RolesView)
            .WithName("GetAvailablePermissions");

        rolesGroup.MapGet("/roles", async (ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetRolesAsync(ct)))
            .RequirePermission(Permissions.RolesView)
            .WithName("GetRoles");

        rolesGroup.MapGet("/roles/{id:guid}", async (Guid id, ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetRoleByIdAsync(id, ct)))
            .RequirePermission(Permissions.RolesView)
            .WithName("GetRoleById");

        rolesGroup.MapPost("/roles", async ([FromBody] CreateRoleRequest request, ITenantManagementService service, CancellationToken ct) =>
        {
            var created = await service.CreateRoleAsync(request, ct);
            return Results.Created($"/api/roles/{created.Id}", created);
        })
        .RequirePermission(Permissions.RolesManage)
        .WithName("CreateRole");

        rolesGroup.MapPut("/roles/{id:guid}", async (Guid id, [FromBody] UpdateRoleRequest request, ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateRoleAsync(id, request, ct)))
            .RequirePermission(Permissions.RolesManage)
            .WithName("UpdateRole");

        rolesGroup.MapDelete("/roles/{id:guid}", async (Guid id, ITenantManagementService service, CancellationToken ct) =>
        {
            await service.DeleteRoleAsync(id, ct);
            return Results.NoContent();
        })
        .RequirePermission(Permissions.RolesManage)
        .WithName("DeleteRole");

        var usersGroup = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        usersGroup.MapGet("/", async (
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            HttpContext httpContext,
            ITenantManagementService service,
            CancellationToken ct) =>
        {
            var all = await service.GetUsersAsync(ct);
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
        })
            .RequirePermission(Permissions.UsersView)
            .WithName("GetUsers");

        usersGroup.MapGet("/{id:guid}", async (Guid id, ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.GetUserByIdAsync(id, ct)))
            .RequirePermission(Permissions.UsersView)
            .WithName("GetUserById");

        usersGroup.MapPost("/", async ([FromBody] CreateTenantUserRequest request, ITenantManagementService service, CancellationToken ct) =>
        {
            var created = await service.CreateUserAsync(request, ct);
            return Results.Created($"/api/users/{created.Id}", created);
        })
        .RequirePermission(Permissions.UsersManage)
        .WithName("CreateUser");

        usersGroup.MapPut("/{id:guid}/role", async (Guid id, [FromBody] UpdateTenantUserRoleRequest request, ITenantManagementService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateUserRoleAsync(id, request, ct)))
            .RequirePermission(Permissions.UsersManage)
            .WithName("UpdateUserRole");

        usersGroup.MapDelete("/{id:guid}", async (Guid id, ITenantManagementService service, CancellationToken ct) =>
        {
            await service.DeleteUserAsync(id, ct);
            return Results.NoContent();
        })
        .RequirePermission(Permissions.UsersManage)
        .WithName("DeleteUser");

        app.MapGet("/api/payments/online/status", [RequireFeature(FeatureKey.OnlinePayments)] (ITenantContext tenantContext) =>
            Results.Ok(new
            {
                status = "Available",
                tenantId = tenantContext.TenantId
            }))
            .WithTags("Payments")
            .RequireAuthorization()
            .RequireFeature(FeatureKey.OnlinePayments)
            .WithName("GetOnlinePaymentsStatus");

        return app;
    }

    private static Guid RequireTenantId(ITenantContext tenantContext)
    {
        if (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            throw new DomainValidationException("TENANT_REQUIRED", "Tenant context is required.", 400);
        }

        return tenantContext.TenantId.Value;
    }
}
