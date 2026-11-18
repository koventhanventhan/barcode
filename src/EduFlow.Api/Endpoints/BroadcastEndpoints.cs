using EduFlow.Api.Authorization;
using EduFlow.Api.Filters;
using EduFlow.Application.Broadcasts;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;

namespace EduFlow.Api.Endpoints;

public static class BroadcastEndpoints
{
    public static IEndpointRouteBuilder MapBroadcastEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/broadcasts")
            .WithTags("Broadcasts")
            .RequireAuthorization()
            .RequirePermission(Permissions.BroadcastsSend)
            .RequireFeature(FeatureKey.BroadcastMessages);

        group.MapGet("/sms-status", async (
            IBroadcastService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSmsStatusAsync(ct)))
            .WithName("GetBroadcastSmsStatus");

        group.MapPost("/preview", async (
            BroadcastComposeRequest request,
            IBroadcastService service,
            CancellationToken ct) =>
            Results.Ok(await service.PreviewAsync(request, ct)))
            .WithName("PreviewBroadcast");

        group.MapPost("/", async (
            BroadcastComposeRequest request,
            IBroadcastService service,
            CancellationToken ct) =>
            Results.Ok(await service.SendAsync(request, ct)))
            .WithName("SendBroadcast");

        group.MapGet("/", async (
            int? page,
            int? pageSize,
            IBroadcastService service,
            CancellationToken ct) =>
            Results.Ok(await service.ListAsync(page ?? 1, pageSize ?? 20, ct)))
            .WithName("ListBroadcasts");

        group.MapGet("/{id:guid}", async (
            Guid id,
            IBroadcastService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetAsync(id, ct)))
            .WithName("GetBroadcast");

        app.MapGet("/api/storage/usage", async (
            ITenantContext tenantContext,
            ILimitService limits,
            CancellationToken ct) =>
        {
            var tenantId = tenantContext.TenantId ?? Guid.Empty;
            if (tenantId == Guid.Empty)
            {
                return Results.BadRequest(new { code = "TENANT_REQUIRED", message = "Open an institute before checking storage." });
            }

            return Results.Ok(await limits.GetStorageUsageAsync(tenantId, ct));
        })
            .RequireAuthorization()
            .RequirePermission(Permissions.SettingsManage)
            .WithTags("Storage")
            .WithName("GetStorageUsage");

        return app;
    }
}
