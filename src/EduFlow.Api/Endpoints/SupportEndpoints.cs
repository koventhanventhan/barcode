using EduFlow.Api.Authorization;
using EduFlow.Application.Support;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public static class SupportEndpoints
{
    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        var tenantGroup = app.MapGroup("/api/support/tickets")
            .WithTags("Support")
            .RequireAuthorization()
            .RequirePermission(Permissions.SettingsManage);

        tenantGroup.MapGet("/", async (
            [FromQuery] SupportTicketStatus? status,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            ISupportTicketService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetTenantTicketsAsync(status, page ?? 1, pageSize ?? 25, ct)))
            .WithName("GetSupportTickets");

        tenantGroup.MapGet("/{id:guid}", async (
            Guid id,
            ISupportTicketService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetTenantTicketByIdAsync(id, ct)))
            .WithName("GetSupportTicketById");

        tenantGroup.MapPost("/", async (
            [FromBody] CreateSupportTicketRequest request,
            ISupportTicketService service,
            CancellationToken ct) =>
        {
            var created = await service.CreateTicketAsync(request, ct);
            return Results.Created($"/api/support/tickets/{created.Id}", created);
        }).WithName("CreateSupportTicket");

        tenantGroup.MapPost("/{id:guid}/reply", async (
            Guid id,
            [FromBody] ReplySupportTicketRequest request,
            ISupportTicketService service,
            CancellationToken ct) =>
            Results.Ok(await service.ReplyAsTenantAsync(id, request, ct)))
            .WithName("ReplySupportTicket");

        tenantGroup.MapPost("/{id:guid}/close", async (
            Guid id,
            ISupportTicketService service,
            CancellationToken ct) =>
            Results.Ok(await service.CloseTenantTicketAsync(id, ct)))
            .WithName("CloseSupportTicket");

        var adminGroup = app.MapGroup("/api/super-admin/support/tickets")
            .WithTags("SuperAdmin Support")
            .RequireSuperAdmin();

        adminGroup.MapGet("/", async (
            [FromQuery] SupportTicketStatus? status,
            [FromQuery] bool? priorityOnly,
            [FromQuery] Guid? tenantId,
            [FromQuery] string? search,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            ISupportTicketService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSuperAdminInboxAsync(
                status,
                priorityOnly,
                tenantId,
                search,
                page ?? 1,
                pageSize ?? 25,
                ct)))
            .WithName("SuperAdminGetSupportInbox");

        adminGroup.MapGet("/{id:guid}", async (
            Guid id,
            ISupportTicketService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSuperAdminTicketByIdAsync(id, ct)))
            .WithName("SuperAdminGetSupportTicketById");

        adminGroup.MapPost("/{id:guid}/reply", async (
            Guid id,
            [FromBody] SuperAdminReplySupportTicketRequest request,
            ISupportTicketService service,
            CancellationToken ct) =>
            Results.Ok(await service.ReplyAsSuperAdminAsync(id, request, ct)))
            .WithName("SuperAdminReplySupportTicket");

        adminGroup.MapPut("/{id:guid}/status", async (
            Guid id,
            [FromBody] UpdateSupportTicketStatusRequest request,
            ISupportTicketService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateStatusAsSuperAdminAsync(id, request, ct)))
            .WithName("SuperAdminUpdateSupportTicketStatus");

        return app;
    }
}
