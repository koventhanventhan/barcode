using EduFlow.Api.Authorization;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Notifications;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/settings", async (INotificationService service, CancellationToken ct) =>
            Results.Ok(await service.GetSettingsWorkspaceAsync(ct)))
            .WithName("GetNotificationSettingsWorkspace");

        group.MapPut("/settings/config", async (
            [FromBody] UpdateNotificationConfigRequest request,
            INotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateConfigAsync(request, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("UpdateNotificationConfig");

        group.MapPut("/settings/sender-id", async (
            [FromBody] EduFlow.Application.Notifications.UpdateTenantSmsSenderIdRequest request,
            INotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.UpdateSmsSenderIdAsync(request, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("UpdateTenantSmsSenderId");

        group.MapPut("/settings/types/{messageType}", async (
            string messageType,
            [FromBody] UpdateNotificationTypeSettingRequest request,
            INotificationService service,
            CancellationToken ct) =>
        {
            var parsed = ParseMessageType(messageType);
            return Results.Ok(await service.UpdateTypeSettingAsync(parsed, request, ct));
        })
        .RequirePermission(Permissions.SettingsManage)
        .WithName("UpdateNotificationTypeSetting");

        group.MapGet("/templates", async (
            string? messageType,
            string? language,
            INotificationService service,
            CancellationToken ct) =>
        {
            NotificationMessageType? parsedType = null;
            if (!string.IsNullOrWhiteSpace(messageType))
            {
                parsedType = ParseMessageType(messageType);
            }

            return Results.Ok(await service.GetTemplatesAsync(parsedType, language, ct));
        }).WithName("GetNotificationTemplates");

        group.MapPut("/templates/{messageType}/{language}", async (
            string messageType,
            string language,
            [FromBody] UpdateNotificationTemplateRequest request,
            INotificationService service,
            CancellationToken ct) =>
        {
            var parsed = ParseMessageType(messageType);
            return Results.Ok(await service.UpdateTemplateAsync(parsed, language, request, ct));
        })
        .RequirePermission(Permissions.SettingsManage)
        .WithName("UpdateNotificationTemplate");

        group.MapPost("/templates/{messageType}/{language}/reset", async (
            string messageType,
            string language,
            INotificationService service,
            CancellationToken ct) =>
        {
            var parsed = ParseMessageType(messageType);
            return Results.Ok(await service.ResetTemplateAsync(parsed, language, ct));
        })
        .RequirePermission(Permissions.SettingsManage)
        .WithName("ResetNotificationTemplate");

        group.MapPost("/preview", (
            [FromBody] PreviewTemplateRequest request,
            INotificationService service) =>
            Results.Ok(service.PreviewTemplate(request)))
            .WithName("PreviewNotificationTemplate");

        group.MapGet("/quota", async (INotificationService service, CancellationToken ct) =>
            Results.Ok(await service.GetSmsQuotaAsync(cancellationToken: ct)))
            .WithName("GetSmsQuotaSummary");

        group.MapGet("/messages", async (
            string? messageType,
            string? channel,
            string? status,
            DateOnly? fromDate,
            DateOnly? toDate,
            Guid? studentId,
            string? search,
            int? page,
            int? pageSize,
            INotificationService service,
            CancellationToken ct) =>
        {
            NotificationMessageType? parsedType = !string.IsNullOrWhiteSpace(messageType)
                ? ParseMessageType(messageType)
                : null;
            NotificationChannel? parsedChannel = !string.IsNullOrWhiteSpace(channel)
                ? ParseChannel(channel)
                : null;
            NotificationStatus? parsedStatus = !string.IsNullOrWhiteSpace(status)
                ? ParseStatus(status)
                : null;

            var filter = new NotificationMessageFilter(
                MessageType: parsedType,
                Channel: parsedChannel,
                Status: parsedStatus,
                FromDate: fromDate,
                ToDate: toDate,
                StudentId: studentId,
                Search: search,
                Page: page ?? 1,
                PageSize: pageSize ?? 50);

            return Results.Ok(await service.GetMessageLogAsync(filter, ct));
        }).WithName("GetNotificationMessageLog");

        group.MapPost("/messages/{id:guid}/retry", async (
            Guid id,
            INotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.RetryMessageAsync(id, cancellationToken: ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("RetryNotificationMessage");

        group.MapPost("/test-send", async (
            [FromBody] TestSendNotificationRequest request,
            INotificationService service,
            CancellationToken ct) =>
            Results.Ok(await service.TriggerTestSendAsync(request, cancellationToken: ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("TriggerTestNotification");

        group.MapPost("/dispatch", async (
            ITenantContext tenantContext,
            INotificationDispatcher dispatcher,
            CancellationToken ct) =>
            Results.Ok(await dispatcher.DispatchPendingAsync(
                tenantIdFilter: tenantContext.TenantId,
                cancellationToken: ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("DispatchPendingNotifications");

        return app;
    }

    private static NotificationMessageType ParseMessageType(string raw)
    {
        if (!Enum.TryParse<NotificationMessageType>(raw, ignoreCase: true, out var parsed))
        {
            throw new DomainValidationException("INVALID_MESSAGE_TYPE", $"Unknown notification message type '{raw}'.");
        }

        return parsed;
    }

    private static NotificationChannel ParseChannel(string raw)
    {
        if (!Enum.TryParse<NotificationChannel>(raw, ignoreCase: true, out var parsed))
        {
            throw new DomainValidationException("INVALID_CHANNEL", $"Unknown notification channel '{raw}'.");
        }

        return parsed;
    }

    private static NotificationStatus ParseStatus(string raw)
    {
        if (!Enum.TryParse<NotificationStatus>(raw, ignoreCase: true, out var parsed))
        {
            throw new DomainValidationException("INVALID_STATUS", $"Unknown notification status '{raw}'.");
        }

        return parsed;
    }
}
