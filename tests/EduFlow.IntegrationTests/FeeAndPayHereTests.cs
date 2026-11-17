using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Api.Endpoints;
using EduFlow.Application.Academics;
using EduFlow.Application.Auth;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Fees;
using EduFlow.Application.Notifications;
using EduFlow.Application.Students;
using EduFlow.Application.SuperAdmin;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Fees;
using EduFlow.Infrastructure.Notifications;
using EduFlow.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class FeeAndPayHereTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Monthly_invoice_generation_is_idempotent_and_applies_student_concessions()
    {
        var client = factory.CreateClient();
        var ctx = await SeedFeeInstituteAsync(client, "fee-idem", "Growth");

        // Create a 20% scholarship concession for student 2
        using var concReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/concessions",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new UpsertStudentConcessionRequest(
                StudentId: ctx.SecondStudentId,
                Name: "Sibling 20% Scholarship",
                DiscountType: DiscountType.Percentage,
                Value: 20m,
                FeeStructureId: ctx.MonthlyStructureId,
                Reason: "Second sibling enrolled"));
        var concRes = await client.SendAsync(concReq);
        concRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // First generation run for 2026-11
        using var gen1Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices/generate",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new GenerateMonthlyInvoicesRequest(BillingPeriod: "2026-11"));
        var gen1Res = await client.SendAsync(gen1Req);
        gen1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var run1 = (await gen1Res.Content.ReadFromJsonAsync<GenerateInvoicesResultDto>(IntegrationTestHelpers.JsonOptions))!;
        run1.CreatedCount.Should().Be(2);
        run1.SkippedExistingCount.Should().Be(0);
        // Student 1: 4000.00, Student 2: 4000 - 800 (20%) = 3200.00 -> Total 7200.00
        run1.TotalNetAmount.Should().Be(7200.00m);

        // Second generation run for 2026-11 (via API) -> idempotent: 0 created, 2 skipped
        using var gen2Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices/generate",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new GenerateMonthlyInvoicesRequest(BillingPeriod: "2026-11"));
        var run2 = (await (await client.SendAsync(gen2Req)).Content.ReadFromJsonAsync<GenerateInvoicesResultDto>(IntegrationTestHelpers.JsonOptions))!;
        run2.CreatedCount.Should().Be(0);
        run2.SkippedExistingCount.Should().Be(2);

        // Third run via Hangfire job -> still only 2 invoices exist for the tenant
        using (var scope = factory.Services.CreateScope())
        {
            var job = scope.ServiceProvider.GetRequiredService<MonthlyFeeInvoiceHangfireJob>();
            await job.ExecuteAsync("2026-11");
        }

        using var listReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/fees/invoices?billingPeriod=2026-11",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain);
        var page = (await (await client.SendAsync(listReq)).Content.ReadFromJsonAsync<FeeInvoicePageDto>(IntegrationTestHelpers.JsonOptions))!;
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Partial_payments_attachment_receipt_pdf_and_refund_void_math_work_end_to_end()
    {
        var client = factory.CreateClient();
        var ctx = await SeedFeeInstituteAsync(client, "fee-part", "Growth");

        using var singleInvReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new CreateSingleInvoiceRequest(ctx.FirstStudentId, ctx.MonthlyStructureId, "2026-11"));
        var invRes = await client.SendAsync(singleInvReq);
        invRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = (await invRes.Content.ReadFromJsonAsync<FeeInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;
        invoice.NetAmount.Should().Be(4000.00m);
        invoice.Status.Should().Be(nameof(FeeInvoiceStatus.Pending));

        // 1. First partial payment: LKR 1,500.50
        using var pay1Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/payments",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new RecordManualPaymentRequest(invoice.Id, 1500.50m, PaymentMethod.BankTransfer, "SLIP-001", Notes: "First installment"));
        var pay1Res = await client.SendAsync(pay1Req);
        pay1Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var pay1 = (await pay1Res.Content.ReadFromJsonAsync<FeePaymentDto>(IntegrationTestHelpers.JsonOptions))!;
        pay1.ReceiptNumber.Should().Be("RCP-000001");

        // Upload bank slip attachment
        using var multipart = new MultipartFormDataContent();
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04 };
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        multipart.Add(fileContent, "file", "bank-slip.png");

        using var attachReq = new HttpRequestMessage(HttpMethod.Post, $"/api/fees/payments/{pay1.Id}/attachment")
        {
            Content = multipart
        };
        attachReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ctx.Auth.AccessToken);
        attachReq.Headers.Add("X-Tenant", ctx.Tenant.Subdomain);
        var attachRes = await client.SendAsync(attachReq);
        attachRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var pay1WithSlip = (await attachRes.Content.ReadFromJsonAsync<FeePaymentDto>(IntegrationTestHelpers.JsonOptions))!;
        pay1WithSlip.AttachmentUrl.Should().NotBeNullOrWhiteSpace();

        // Check invoice is PartiallyPaid with 2499.50 balance
        var invAfterPay1 = await GetInvoiceAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, invoice.Id);
        invAfterPay1.Status.Should().Be(nameof(FeeInvoiceStatus.PartiallyPaid));
        invAfterPay1.PaidAmount.Should().Be(1500.50m);
        invAfterPay1.BalanceAmount.Should().Be(2499.50m);

        // Overpayment attempt should fail
        using var overpayReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/payments",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new RecordManualPaymentRequest(invoice.Id, 2500.00m, PaymentMethod.Cash));
        (await client.SendAsync(overpayReq)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Second payment for remaining 2499.50 -> marks invoice Paid
        using var pay2Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/payments",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new RecordManualPaymentRequest(invoice.Id, 2499.50m, PaymentMethod.Cash));
        var pay2Res = await client.SendAsync(pay2Req);
        pay2Res.StatusCode.Should().Be(HttpStatusCode.Created);
        var pay2 = (await pay2Res.Content.ReadFromJsonAsync<FeePaymentDto>(IntegrationTestHelpers.JsonOptions))!;
        pay2.ReceiptNumber.Should().Be("RCP-000002");

        var invAfterPay2 = await GetInvoiceAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, invoice.Id);
        invAfterPay2.Status.Should().Be(nameof(FeeInvoiceStatus.Paid));
        invAfterPay2.PaidAmount.Should().Be(4000.00m);
        invAfterPay2.BalanceAmount.Should().Be(0.00m);

        // 3. Download Receipt PDF
        using var receiptReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/fees/payments/{pay2.Id}/receipt",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain);
        var receiptRes = await client.SendAsync(receiptReq);
        receiptRes.StatusCode.Should().Be(HttpStatusCode.OK);
        receiptRes.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var pdfBytes = await receiptRes.Content.ReadAsByteArrayAsync();
        pdfBytes.Length.Should().BeGreaterThan(500);

        // 4. Refund LKR 500.00 from payment 2 -> invoice reverts to PartiallyPaid with balance 500.00
        using var refundReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/fees/payments/{pay2.Id}/refund-or-void",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new RefundOrVoidPaymentRequest("Concession adjustment refund", RefundAmount: 500.00m));
        var refundRes = await client.SendAsync(refundReq);
        refundRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var invAfterRefund = await GetInvoiceAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, invoice.Id);
        invAfterRefund.Status.Should().Be(nameof(FeeInvoiceStatus.PartiallyPaid));
        invAfterRefund.PaidAmount.Should().Be(3500.00m);
        invAfterRefund.BalanceAmount.Should().Be(500.00m);
    }

    [Fact]
    public async Task PayHere_sandbox_checkout_and_webhook_verifies_md5sig_rejects_invalid_handles_replay_and_sends_parent_and_admin_messages()
    {
        var client = factory.CreateClient();
        var ctx = await SeedFeeInstituteAsync(client, "fee-payhere", "Enterprise");

        var smsStore = factory.Services.GetRequiredService<FakeSmsStore>();
        var emailStore = factory.Services.GetRequiredService<InMemoryEmailStore>();
        var paymentGateway = factory.Services.GetRequiredService<IPaymentGateway>();

        // 1. Configure PayHere sandbox credentials (encrypted at rest via Data Protection API)
        const string merchantId = "1219988";
        const string merchantSecret = "SECRET_KEY_FOR_PAYHERE_TEST_2026";
        using var cfgReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/fees/payhere/settings",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new UpdatePayHereGatewaySettingsRequest(
                MerchantId: merchantId,
                MerchantSecret: merchantSecret,
                IsSandbox: true,
                IsEnabled: true));
        var cfgRes = await client.SendAsync(cfgReq);
        cfgRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var cfg = (await cfgRes.Content.ReadFromJsonAsync<PayHereGatewaySettingsDto>(IntegrationTestHelpers.JsonOptions))!;
        cfg.HasMerchantSecret.Should().BeTrue();
        cfg.MaskedMerchantSecret.Should().EndWith("2026");

        // 2. Create invoice & initiate PayHere checkout
        using var invReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new CreateSingleInvoiceRequest(ctx.FirstStudentId, ctx.MonthlyStructureId, "2026-11"));
        var invoice = (await (await client.SendAsync(invReq)).Content.ReadFromJsonAsync<FeeInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;

        using var checkoutReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/payhere/checkout",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new InitiatePayHereCheckoutRequest(invoice.Id));
        var checkoutRes = await client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkout = (await checkoutRes.Content.ReadFromJsonAsync<PayHereCheckoutSessionDto>(IntegrationTestHelpers.JsonOptions))!;
        checkout.OrderId.Should().StartWith("PH-");
        checkout.FormFields["hash"].Should().HaveLength(32);

        // 3. Invalid webhook signature -> rejected with 400 INVALID_WEBHOOK_SIGNATURE and invoice stays Pending
        var invalidWebhookContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["merchant_id"] = merchantId,
            ["order_id"] = checkout.OrderId,
            ["payment_id"] = "PH_PAY_INVALID",
            ["payhere_amount"] = "4000.00",
            ["payhere_currency"] = "LKR",
            ["status_code"] = "2",
            ["md5sig"] = "BAD_MD5_SIGNATURE_00000000000000"
        });
        var invalidRes = await client.PostAsync("/api/fees/payhere/webhook", invalidWebhookContent);
        invalidRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var doc = JsonDocument.Parse(await invalidRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("INVALID_WEBHOOK_SIGNATURE");
        }

        var invAfterInvalid = await GetInvoiceAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, invoice.Id);
        invAfterInvalid.Status.Should().Be(nameof(FeeInvoiceStatus.Pending));

        // 4. Valid webhook signature -> marks invoice Paid, issues sequential receipt, sends Parent + Admin messages
        var validSig = paymentGateway.GenerateWebhookSignature(
            merchantId,
            merchantSecret,
            checkout.OrderId,
            "4000.00",
            "LKR",
            2);

        var validWebhookContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["merchant_id"] = merchantId,
            ["order_id"] = checkout.OrderId,
            ["payment_id"] = "3200991188",
            ["payhere_amount"] = "4000.00",
            ["payhere_currency"] = "LKR",
            ["status_code"] = "2",
            ["md5sig"] = validSig,
            ["method"] = "VISA",
            ["status_message"] = "Successfully completed"
        });
        var validRes = await client.PostAsync("/api/fees/payhere/webhook", validWebhookContent);
        validRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var webhookResult = (await validRes.Content.ReadFromJsonAsync<PayHereWebhookResultDto>(IntegrationTestHelpers.JsonOptions))!;
        webhookResult.WasReplay.Should().BeFalse();
        webhookResult.InvoiceStatus.Should().Be(nameof(FeeInvoiceStatus.Paid));
        webhookResult.ReceiptNumber.Should().Be("RCP-000001");

        // Verify Parent + Admin messages were sent via Fake provider and logged in NotificationOutbox
        using var logReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/notifications/messages?pageSize=50",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain);
        var msgLog = (await (await client.SendAsync(logReq)).Content.ReadFromJsonAsync<NotificationMessageLogResponse>(IntegrationTestHelpers.JsonOptions))!;
        msgLog.Items.Should().Contain(m =>
            m.MessageType == nameof(NotificationMessageType.PaymentReceivedParent)
            && m.Status == nameof(NotificationStatus.Sent)
            && m.Body.Contains("RCP-000001"));
        msgLog.Items.Should().Contain(m =>
            m.MessageType == nameof(NotificationMessageType.PaymentReceivedAdmin)
            && m.Status == nameof(NotificationStatus.Sent)
            && m.Body.Contains("RCP-000001"));

        smsStore.Messages.Where(m => m.TenantId == ctx.Tenant.Id).Should().NotBeEmpty();
        emailStore.Messages.Should().Contain(e => e.HtmlBody.Contains("RCP-000001"));

        // 5. Replay same webhook -> idempotent, WasReplay = true, no second payment created
        var replayWebhookContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["merchant_id"] = merchantId,
            ["order_id"] = checkout.OrderId,
            ["payment_id"] = "3200991188",
            ["payhere_amount"] = "4000.00",
            ["payhere_currency"] = "LKR",
            ["status_code"] = "2",
            ["md5sig"] = validSig
        });
        var replayRes = await client.PostAsync("/api/fees/payhere/webhook", replayWebhookContent);
        replayRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayDto = (await replayRes.Content.ReadFromJsonAsync<PayHereWebhookResultDto>(IntegrationTestHelpers.JsonOptions))!;
        replayDto.WasReplay.Should().BeTrue();
        replayDto.PaymentId.Should().Be(webhookResult.PaymentId);
        replayDto.ReceiptNumber.Should().Be("RCP-000001");

        var invFinal = await GetInvoiceAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, invoice.Id);
        invFinal.Payments.Should().ContainSingle();
        invFinal.PaidAmount.Should().Be(4000.00m);
    }

    [Fact]
    public async Task Fee_reminders_job_sends_before_due_on_due_and_overdue_and_respects_feature_switch()
    {
        var client = factory.CreateClient();
        var ctx = await SeedFeeInstituteAsync(client, "fee-rem", "Starter");

        var dueDate = new DateOnly(2026, 11, 20);
        using var invReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new CreateSingleInvoiceRequest(ctx.FirstStudentId, ctx.MonthlyStructureId, "2026-11", DueDate: dueDate));
        (await client.SendAsync(invReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        // 1. Run 3 days before due date (2026-11-17) -> sends BeforeDue reminder once
        var beforeResult = await RunRemindersAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, new DateOnly(2026, 11, 17));
        beforeResult.BeforeDueSent.Should().Be(1);

        // Repeat on same day -> idempotent, 0 sent
        var beforeRepeat = await RunRemindersAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, new DateOnly(2026, 11, 17));
        beforeRepeat.BeforeDueSent.Should().Be(0);

        // 2. Run on due date (2026-11-20) -> sends OnDueDate reminder
        var onDueResult = await RunRemindersAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, dueDate);
        onDueResult.OnDueSent.Should().Be(1);

        // 3. Turn FeeReminderSms OFF -> running 3 days after due date (2026-11-23) skips sending
        using var disableSwitchReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/FeeReminderSms",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new UpdateFeatureSwitchRequest(IsEnabled: false));
        (await client.SendAsync(disableSwitchReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var overdueWhenOff = await RunRemindersAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, new DateOnly(2026, 11, 23));
        overdueWhenOff.OverdueSent.Should().Be(0);
        overdueWhenOff.SkippedCount.Should().Be(1);

        // 4. Turn FeeReminderSms back ON -> overdue reminder is sent
        using var enableSwitchReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/FeeReminderSms",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new UpdateFeatureSwitchRequest(IsEnabled: true));
        (await client.SendAsync(enableSwitchReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var overdueWhenOn = await RunRemindersAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, new DateOnly(2026, 11, 23));
        overdueWhenOn.OverdueSent.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_payments_allocate_strictly_sequential_unique_receipt_numbers_per_tenant()
    {
        var client = factory.CreateClient();
        var ctx = await SeedFeeInstituteAsync(client, "fee-conc", "Growth");

        using var invReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices",
            ctx.Auth.AccessToken,
            ctx.Tenant.Subdomain,
            new CreateSingleInvoiceRequest(ctx.FirstStudentId, ctx.MonthlyStructureId, "2026-11"));
        var invoice = (await (await client.SendAsync(invReq)).Content.ReadFromJsonAsync<FeeInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;

        // Fire 10 concurrent partial payments of LKR 100.00 each against the 4,000.00 invoice
        const int concurrentCount = 10;
        var tasks = Enumerable.Range(1, concurrentCount).Select(async index =>
        {
            using var req = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Post,
                "/api/fees/payments",
                ctx.Auth.AccessToken,
                ctx.Tenant.Subdomain,
                new RecordManualPaymentRequest(invoice.Id, 100.00m, PaymentMethod.Cash, ReferenceNumber: $"CONC-{index}"));
            var res = await client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
            return (await res.Content.ReadFromJsonAsync<FeePaymentDto>(IntegrationTestHelpers.JsonOptions))!;
        });

        var payments = await Task.WhenAll(tasks);
        payments.Should().HaveCount(concurrentCount);

        var sequences = payments.Select(p => p.ReceiptSequence).OrderBy(s => s).ToList();
        sequences.Should().Equal(Enumerable.Range(1, concurrentCount));

        var receiptNumbers = payments.Select(p => p.ReceiptNumber).OrderBy(r => r).ToList();
        receiptNumbers.Should().Equal(Enumerable.Range(1, concurrentCount).Select(FeeRules.FormatReceiptNumber));

        var updatedInvoice = await GetInvoiceAsync(client, ctx.Auth.AccessToken, ctx.Tenant.Subdomain, invoice.Id);
        updatedInvoice.PaidAmount.Should().Be(1000.00m);
        updatedInvoice.BalanceAmount.Should().Be(3000.00m);
    }

    [Fact]
    public async Task SuperAdmin_manual_platform_invoice_marks_paid_extends_subscription_and_generates_pdf()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "plat-bill", "Pro");

        var initialEndDate = tenant.Subscription!.EndDate!.Value;

        using var createPlatReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tenant.Id}/invoices",
            superAdmin.AccessToken,
            body: new CreatePlatformInvoiceRequest(Amount: 25000.00m, PeriodMonths: 1, DaysToExtend: 30, Notes: "Annual renewal month 1"));
        var createPlatRes = await client.SendAsync(createPlatReq);
        createPlatRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var platInvoice = (await createPlatRes.Content.ReadFromJsonAsync<PlatformSubscriptionInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;
        platInvoice.InvoiceNumber.Should().StartWith("PLAT-");
        platInvoice.Status.Should().Be(nameof(PlatformInvoiceStatus.Pending));

        using var markPaidReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/invoices/{platInvoice.Id}/mark-paid",
            superAdmin.AccessToken,
            body: new MarkPlatformInvoicePaidRequest(PaymentReference: "BANK-TX-8899", Notes: "Verified by finance"));
        var markPaidRes = await client.SendAsync(markPaidReq);
        markPaidRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var paidInvoice = (await markPaidRes.Content.ReadFromJsonAsync<PlatformSubscriptionInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;
        paidInvoice.Status.Should().Be(nameof(PlatformInvoiceStatus.Paid));
        paidInvoice.PaymentReference.Should().Be("BANK-TX-8899");

        // Subscription is now Active and extended by 30 days
        using var detailReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/super-admin/tenants/{tenant.Id}",
            superAdmin.AccessToken);
        var updatedTenant = (await (await client.SendAsync(detailReq)).Content.ReadFromJsonAsync<TenantDetailDto>(IntegrationTestHelpers.JsonOptions))!;
        updatedTenant.Subscription!.Status.Should().Be(nameof(SubscriptionStatus.Active));
        updatedTenant.Subscription.EndDate!.Value.Should().BeAfter(initialEndDate.AddDays(29));

        // Download Platform Subscription Invoice PDF
        using var pdfReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/super-admin/invoices/{platInvoice.Id}/pdf",
            superAdmin.AccessToken);
        var pdfRes = await client.SendAsync(pdfReq);
        pdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        pdfRes.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pdfRes.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(500);
    }

    private static async Task<FeeInvoiceDto> GetInvoiceAsync(HttpClient client, string token, string subdomain, Guid invoiceId)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/fees/invoices/{invoiceId}",
            token,
            subdomain);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<FeeInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<RunFeeRemindersResultDto> RunRemindersAsync(
        HttpClient client,
        string token,
        string subdomain,
        DateOnly dateOverride)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/reminders/run",
            token,
            subdomain,
            new RunFeeRemindersRequest(dateOverride));
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<RunFeeRemindersResultDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<SeededFeeInstitute> SeedFeeInstituteAsync(HttpClient client, string prefix, string planCode)
    {
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, prefix, planCode);
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AdminUser@123!");

        // Disable quiet hours so reminder/payment SMS assertions work at any test wall-clock hour
        using var quietReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/config",
            auth.AccessToken,
            tenant.Subdomain,
            new UpdateNotificationConfigRequest(
                QuietHoursEnabled: false,
                QuietHoursStartMinutes: 21 * 60,
                QuietHoursEndMinutes: 6 * 60,
                FeeReminderDaysBefore: 3,
                FeeReminderOnDueDate: true,
                FeeReminderDaysAfterOverdue: 3,
                ExamReminderDaysBefore: 1));
        (await client.SendAsync(quietReq)).EnsureSuccessStatusCode();

        using var levelsReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/education-levels",
            auth.AccessToken,
            tenant.Subdomain);
        var levels = (await (await client.SendAsync(levelsReq)).Content.ReadFromJsonAsync<List<EducationLevelDto>>(IntegrationTestHelpers.JsonOptions))!;
        var levelId = levels.First(l => l.IsEnabled).Id;

        using var batchReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/batches",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertBatchRequest("2026 O/L Batch", $"B{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}", levelId, null, null, "2026"));
        var batch = (await (await client.SendAsync(batchReq)).Content.ReadFromJsonAsync<BatchDto>(IntegrationTestHelpers.JsonOptions))!;

        var s1 = await CreateStudentAsync(client, auth.AccessToken, tenant.Subdomain, "Nimal Perera", "+94771112233", levelId, batch.Id);
        var s2 = await CreateStudentAsync(client, auth.AccessToken, tenant.Subdomain, "Kavindi Silva", "+94772223344", levelId, batch.Id);

        using var fsReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/structures",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertFeeStructureRequest(
                Name: "Monthly Tuition",
                Frequency: FeeFrequency.Monthly,
                Amount: 4000.00m,
                EducationLevelId: levelId,
                BatchId: batch.Id,
                DueDayOfMonth: 10));
        var fs = (await (await client.SendAsync(fsReq)).Content.ReadFromJsonAsync<FeeStructureDto>(IntegrationTestHelpers.JsonOptions))!;

        return new SeededFeeInstitute(auth, tenant, batch.Id, fs.Id, s1.Id, s2.Id);
    }

    private static async Task<StudentDto> CreateStudentAsync(
        HttpClient client,
        string token,
        string subdomain,
        string name,
        string phone,
        Guid levelId,
        Guid batchId)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            token,
            subdomain,
            new UpsertStudentRequest(name, "Guardian " + name, phone, "Colombo", levelId, BatchIds: [batchId]));
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<StudentDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private sealed record SeededFeeInstitute(
        AuthResponse Auth,
        TenantDetailDto Tenant,
        Guid BatchId,
        Guid MonthlyStructureId,
        Guid FirstStudentId,
        Guid SecondStudentId);
}
