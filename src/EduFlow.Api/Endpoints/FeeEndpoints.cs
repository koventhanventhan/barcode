using System.Globalization;
using EduFlow.Api.Authorization;
using EduFlow.Api.Filters;
using EduFlow.Application.Academics;
using EduFlow.Application.Fees;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace EduFlow.Api.Endpoints;

public static class FeeEndpoints
{
    public static IEndpointRouteBuilder MapFeeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fees")
            .WithTags("Fees")
            .RequireAuthorization();

        group.MapGet("/structures", async (IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.GetFeeStructuresAsync(ct)))
            .RequirePermission(Permissions.FeesView)
            .WithName("GetFeeStructures");

        group.MapPost("/structures", async ([FromBody] UpsertFeeStructureRequest request, IFeeService service, CancellationToken ct) =>
        {
            var created = await service.CreateFeeStructureAsync(request, ct);
            return Results.Created($"/api/fees/structures/{created.Id}", created);
        })
        .RequirePermission(Permissions.FeesCollect)
        .WithName("CreateFeeStructure");

        group.MapPut("/structures/{id:guid}", async (Guid id, [FromBody] UpsertFeeStructureRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateFeeStructureAsync(id, request, ct)))
            .RequirePermission(Permissions.FeesCollect)
            .WithName("UpdateFeeStructure");

        group.MapDelete("/structures/{id:guid}", async (Guid id, IFeeService service, CancellationToken ct) =>
        {
            await service.DeleteFeeStructureAsync(id, ct);
            return Results.NoContent();
        })
        .RequirePermission(Permissions.FeesCollect)
        .WithName("DeleteFeeStructure");

        group.MapGet("/concessions", async ([FromQuery] Guid? studentId, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.GetConcessionsAsync(studentId, ct)))
            .RequirePermission(Permissions.FeesView)
            .WithName("GetStudentConcessions");

        group.MapPost("/concessions", async ([FromBody] UpsertStudentConcessionRequest request, IFeeService service, CancellationToken ct) =>
        {
            var created = await service.CreateConcessionAsync(request, ct);
            return Results.Created($"/api/fees/concessions/{created.Id}", created);
        })
        .RequirePermission(Permissions.FeesCollect)
        .WithName("CreateStudentConcession");

        group.MapPut("/concessions/{id:guid}", async (Guid id, [FromBody] UpsertStudentConcessionRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.UpdateConcessionAsync(id, request, ct)))
            .RequirePermission(Permissions.FeesCollect)
            .WithName("UpdateStudentConcession");

        group.MapDelete("/concessions/{id:guid}", async (Guid id, IFeeService service, CancellationToken ct) =>
        {
            await service.DeleteConcessionAsync(id, ct);
            return Results.NoContent();
        })
        .RequirePermission(Permissions.FeesCollect)
        .WithName("DeleteStudentConcession");

