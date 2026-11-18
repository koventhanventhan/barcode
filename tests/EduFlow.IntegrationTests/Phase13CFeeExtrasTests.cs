using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduFlow.Application.Academics;
using EduFlow.Application.Auth;
using EduFlow.Application.Fees;
using EduFlow.Application.Notifications;
using EduFlow.Application.Parents;
using EduFlow.Application.Students;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Notifications;
using EduFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class Phase13CFeeExtrasTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04];

    [Fact]
    public async Task Bank_payment_slip_approval_and_rejection_flow_updates_invoice_receipt_and_messages()
    {
        var client = factory.CreateClient();
        var ctx = await SeedAsync(client, "13c-bank", "Growth");

        // 1. Admin configures bank account details in Settings
        using var bankReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/bank-account",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new UpdateTenantBankAccountRequest(
                BankName: "Commercial Bank of Ceylon",
                AccountName: "Admino Tuition Academy",
                AccountNumber: "1000998877",
                BranchName: "Colombo 07",
                Instructions: "Put student code in remarks",
                IsEnabled: true));
        var bankRes = await client.SendAsync(bankReq);
        bankRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Create parent linked to Student 1 and verify parent sees bank details
        var parentAuth = await CreateAndLoginParentAsync(client, ctx, "0779001001", [ctx.Student1.Id]);
        using var parentBankReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/parent/bank-account",
            parentAuth.AccessToken,
            ctx.Tenant.Subdomain);
        var parentBank = await (await client.SendAsync(parentBankReq)).Content.ReadFromJsonAsync<TenantBankAccountDto>(IntegrationTestHelpers.JsonOptions);
        parentBank!.AccountNumber.Should().Be("1000998877");
        parentBank.BankName.Should().Be("Commercial Bank of Ceylon");

        // 3. Create invoice for Student 1 (LKR 4000.00)
        var invoice = await CreateSingleInvoiceAsync(client, ctx, ctx.Student1.Id, ctx.MonthlyStructureId, "2026-11");

        // 4. Parent uploads a bank slip (Pending)
        var rejectedSubmission = await UploadParentBankSlipAsync(
            client,
            ctx.Tenant.Subdomain,
            parentAuth.AccessToken,
            ctx.Student1.Id,
            invoice.Id,
            4000.00m,
            "SLIP-BAD-001",
            "Blurry slip");
        rejectedSubmission.Status.Should().Be("Pending");

        // Invoice must still be Pending with 0 paid before approval
        var invBefore = await GetInvoiceAsync(client, ctx, invoice.Id);
        invBefore.Status.Should().Be(nameof(FeeInvoiceStatus.Pending));
        invBefore.PaidAmount.Should().Be(0m);

        // 5. Admin rejects with reason -> invoice stays Pending, no receipt created
        using var rejectReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/fees/bank-slips/{rejectedSubmission.Id}/reject",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new RejectBankPaymentRequest("Slip reference number is illegible"));
        var rejectRes = await client.SendAsync(rejectReq);
        rejectRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejectedDto = (await rejectRes.Content.ReadFromJsonAsync<BankPaymentSubmissionDto>(IntegrationTestHelpers.JsonOptions))!;
        rejectedDto.Status.Should().Be("Rejected");
        rejectedDto.RejectionReason.Should().Be("Slip reference number is illegible");
        rejectedDto.FeePaymentId.Should().BeNull();

        // 6. Parent uploads a clear slip and Admin approves -> invoice Paid, receipt issued, SMS sent
        var validSubmission = await UploadParentBankSlipAsync(
            client,
            ctx.Tenant.Subdomain,
            parentAuth.AccessToken,
            ctx.Student1.Id,
            invoice.Id,
            4000.00m,
            "SLIP-GOOD-002",
            "Clear transfer slip");

        using var approveReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/fees/bank-slips/{validSubmission.Id}/approve",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new ApproveBankPaymentRequest("Verified on bank statement"));
        var approveRes = await client.SendAsync(approveReq);
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = (await approveRes.Content.ReadFromJsonAsync<BankPaymentSubmissionDto>(IntegrationTestHelpers.JsonOptions))!;
        approved.Status.Should().Be("Approved");
        approved.ReceiptNumber.Should().Be("RCP-000001");
        approved.FeePaymentId.Should().NotBeNull();

        var invAfter = await GetInvoiceAsync(client, ctx, invoice.Id);
        invAfter.Status.Should().Be(nameof(FeeInvoiceStatus.Paid));
        invAfter.PaidAmount.Should().Be(4000.00m);
        invAfter.BalanceAmount.Should().Be(0.00m);

        // Parent can download receipt PDF & view slip file
        using var parentReceiptReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/parent/children/{ctx.Student1.Id}/payments/{approved.FeePaymentId}/receipt",
            parentAuth.AccessToken,
            ctx.Tenant.Subdomain);
        var parentReceiptRes = await client.SendAsync(parentReceiptReq);
        parentReceiptRes.StatusCode.Should().Be(HttpStatusCode.OK);
        parentReceiptRes.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        // Payment confirmation SMS messages dispatched
        using var scope = factory.Services.CreateScope();
        var sms = scope.ServiceProvider.GetRequiredService<FakeSmsStore>();
        sms.Messages.Should().Contain(m => m.TenantId == ctx.Tenant.Id && m.Message.Contains("RCP-000001"));
    }

    [Fact]
    public async Task Credit_balance_applies_to_next_invoices_with_exact_decimal_math_and_restores_on_void()
    {
        var client = factory.CreateClient();
        var ctx = await SeedAsync(client, "13c-cred", "Growth");

        // 1. Record advance credit of LKR 5,000.50 for Student 1
        using var creditReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/credits",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new RecordStudentCreditRequest(ctx.Student1.Id, 5000.50m, PaymentMethod.Cash, "ADV-001", "Term advance"));
        var creditRes = await client.SendAsync(creditReq);
        creditRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. First invoice (4,000.00) -> auto-applies 4,000.00 -> Paid, 1,000.50 credit remaining
        var inv1 = await CreateSingleInvoiceAsync(client, ctx, ctx.Student1.Id, ctx.MonthlyStructureId, "2026-11");
        inv1.CreditAppliedAmount.Should().Be(4000.00m);
        inv1.BalanceAmount.Should().Be(0.00m);
        inv1.Status.Should().Be(nameof(FeeInvoiceStatus.Paid));

        var summaryAfter1 = await GetCreditSummaryAsync(client, ctx, ctx.Student1.Id);
        summaryAfter1.Balance.Should().Be(1000.50m);

        // 3. Second invoice (4,000.00) -> auto-applies remaining 1,000.50 -> PartiallyPaid, balance 2,999.50
        var inv2 = await CreateSingleInvoiceAsync(client, ctx, ctx.Student1.Id, ctx.MonthlyStructureId, "2026-12");
        inv2.CreditAppliedAmount.Should().Be(1000.50m);
        inv2.BalanceAmount.Should().Be(2999.50m);
        inv2.Status.Should().Be(nameof(FeeInvoiceStatus.PartiallyPaid));

        var summaryAfter2 = await GetCreditSummaryAsync(client, ctx, ctx.Student1.Id);
        summaryAfter2.Balance.Should().Be(0.00m);

        // 4. Void inv2 (no cash paid yet) -> restores 1,000.50 back to student credit balance
        using var voidReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/fees/invoices/{inv2.Id}/void",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new VoidInvoiceRequest("Billed in error"));
        var voidRes = await client.SendAsync(voidReq);
        voidRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var summaryAfterVoid = await GetCreditSummaryAsync(client, ctx, ctx.Student1.Id);
        summaryAfterVoid.Balance.Should().Be(1000.50m);

        // 5. Pay inv3 with ApplyExcessToCredit=true (pay 5,000.00 on 4,000.00 invoice when credit is 1,000.50):
        // inv3 takes 1,000.50 credit -> balance 2,999.50; paying 3,500.00 cash settles 2,999.50 + 500.50 excess credit!
        var inv3 = await CreateSingleInvoiceAsync(client, ctx, ctx.Student1.Id, ctx.MonthlyStructureId, "2027-01");
        inv3.BalanceAmount.Should().Be(2999.50m);

        using var payReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/payments",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new RecordManualPaymentRequest(inv3.Id, 3500.00m, PaymentMethod.Cash, "CSH-EX", ApplyExcessToCredit: true));
        var payRes = await client.SendAsync(payReq);
        payRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = (await payRes.Content.ReadFromJsonAsync<FeePaymentDto>(IntegrationTestHelpers.JsonOptions))!;
        payment.Amount.Should().Be(2999.50m);

        var summaryAfterExcess = await GetCreditSummaryAsync(client, ctx, ctx.Student1.Id);
        summaryAfterExcess.Balance.Should().Be(500.50m);
    }

    [Fact]
    public async Task Sibling_discount_rule_and_admission_fee_auto_issue_apply_correctly()
    {
        var client = factory.CreateClient();
        var ctx = await SeedAsync(client, "13c-sib", "Growth");

        // 1. Configure institute-wide Admission/Registration fee with AutoIssueOnAdmission=true
        using var admFsReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/structures",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new UpsertFeeStructureRequest(
                Name: "Registration & Admission Fee",
                Frequency: FeeFrequency.Admission,
                Amount: 2500.00m,
                AutoIssueOnAdmission: true));
        var admFsRes = await client.SendAsync(admFsReq);
        admFsRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 2. Enable Sibling Discount Rule: 10% for 2nd child, 20% for 3rd+ child (Monthly only)
        using var sibRuleReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/fees/sibling-discount",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new UpdateSiblingDiscountRuleRequest(
                IsEnabled: true,
                DiscountType: DiscountType.Percentage,
                SecondChildValue: 10m,
                ThirdPlusChildValue: 20m,
                ApplyToMonthly: true,
                ApplyToOneTime: false,
                ApplyToAdmission: false,
                StackWithConcession: false));
        (await client.SendAsync(sibRuleReq)).EnsureSuccessStatusCode();

        // 3. Admit 3 siblings sharing the same guardian phone (+94775551122) -> auto-issues Admission invoices!
        var sib1 = await CreateStudentAsync(client, ctx.AdminAuth.AccessToken, ctx.Tenant.Subdomain, "Sibling One", "+94775551122", ctx.LevelId, ctx.BatchId, new DateOnly(2025, 1, 5));
        var sib2 = await CreateStudentAsync(client, ctx.AdminAuth.AccessToken, ctx.Tenant.Subdomain, "Sibling Two", "0775551122", ctx.LevelId, ctx.BatchId, new DateOnly(2026, 1, 5));
        var sib3 = await CreateStudentAsync(client, ctx.AdminAuth.AccessToken, ctx.Tenant.Subdomain, "Sibling Three", "+94775551122", ctx.LevelId, ctx.BatchId, new DateOnly(2026, 6, 5));

        // Check admission invoices auto-issued on creation
        using var admInvReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/fees/invoices?billingPeriod=ADMISSION",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain);
        var admPage = (await (await client.SendAsync(admInvReq)).Content.ReadFromJsonAsync<FeeInvoicePageDto>(IntegrationTestHelpers.JsonOptions))!;
        admPage.Items.Should().Contain(i => i.StudentId == sib1.Id && i.NetAmount == 2500.00m && i.Frequency == "Admission");
        admPage.Items.Should().Contain(i => i.StudentId == sib2.Id && i.NetAmount == 2500.00m);
        admPage.Items.Should().Contain(i => i.StudentId == sib3.Id && i.NetAmount == 2500.00m);

        // 4. Generate monthly invoices for 2026-11 -> 1st sibling 4000, 2nd 3600 (10% off), 3rd 3200 (20% off)
        using var genReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices/generate",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new GenerateMonthlyInvoicesRequest("2026-11"));
        var genRes = await client.SendAsync(genReq);
        genRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await genRes.Content.ReadFromJsonAsync<GenerateInvoicesResultDto>(IntegrationTestHelpers.JsonOptions))!;

        var invSib1 = result.CreatedInvoices.Single(i => i.StudentId == sib1.Id);
        var invSib2 = result.CreatedInvoices.Single(i => i.StudentId == sib2.Id);
        var invSib3 = result.CreatedInvoices.Single(i => i.StudentId == sib3.Id);

        invSib1.DiscountAmount.Should().Be(0.00m);
        invSib1.NetAmount.Should().Be(4000.00m);

        invSib2.DiscountAmount.Should().Be(400.00m);
        invSib2.NetAmount.Should().Be(3600.00m);
        invSib2.ConcessionDescription.Should().Contain("Sibling Discount");

        invSib3.DiscountAmount.Should().Be(800.00m);
        invSib3.NetAmount.Should().Be(3200.00m);
        invSib3.ConcessionDescription.Should().Contain("Sibling Discount");
    }

    [Fact]
    public async Task Receipt_reprint_and_daily_cashier_closing_report_match_users_and_methods()
    {
        var client = factory.CreateClient();
        var ctx = await SeedAsync(client, "13c-close", "Growth");

        // Create a second cashier staff user with fees.collect + fees.view permissions
        var cashier2 = await CreateStaffCashierAsync(client, ctx, "Cashier Two", $"cashier2@{ctx.Tenant.Subdomain}.lk");

        var inv1 = await CreateSingleInvoiceAsync(client, ctx, ctx.Student1.Id, ctx.MonthlyStructureId, "2026-11");
        var inv2 = await CreateSingleInvoiceAsync(client, ctx, ctx.Student2.Id, ctx.MonthlyStructureId, "2026-11");

        // Admin records Cash payment of 2,500.25 on inv1
        using var pay1Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/payments",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new RecordManualPaymentRequest(inv1.Id, 2500.25m, PaymentMethod.Cash, "C-01"));
        var pay1 = (await (await client.SendAsync(pay1Req)).Content.ReadFromJsonAsync<FeePaymentDto>(IntegrationTestHelpers.JsonOptions))!;

        // Cashier 2 records BankTransfer of 1,500.75 on inv2 and Cash advance credit of 500.00 for Student 2
        using var pay2Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/payments",
            cashier2.AccessToken,
            ctx.Tenant.Subdomain,
            new RecordManualPaymentRequest(inv2.Id, 1500.75m, PaymentMethod.BankTransfer, "B-02"));
        var pay2Res = await client.SendAsync(pay2Req);
        pay2Res.StatusCode.Should().Be(HttpStatusCode.Created);

        using var creditReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/credits",
            cashier2.AccessToken,
            ctx.Tenant.Subdomain,
            new RecordStudentCreditRequest(ctx.Student2.Id, 500.00m, PaymentMethod.Cash, "ADV-C2"));
        (await client.SendAsync(creditReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Reprint receipt for pay1 twice -> print count increments & audit log written
        using var reprint1Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/fees/payments/{pay1.Id}/receipt?reprint=true",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain);
        var reprint1Res = await client.SendAsync(reprint1Req);
        reprint1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        reprint1Res.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        using var reprint2Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/fees/payments/{pay1.Id}/reprint",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain);
        (await client.SendAsync(reprint2Req)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var payGetReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/fees/payments?invoiceId={inv1.Id}",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain);
        var paymentsList = (await (await client.SendAsync(payGetReq)).Content.ReadFromJsonAsync<List<FeePaymentDto>>(IntegrationTestHelpers.JsonOptions))!;
        paymentsList.Single(p => p.Id == pay1.Id).ReceiptPrintCount.Should().Be(2);

        // Daily Cashier Closing Report for Colombo today
        using var closingReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/fees/reports/cashier-closing",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain);
        var closingRes = await client.SendAsync(closingReq);
        closingRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await closingRes.Content.ReadFromJsonAsync<CashierClosingReportDto>(IntegrationTestHelpers.JsonOptions))!;

        report.InvoicePaymentsTotal.Should().Be(4001.00m);
        report.AdvanceDepositsTotal.Should().Be(500.00m);
        report.GrandTotalCollected.Should().Be(4501.00m);
        report.TotalTransactions.Should().Be(3);

        var cashRow = report.ByMethod.Single(m => m.Method == "Cash");
        cashRow.InvoicePaymentsTotal.Should().Be(2500.25m);
        cashRow.AdvanceDepositsTotal.Should().Be(500.00m);
        cashRow.NetCollectedTotal.Should().Be(3000.25m);

        var bankRow = report.ByMethod.Single(m => m.Method == "BankTransfer");
        bankRow.InvoicePaymentsTotal.Should().Be(1500.75m);
        bankRow.NetCollectedTotal.Should().Be(1500.75m);

        report.ByUser.Should().HaveCount(2);
        var adminClosing = report.ByUser.Single(u => u.CashTotal == 2500.25m);
        adminClosing.NetCollectedTotal.Should().Be(2500.25m);

        var cashier2Closing = report.ByUser.Single(u => u.BankTransferTotal == 1500.75m);
        cashier2Closing.AdvanceDepositsTotal.Should().Be(500.00m);
        cashier2Closing.NetCollectedTotal.Should().Be(2000.75m);

        // Export closing report PDF & CSV
        using var exportPdfReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/fees/reports/cashier-closing/export?format=pdf",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain);
        var exportPdfRes = await client.SendAsync(exportPdfReq);
        exportPdfRes.StatusCode.Should().Be(HttpStatusCode.OK);
        exportPdfRes.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        using var exportCsvReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/fees/reports/cashier-closing/export?format=csv",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain);
        var exportCsvRes = await client.SendAsync(exportCsvReq);
        exportCsvRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await exportCsvRes.Content.ReadAsStringAsync()).Should().Contain("RCP-000001");
    }

    private static async Task<SeedContext> SeedAsync(HttpClient client, string prefix, string planCode)
    {
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, prefix, planCode);
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AdminUser@123!");

        using var quietReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/config",
            auth.AccessToken,
            tenant.Subdomain,
            new UpdateNotificationConfigRequest(false, 21 * 60, 6 * 60, 3, true, 3, 1));
        (await client.SendAsync(quietReq)).EnsureSuccessStatusCode();

        using var levelsReq = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/education-levels", auth.AccessToken, tenant.Subdomain);
        var levels = (await (await client.SendAsync(levelsReq)).Content.ReadFromJsonAsync<List<EducationLevelDto>>(IntegrationTestHelpers.JsonOptions))!;
        var levelId = levels.First(l => l.IsEnabled).Id;

        using var batchReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/batches",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertBatchRequest("2026 Batch", $"B{Guid.NewGuid().ToString("N")[..5].ToUpperInvariant()}", levelId, null, null, "2026"));
        var batch = (await (await client.SendAsync(batchReq)).Content.ReadFromJsonAsync<BatchDto>(IntegrationTestHelpers.JsonOptions))!;

        var s1 = await CreateStudentAsync(client, auth.AccessToken, tenant.Subdomain, "Amal Fernando", "+94771110001", levelId, batch.Id);
        var s2 = await CreateStudentAsync(client, auth.AccessToken, tenant.Subdomain, "Binu Jayawardena", "+94771110002", levelId, batch.Id);

        using var fsReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/structures",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertFeeStructureRequest("Monthly Tuition", FeeFrequency.Monthly, 4000.00m, levelId, batch.Id, 10));
        var fs = (await (await client.SendAsync(fsReq)).Content.ReadFromJsonAsync<FeeStructureDto>(IntegrationTestHelpers.JsonOptions))!;

        return new SeedContext(auth, tenant, levelId, batch.Id, fs.Id, s1, s2);
    }

    private static async Task<StudentDto> CreateStudentAsync(
        HttpClient client,
        string token,
        string subdomain,
        string name,
        string phone,
        Guid levelId,
        Guid batchId,
        DateOnly? admissionDate = null)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            token,
            subdomain,
            new UpsertStudentRequest(name, "Guardian " + name, phone, "Colombo", levelId, AdmissionDate: admissionDate, BatchIds: [batchId]));
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<StudentDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<AuthResponse> CreateAndLoginParentAsync(HttpClient client, SeedContext ctx, string phone, IReadOnlyList<Guid> studentIds)
    {
        using var createReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/parents",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new CreateParentAccountRequest("Parent User", phone, studentIds, Password: "ParentPass@123!"));
        (await client.SendAsync(createReq)).EnsureSuccessStatusCode();

        return await IntegrationTestHelpers.LoginTenantUserAsync(client, ctx.Tenant.Subdomain, phone, "ParentPass@123!");
    }

    private static async Task<AuthResponse> CreateStaffCashierAsync(HttpClient client, SeedContext ctx, string name, string email)
    {
        using var rolesReq = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/roles", ctx.AdminAuth.AccessToken, ctx.Tenant.Subdomain);
        var roles = (await (await client.SendAsync(rolesReq)).Content.ReadFromJsonAsync<List<RoleDto>>(IntegrationTestHelpers.JsonOptions))!;
        var staffRole = roles.First(r => r.Name.Equals("Staff", StringComparison.OrdinalIgnoreCase));

        using var userReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new CreateTenantUserRequest(email, name, "Cashier@123!", RoleType.Staff, CustomRoleId: staffRole.Id));
        var userRes = await client.SendAsync(userReq);
        userRes.EnsureSuccessStatusCode();

        return await IntegrationTestHelpers.LoginTenantUserAsync(client, ctx.Tenant.Subdomain, email, "Cashier@123!");
    }

    private static async Task<FeeInvoiceDto> CreateSingleInvoiceAsync(HttpClient client, SeedContext ctx, Guid studentId, Guid structureId, string period)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices",
            ctx.AdminAuth.AccessToken,
            ctx.Tenant.Subdomain,
            new CreateSingleInvoiceRequest(studentId, structureId, period));
        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<FeeInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<FeeInvoiceDto> GetInvoiceAsync(HttpClient client, SeedContext ctx, Guid invoiceId)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, $"/api/fees/invoices/{invoiceId}", ctx.AdminAuth.AccessToken, ctx.Tenant.Subdomain);
        return (await (await client.SendAsync(req)).Content.ReadFromJsonAsync<FeeInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<StudentCreditBalanceDto> GetCreditSummaryAsync(HttpClient client, SeedContext ctx, Guid studentId)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, $"/api/fees/students/{studentId}/credits", ctx.AdminAuth.AccessToken, ctx.Tenant.Subdomain);
        return (await (await client.SendAsync(req)).Content.ReadFromJsonAsync<StudentCreditBalanceDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<BankPaymentSubmissionDto> UploadParentBankSlipAsync(
        HttpClient client,
        string subdomain,
        string parentToken,
        Guid studentId,
        Guid invoiceId,
        decimal amount,
        string referenceNumber,
        string notes)
    {
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)), "amount");
        multipart.Add(new StringContent(referenceNumber), "referenceNumber");
        multipart.Add(new StringContent("2026-11-18"), "transferDate");
        multipart.Add(new StringContent(notes), "notes");

        var fileContent = new ByteArrayContent(ValidPngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(fileContent, "file", "slip.png");

        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/parent/children/{studentId}/invoices/{invoiceId}/bank-slips")
        {
            Content = multipart
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", parentToken);
        req.Headers.Add("X-Tenant", subdomain);

        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await res.Content.ReadFromJsonAsync<BankPaymentSubmissionDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private sealed record SeedContext(
        AuthResponse AdminAuth,
        TenantDetailDto Tenant,
        Guid LevelId,
        Guid BatchId,
        Guid MonthlyStructureId,
        StudentDto Student1,
        StudentDto Student2);
}