        group.MapGet("/invoices", async (
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] Guid? batchId,
            [FromQuery] Guid? studentId,
            [FromQuery] string? billingPeriod,
            [FromQuery] bool? overdueOnly,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            IFeeService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetInvoicesAsync(
                new FeeInvoiceListQuery(search, status, batchId, studentId, billingPeriod, overdueOnly, page ?? 1, pageSize ?? 50),
                ct)))
            .RequirePermission(Permissions.FeesView)
            .WithName("GetFeeInvoices");

        group.MapGet("/invoices/{id:guid}", async (Guid id, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.GetInvoiceAsync(id, ct)))
            .RequirePermission(Permissions.FeesView)
            .WithName("GetFeeInvoiceById");

        group.MapPost("/invoices", async ([FromBody] CreateSingleInvoiceRequest request, IFeeService service, CancellationToken ct) =>
        {
            var created = await service.CreateSingleInvoiceAsync(request, ct);
            return Results.Created($"/api/fees/invoices/{created.Id}", created);
        })
        .RequirePermission(Permissions.FeesCollect)
        .WithName("CreateSingleFeeInvoice");

        group.MapPost("/invoices/generate", async ([FromBody] GenerateMonthlyInvoicesRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.GenerateMonthlyInvoicesAsync(request, tenantIdOverride: null, ct)))
            .RequirePermission(Permissions.FeesCollect)
            .WithName("GenerateMonthlyFeeInvoices");

        group.MapPost("/invoices/{id:guid}/void", async (Guid id, [FromBody] VoidInvoiceRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.VoidInvoiceAsync(id, request, ct)))
            .RequirePermission(Permissions.FeesCollect)
            .WithName("VoidFeeInvoice");

        group.MapGet("/payments", async (
            [FromQuery] Guid? invoiceId,
            [FromQuery] Guid? studentId,
            [FromQuery] int? take,
            IFeeService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetPaymentsAsync(invoiceId, studentId, take ?? 100, ct)))
            .RequirePermission(Permissions.FeesView)
            .WithName("GetFeePayments");

        group.MapPost("/payments", async ([FromBody] RecordManualPaymentRequest request, IFeeService service, CancellationToken ct) =>
        {
            var created = await service.RecordManualPaymentAsync(request, ct);
            return Results.Created($"/api/fees/payments/{created.Id}", created);
        })
        .RequirePermission(Permissions.FeesCollect)
        .WithName("RecordManualFeePayment");

        group.MapPost("/payments/{id:guid}/attachment", async (Guid id, IFormFile file, IFeeService service, CancellationToken ct) =>
        {
            await using var stream = file.OpenReadStream();
            return Results.Ok(await service.UploadPaymentAttachmentAsync(id, stream, file.FileName, file.ContentType, file.Length, ct));
        })
        .RequirePermission(Permissions.FeesCollect)
        .DisableAntiforgery()
        .WithName("UploadFeePaymentAttachment");

        group.MapPost("/payments/{id:guid}/refund-or-void", async (Guid id, [FromBody] RefundOrVoidPaymentRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.RefundOrVoidPaymentAsync(id, request, ct)))
            .RequirePermission(Permissions.FeesCollect)
            .WithName("RefundOrVoidFeePayment");

        group.MapGet("/payments/{id:guid}/receipt", async (Guid id, IFeeService service, CancellationToken ct) =>
            ToFileResult(await service.GetReceiptPdfAsync(id, ct)))
            .RequirePermission(Permissions.FeesView)
            .WithName("GetFeePaymentReceiptPdf");

        group.MapPost("/reminders/run", async ([FromBody] RunFeeRemindersRequest? request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.ProcessFeeRemindersAsync(request?.DateOverride, tenantIdOverride: null, ct)))
            .RequirePermission(Permissions.FeesCollect)
            .WithName("RunFeeReminders");

        group.MapGet("/dashboard", async (
            [FromQuery] string? billingPeriod,
            [FromQuery] Guid? batchId,
            [FromQuery] DateOnly? asOfDate,
            IFeeService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetDashboardAsync(new FeeDashboardQuery(billingPeriod, batchId, asOfDate), ct)))
            .RequirePermission(Permissions.FeesView)
            .WithName("GetFeeDashboard");

        group.MapGet("/export", async (
            [FromQuery] string? format,
            [FromQuery] string? search,
            [FromQuery] string? status,
            [FromQuery] Guid? batchId,
            [FromQuery] Guid? studentId,
            [FromQuery] string? billingPeriod,
            [FromQuery] bool? overdueOnly,
            IFeeService service,
            CancellationToken ct) =>
            ToFileResult(await service.ExportFeeReportAsync(
                format,
                new FeeInvoiceListQuery(search, status, batchId, studentId, billingPeriod, overdueOnly),
                ct)))
            .RequirePermission(Permissions.FeesView)
            .RequireFeature(FeatureKey.DataExport)
            .WithName("ExportFeeReport");

        group.MapGet("/payhere/settings", async (IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.GetPayHereSettingsAsync(ct)))
            .RequirePermission(Permissions.SettingsManage)
            .WithName("GetPayHereGatewaySettings");

        group.MapPut("/payhere/settings", async ([FromBody] UpdatePayHereGatewaySettingsRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.UpdatePayHereSettingsAsync(request, ct)))
            .RequirePermission(Permissions.SettingsManage)
            .RequireFeature(FeatureKey.OnlinePayments)
            .WithName("UpdatePayHereGatewaySettings");

        group.MapPost("/payhere/checkout", async ([FromBody] InitiatePayHereCheckoutRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.InitiatePayHereCheckoutAsync(request, ct)))
            .RequireFeature(FeatureKey.OnlinePayments)
            .WithName("InitiatePayHereCheckout");

        app.MapGet("/api/fees/payhere/orders/{orderId}", async (string orderId, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.GetPayHereOrderStatusAsync(orderId, ct)))
            .WithTags("Fees")
            .WithName("GetPayHereOrderStatus");

        app.MapGet("/api/fees/payhere/orders/{orderId}/receipt", async (string orderId, IFeeService service, CancellationToken ct) =>
            ToFileResult(await service.GetPayHereOrderReceiptPdfAsync(orderId, ct)))
            .WithTags("Fees")
            .WithName("GetPayHereOrderReceiptPdf");

        app.MapPost("/api/fees/payhere/sandbox-complete", async ([FromBody] SimulatePayHereSandboxRequest request, IFeeService service, CancellationToken ct) =>
            Results.Ok(await service.SimulatePayHereSandboxAsync(request, ct)))
            .WithTags("Fees")
            .WithName("SimulatePayHereSandboxComplete");

        app.MapPost("/api/fees/payhere/webhook", HandlePayHereWebhookAsync)
            .DisableAntiforgery()
            .WithTags("Fees")
            .WithName("ProcessPayHereWebhook");

        app.MapPost("/api/payments/payhere/webhook", HandlePayHereWebhookAsync)
            .DisableAntiforgery()
            .WithTags("Payments")
            .WithName("ProcessPayHereWebhookAlias");

        return app;
    }

    private static async Task<IResult> HandlePayHereWebhookAsync(
        HttpContext httpContext,
        IFeeService feeService,
        CancellationToken ct)
    {
        PayHereWebhookPayload? payload;

        if (httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync(ct);
            _ = int.TryParse(form["status_code"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var statusCode);
            payload = new PayHereWebhookPayload(
                MerchantId: form["merchant_id"].ToString(),
                OrderId: form["order_id"].ToString(),
                PaymentId: form["payment_id"].ToString(),
                PayHereAmount: form["payhere_amount"].ToString(),
                PayHereCurrency: form["payhere_currency"].ToString(),
                StatusCode: statusCode,
                Md5Sig: form["md5sig"].ToString(),
                Method: form["method"].ToString(),
                StatusMessage: form["status_message"].ToString(),
                Custom1: form["custom_1"].ToString(),
                Custom2: form["custom_2"].ToString());
        }
        else
        {
            payload = await httpContext.Request.ReadFromJsonAsync<PayHereWebhookPayload>(cancellationToken: ct);
        }

        if (payload is null)
        {
            throw new DomainValidationException("INVALID_WEBHOOK_PAYLOAD", "PayHere webhook payload is required.");
        }

        var result = await feeService.ProcessPayHereWebhookAsync(payload, ct);
        return Results.Ok(result);
    }

    private static IResult ToFileResult(StoredDownload download) =>
        Results.File(download.Content, download.ContentType, download.FileName);
}
