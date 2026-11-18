using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduFlow.Api.Endpoints;
using EduFlow.Application.Academics;
using EduFlow.Application.Attendance;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Fees;
using EduFlow.Application.Notifications;
using EduFlow.Application.Staff;
using EduFlow.Application.Students;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class Phase13EPlanSheetAuditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] ValidPngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03, 0x04];

    [Fact]
    public async Task Seeded_plans_match_A7_plan_sheet_exactly()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/plans");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var plans = (await response.Content.ReadFromJsonAsync<List<PlanDto>>(IntegrationTestHelpers.JsonOptions))!;
        plans.Select(p => p.Code).Should().ContainInOrder("Starter", "Growth", "Pro", "Enterprise");

        var starter = plans.Single(p => p.Code == "Starter");
        starter.Limits.Should().BeEquivalentTo(new Dictionary<string, int?>
        {
            [nameof(LimitKey.Students)] = 250,
            [nameof(LimitKey.EducationLevels)] = 2,
            [nameof(LimitKey.Batches)] = 10,
            [nameof(LimitKey.Subjects)] = 5,
            [nameof(LimitKey.StaffAccounts)] = 3,
            [nameof(LimitKey.AdminAccounts)] = 1,
            [nameof(LimitKey.SmsSegmentsPerMonth)] = 300,
            [nameof(LimitKey.CloudStorageGb)] = 1
        });

        var growth = plans.Single(p => p.Code == "Growth");
        growth.Limits.Should().BeEquivalentTo(new Dictionary<string, int?>
        {
            [nameof(LimitKey.Students)] = 500,
            [nameof(LimitKey.EducationLevels)] = 3,
            [nameof(LimitKey.Batches)] = 15,
            [nameof(LimitKey.Subjects)] = 10,
            [nameof(LimitKey.StaffAccounts)] = 8,
            [nameof(LimitKey.AdminAccounts)] = 2,
            [nameof(LimitKey.SmsSegmentsPerMonth)] = 600,
            [nameof(LimitKey.CloudStorageGb)] = 5
        });

        var pro = plans.Single(p => p.Code == "Pro");
        pro.Limits.Should().BeEquivalentTo(new Dictionary<string, int?>
        {
            [nameof(LimitKey.Students)] = 1500,
            [nameof(LimitKey.EducationLevels)] = 4,
            [nameof(LimitKey.Batches)] = 25,
            [nameof(LimitKey.Subjects)] = 60,
            [nameof(LimitKey.StaffAccounts)] = 40,
            [nameof(LimitKey.AdminAccounts)] = 3,
            [nameof(LimitKey.SmsSegmentsPerMonth)] = 1500,
            [nameof(LimitKey.CloudStorageGb)] = 10
        });

        var enterprise = plans.Single(p => p.Code == "Enterprise");
        enterprise.Limits.Should().BeEquivalentTo(new Dictionary<string, int?>
        {
            [nameof(LimitKey.Students)] = 2500,
            [nameof(LimitKey.EducationLevels)] = 5,
            [nameof(LimitKey.Batches)] = null,
            [nameof(LimitKey.Subjects)] = null,
            [nameof(LimitKey.StaffAccounts)] = null,
            [nameof(LimitKey.AdminAccounts)] = 5,
            [nameof(LimitKey.SmsSegmentsPerMonth)] = 2500,
            [nameof(LimitKey.CloudStorageGb)] = 50
        });

        string[] starterFeatures =
        [
            nameof(FeatureKey.ParentPortal),
            nameof(FeatureKey.ParentCanPayOnline),
            nameof(FeatureKey.ParentCanSeeMarks),
            nameof(FeatureKey.AttendanceSms),
            nameof(FeatureKey.ClassSessionSms),
            nameof(FeatureKey.FeeReminderSms),
            nameof(FeatureKey.PaymentConfirmationSms),
            nameof(FeatureKey.ExamNotifications),
            nameof(FeatureKey.EmailNotifications),
            nameof(FeatureKey.AutoBackup),
            nameof(FeatureKey.BroadcastMessages)
        ];
        string[] growthAdded = [nameof(FeatureKey.DataExport), nameof(FeatureKey.BarcodeGenerator)];
        string[] proAdded =
        [
            nameof(FeatureKey.PerformanceReports),
            nameof(FeatureKey.MultiBranch),
            nameof(FeatureKey.BatchGroups),
            nameof(FeatureKey.ReportCleanup),
            nameof(FeatureKey.PrioritySupport)
        ];
        string[] enterpriseAdded =
        [
            nameof(FeatureKey.OnlinePayments),
            nameof(FeatureKey.StaffLeave),
            nameof(FeatureKey.Timetable),
            nameof(FeatureKey.WhatsAppMessaging),
            nameof(FeatureKey.OnlineAttendance),
            nameof(FeatureKey.RemoveProductBranding)
        ];

        starter.Features.Should().BeEquivalentTo(starterFeatures);
        growth.Features.Should().BeEquivalentTo(starterFeatures.Concat(growthAdded));
        pro.Features.Should().BeEquivalentTo(starterFeatures.Concat(growthAdded).Concat(proAdded));
        enterprise.Features.Should().BeEquivalentTo(starterFeatures.Concat(growthAdded).Concat(proAdded).Concat(enterpriseAdded));
    }

    [Fact]
    public async Task Every_limit_in_A7_is_enforced_server_side()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var starter = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13e-limits", "Starter");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(client, starter.Subdomain, $"admin@{starter.Subdomain}.lk", "AdminUser@123!");

        var levels = await SendAsync<List<EducationLevelDto>>(client, auth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/education-levels");
        var enabledLevel = levels.First(l => l.IsEnabled);

        // 1. EducationLevels (Starter max = 2)
        using var levelExceedReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/education-levels/selection",
            auth.AccessToken,
            starter.Subdomain,
            new UpdateLevelSelectionRequest(levels.Take(3).Select(l => l.Id).ToList()));
        await AssertLimitReachedAsync(await client.SendAsync(levelExceedReq), nameof(LimitKey.EducationLevels), 2);

        // 2. Batches (Starter max = 10)
        for (var i = 1; i <= 10; i++)
        {
            await SendAsync<BatchDto>(
                client,
                auth.AccessToken,
                starter.Subdomain,
                HttpMethod.Post,
                "/api/batches",
                new UpsertBatchRequest($"Batch {i}", $"B{i:D2}", enabledLevel.Id, null, null, "2026"));
        }

        using var batch11Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/batches",
            auth.AccessToken,
            starter.Subdomain,
            new UpsertBatchRequest("Batch 11", "B11", enabledLevel.Id, null, null, "2026"));
        await AssertLimitReachedAsync(await client.SendAsync(batch11Req), nameof(LimitKey.Batches), 10);

        // 3. Subjects (Starter max = 5)
        for (var i = 1; i <= 5; i++)
        {
            await SendAsync<SubjectDto>(
                client,
                auth.AccessToken,
                starter.Subdomain,
                HttpMethod.Post,
                "/api/subjects",
                new UpsertSubjectRequest($"Subject {i}", $"SUB{i:D2}", enabledLevel.Id));
        }

        using var subject6Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/subjects",
            auth.AccessToken,
            starter.Subdomain,
            new UpsertSubjectRequest("Subject 6", "SUB06", enabledLevel.Id));
        await AssertLimitReachedAsync(await client.SendAsync(subject6Req), nameof(LimitKey.Subjects), 5);

        // 4. AdminAccounts (Starter max = 1) & StaffAccounts (Starter max = 3)
        using var admin2Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            auth.AccessToken,
            starter.Subdomain,
            new CreateTenantUserRequest($"admin2@{starter.Subdomain}.lk", "Admin 2", "AdminPass@123!", RoleType.InstituteAdmin));
        await AssertLimitReachedAsync(await client.SendAsync(admin2Req), nameof(LimitKey.AdminAccounts), 1);

        for (var i = 1; i <= 3; i++)
        {
            await SendAsync<TenantUserDto>(
                client,
                auth.AccessToken,
                starter.Subdomain,
                HttpMethod.Post,
                "/api/users",
                new CreateTenantUserRequest($"staff{i}@{starter.Subdomain}.lk", $"Staff {i}", "StaffPass@123!", RoleType.Staff));
        }

        using var staff4Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            auth.AccessToken,
            starter.Subdomain,
            new CreateTenantUserRequest($"staff4@{starter.Subdomain}.lk", "Staff 4", "StaffPass@123!", RoleType.Teacher));
        await AssertLimitReachedAsync(await client.SendAsync(staff4Req), nameof(LimitKey.StaffAccounts), 3);

        // 5. Students (Starter max = 250) via bulk import + 251st blocked
        var csv = new StringBuilder("fullName,guardianName,guardianPhone,address,educationLevelCode,gender\n");
        for (var i = 1; i <= 250; i++)
        {
            csv.Append($"Student {i},Guardian {i},077{1000000 + i},Colombo {i},{enabledLevel.Code},Male\n");
        }

        using var importReq = new HttpRequestMessage(HttpMethod.Post, "/api/students/import");
        importReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        importReq.Headers.Add("X-Tenant", starter.Subdomain);
        using var importForm = new MultipartFormDataContent();
        var csvContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv.ToString()));
        csvContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        importForm.Add(csvContent, "file", "students.csv");
        importReq.Content = importForm;
        var importRes = await client.SendAsync(importReq);
        importRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using var student251Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            auth.AccessToken,
            starter.Subdomain,
            new UpsertStudentRequest("Student 251", "Guardian 251", "0779999251", "Colombo", enabledLevel.Id));
        await AssertLimitReachedAsync(await client.SendAsync(student251Req), nameof(LimitKey.Students), 250);

        // 6. SmsSegmentsPerMonth & CloudStorageGb on tight custom plan
        var tightPlanCode = $"TIGHT-{Guid.NewGuid().ToString("N")[..6]}";
        using var createPlanReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/super-admin/plans",
            superAdmin.AccessToken,
            body: new CreatePlanRequest(
                Code: tightPlanCode,
                Name: "Tight Audit",
                Description: "Tight SMS and storage limits",
                PriceMonthlyLkr: 1000m,
                Currency: "LKR",
                SortOrder: 99,
                Features: [FeatureKey.AttendanceSms, FeatureKey.EmailNotifications],
                Limits:
                [
                    new PlanLimitInput(LimitKey.Students, 50),
                    new PlanLimitInput(LimitKey.EducationLevels, 2),
                    new PlanLimitInput(LimitKey.Batches, 5),
                    new PlanLimitInput(LimitKey.Subjects, 5),
                    new PlanLimitInput(LimitKey.StaffAccounts, 5),
                    new PlanLimitInput(LimitKey.AdminAccounts, 1),
                    new PlanLimitInput(LimitKey.SmsSegmentsPerMonth, 1),
                    new PlanLimitInput(LimitKey.CloudStorageGb, 0)
                ]));
        (await client.SendAsync(createPlanReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        var tightTenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13e-tight", tightPlanCode);
        var tightAuth = await IntegrationTestHelpers.LoginTenantUserAsync(client, tightTenant.Subdomain, $"admin@{tightTenant.Subdomain}.lk", "AdminUser@123!");
        var tightLevels = await SendAsync<List<EducationLevelDto>>(client, tightAuth.AccessToken, tightTenant.Subdomain, HttpMethod.Get, "/api/education-levels");
        var tightStudent = await SendAsync<StudentDto>(
            client,
            tightAuth.AccessToken,
            tightTenant.Subdomain,
            HttpMethod.Post,
            "/api/students",
            new UpsertStudentRequest("Tight Student", "Guardian", "0775551111", "Colombo", tightLevels.First(l => l.IsEnabled).Id));

        // 6a. CloudStorageGb (0 GB blocks upload, 1 GB add-on unblocks)
        var photoBlockedRes = await UploadStudentPhotoAsync(client, tightAuth.AccessToken, tightTenant.Subdomain, tightStudent.Id);
        await AssertLimitReachedAsync(photoBlockedRes, nameof(LimitKey.CloudStorageGb), 0);

        using var addStorageReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tightTenant.Id}/add-ons",
            superAdmin.AccessToken,
            body: new GrantTenantAddOnRequest(TenantAddOnType.ExtraStorageGb, 1, Note: "Audit storage"));
        (await client.SendAsync(addStorageReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        var photoAllowedRes = await UploadStudentPhotoAsync(client, tightAuth.AccessToken, tightTenant.Subdomain, tightStudent.Id);
        photoAllowedRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6b. SmsSegmentsPerMonth (1 segment with Block policy blocks 2nd segment; add-on unblocks)
        using var smsConfigReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/config",
            tightAuth.AccessToken,
            tightTenant.Subdomain,
            new UpdateNotificationConfigRequest(false, 21 * 60, 6 * 60, 3, true, 3, 1, SmsOverQuotaPolicy.Block));
        (await client.SendAsync(smsConfigReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var testSms1 = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/test-send",
            tightAuth.AccessToken,
            tightTenant.Subdomain,
            new TestSendNotificationRequest(NotificationMessageType.AttendancePresent, "0771112222", null, null, "en"));
        var sms1 = await (await client.SendAsync(testSms1)).Content.ReadFromJsonAsync<TestSendNotificationResultDto>(IntegrationTestHelpers.JsonOptions);
        sms1!.SentCount.Should().Be(1);

        using var testSms2 = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/test-send",
            tightAuth.AccessToken,
            tightTenant.Subdomain,
            new TestSendNotificationRequest(NotificationMessageType.AttendancePresent, "0771113333", null, null, "en"));
        var sms2 = await (await client.SendAsync(testSms2)).Content.ReadFromJsonAsync<TestSendNotificationResultDto>(IntegrationTestHelpers.JsonOptions);
        sms2!.SentCount.Should().Be(0);
        sms2.SkippedCount.Should().Be(1);
        sms2.Messages.Should().ContainSingle(m => m.StatusReason == "Skipped:QuotaExceeded");

        using var addSmsReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tightTenant.Id}/add-ons",
            superAdmin.AccessToken,
            body: new GrantTenantAddOnRequest(TenantAddOnType.ExtraSmsSegments, 5, Note: "Audit SMS"));
        (await client.SendAsync(addSmsReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        using var testSms3 = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/test-send",
            tightAuth.AccessToken,
            tightTenant.Subdomain,
            new TestSendNotificationRequest(NotificationMessageType.AttendancePresent, "0771114444", null, null, "en"));
        var sms3 = await (await client.SendAsync(testSms3)).Content.ReadFromJsonAsync<TestSendNotificationResultDto>(IntegrationTestHelpers.JsonOptions);
        sms3!.SentCount.Should().Be(1);
    }

    [Fact]
    public async Task Feature_rows_in_A7_are_gated_by_plan_and_tenant_switch()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var starter = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13e-feat-st", "Starter");
        var starterAuth = await IntegrationTestHelpers.LoginTenantUserAsync(client, starter.Subdomain, $"admin@{starter.Subdomain}.lk", "AdminUser@123!");

        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/students/export?format=csv", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.DataExport));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Get, $"/api/students/{Guid.NewGuid()}/id-card", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.BarcodeGenerator));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Get, $"/api/exams/{Guid.NewGuid()}/report-cards", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.PerformanceReports));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Get, $"/api/exams/students/{Guid.NewGuid()}/trends", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.PerformanceReports));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/batch-groups", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.BatchGroups));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/settings/cleanup", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.ReportCleanup));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Post, "/api/branches", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.MultiBranch), new CreateBranchRequest("Galle", "GAL"));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Put, "/api/fees/payhere/settings", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.OnlinePayments), new UpdatePayHereGatewaySettingsRequest("1211149", "sec", true, true));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/staff/attendance", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.StaffLeave));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/timetable", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.Timetable));
        await AssertFeatureErrorAsync(client, starterAuth.AccessToken, starter.Subdomain, HttpMethod.Post, "/api/attendance/join", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.OnlineAttendance), new JoinAttendanceRequest("JOIN1", "S000001"));

        // Pro plan has MultiBranch and PerformanceReports; toggling tenant switch OFF must yield FEATURE_DISABLED
        var pro = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13e-feat-pr", "Pro");
        var proAuth = await IntegrationTestHelpers.LoginTenantUserAsync(client, pro.Subdomain, $"admin@{pro.Subdomain}.lk", "AdminUser@123!");

        using var trendsOnReq = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, $"/api/exams/students/{Guid.NewGuid()}/trends", proAuth.AccessToken, pro.Subdomain);
        (await client.SendAsync(trendsOnReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        await ToggleFeatureAsync(client, proAuth.AccessToken, pro.Subdomain, FeatureKey.MultiBranch, false);
        await ToggleFeatureAsync(client, proAuth.AccessToken, pro.Subdomain, FeatureKey.PerformanceReports, false);

        await AssertFeatureErrorAsync(client, proAuth.AccessToken, pro.Subdomain, HttpMethod.Post, "/api/branches", "FEATURE_DISABLED", nameof(FeatureKey.MultiBranch), new CreateBranchRequest("Kandy", "KDY"));
        await AssertFeatureErrorAsync(client, proAuth.AccessToken, pro.Subdomain, HttpMethod.Get, $"/api/exams/students/{Guid.NewGuid()}/trends", "FEATURE_DISABLED", nameof(FeatureKey.PerformanceReports));
        await AssertFeatureErrorAsync(client, proAuth.AccessToken, pro.Subdomain, HttpMethod.Get, $"/api/exams/{Guid.NewGuid()}/report-cards", "FEATURE_DISABLED", nameof(FeatureKey.PerformanceReports));
    }

    [Fact]
    public async Task Super_Admin_per_tenant_overrides_work_for_PayHere_and_staff_attendance()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var starter = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13e-ovr", "Starter");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(client, starter.Subdomain, $"admin@{starter.Subdomain}.lk", "AdminUser@123!");

        // Blocked by default on Starter
        await AssertFeatureErrorAsync(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Put,
            "/api/fees/payhere/settings",
            "FEATURE_NOT_IN_PLAN",
            nameof(FeatureKey.OnlinePayments),
            new UpdatePayHereGatewaySettingsRequest("1211149", "SANDBOX_SECRET", true, true));

        await AssertFeatureErrorAsync(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Get,
            "/api/staff/attendance",
            "FEATURE_NOT_IN_PLAN",
            nameof(FeatureKey.StaffLeave));

        // Grant overrides ("available on request")
        using var payHereOvrReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            $"/api/super-admin/tenants/{starter.Id}/feature-overrides/OnlinePayments",
            superAdmin.AccessToken,
            body: new SetFeatureOverrideRequest(true, "Available on request: PayHere"));
        (await client.SendAsync(payHereOvrReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var staffOvrReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            $"/api/super-admin/tenants/{starter.Id}/feature-overrides/StaffLeave",
            superAdmin.AccessToken,
            body: new SetFeatureOverrideRequest(true, "Available on request: Staff attendance"));
        (await client.SendAsync(staffOvrReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        // PayHere settings + checkout now succeed on Starter
        var settings = await SendAsync<PayHereGatewaySettingsDto>(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Put,
            "/api/fees/payhere/settings",
            new UpdatePayHereGatewaySettingsRequest("1211149", "SANDBOX_SECRET", true, true));
        settings.IsOnlinePaymentsFeatureEnabled.Should().BeTrue();
        settings.IsOnlinePaymentsInPlan.Should().BeTrue();

        var levels = await SendAsync<List<EducationLevelDto>>(client, auth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/education-levels");
        var levelId = levels.First(l => l.IsEnabled).Id;
        var student = await SendAsync<StudentDto>(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Post,
            "/api/students",
            new UpsertStudentRequest("Override Student", "Guardian", "0778889900", "Colombo", levelId));
        var structure = await SendAsync<FeeStructureDto>(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Post,
            "/api/fees/structures",
            new UpsertFeeStructureRequest("Term Fee", FeeFrequency.OneTime, 2500m, levelId, null, 10));
        var invoice = await SendAsync<FeeInvoiceDto>(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Post,
            "/api/fees/invoices",
            new CreateSingleInvoiceRequest(student.Id, structure.Id, "2026-11"));
        var checkout = await SendAsync<PayHereCheckoutSessionDto>(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Post,
            "/api/fees/payhere/checkout",
            new InitiatePayHereCheckoutRequest(invoice.Id));
        checkout.OrderId.Should().StartWith("PH-");

        // Staff attendance read & write now succeed on Starter
        var sheet = await SendAsync<StaffAttendanceSheetDto>(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Get,
            "/api/staff/attendance");
        sheet.Entries.Should().NotBeEmpty();
        var adminEntry = sheet.Entries.First();
        var savedSheet = await SendAsync<StaffAttendanceSheetDto>(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Put,
            "/api/staff/attendance",
            new SaveStaffAttendanceSheetRequest(sheet.Date, [new StaffAttendanceEntryInput(adminEntry.StaffUserId, StaffAttendanceStatus.Present, 480, 1020, "On time")]));
        savedSheet.PresentCount.Should().BeGreaterOrEqualTo(1);

        // Tenant switch OFF still disables overridden feature
        await ToggleFeatureAsync(client, auth.AccessToken, starter.Subdomain, FeatureKey.StaffLeave, false);
        await AssertFeatureErrorAsync(client, auth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/staff/attendance", "FEATURE_DISABLED", nameof(FeatureKey.StaffLeave));

        // Removing overrides returns to FEATURE_NOT_IN_PLAN
        using var delPayReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Delete,
            $"/api/super-admin/tenants/{starter.Id}/feature-overrides/OnlinePayments",
            superAdmin.AccessToken);
        (await client.SendAsync(delPayReq)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var delStaffReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Delete,
            $"/api/super-admin/tenants/{starter.Id}/feature-overrides/StaffLeave",
            superAdmin.AccessToken);
        (await client.SendAsync(delStaffReq)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await AssertFeatureErrorAsync(client, auth.AccessToken, starter.Subdomain, HttpMethod.Get, "/api/staff/attendance", "FEATURE_NOT_IN_PLAN", nameof(FeatureKey.StaffLeave));
        await AssertFeatureErrorAsync(
            client,
            auth.AccessToken,
            starter.Subdomain,
            HttpMethod.Post,
            "/api/fees/payhere/checkout",
            "FEATURE_NOT_IN_PLAN",
            nameof(FeatureKey.OnlinePayments),
            new InitiatePayHereCheckoutRequest(invoice.Id));
    }

    private static async Task<HttpResponseMessage> UploadStudentPhotoAsync(
        HttpClient client,
        string token,
        string subdomain,
        Guid studentId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/students/{studentId}/photo");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-Tenant", subdomain);
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(ValidPngBytes);
        bytes.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
        form.Add(bytes, "file", "photo.png");
        req.Content = form;
        return await client.SendAsync(req);
    }

    private static async Task ToggleFeatureAsync(
        HttpClient client,
        string token,
        string subdomain,
        FeatureKey key,
        bool enabled)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            $"/api/settings/features/{key}",
            token,
            subdomain,
            new UpdateFeatureSwitchRequest(enabled));
        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task AssertLimitReachedAsync(HttpResponseMessage response, string expectedLimit, int expectedMax)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("LIMIT_REACHED");
        doc.RootElement.GetProperty("limit").GetString().Should().Be(expectedLimit);
        doc.RootElement.GetProperty("max").GetInt32().Should().Be(expectedMax);
    }

    private static async Task AssertFeatureErrorAsync(
        HttpClient client,
        string token,
        string subdomain,
        HttpMethod method,
        string url,
        string expectedCode,
        string expectedFeature,
        object? body = null)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(method, url, token, subdomain, body);
        var res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be(expectedCode);
        doc.RootElement.GetProperty("feature").GetString().Should().Be(expectedFeature);
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client,
        string token,
        string subdomain,
        HttpMethod method,
        string url,
        object? body = null)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(method, url, token, subdomain, body);
        var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        res.IsSuccessStatusCode.Should().BeTrue($"{method} {url} failed ({res.StatusCode}): {raw}");
        return JsonSerializer.Deserialize<T>(raw, IntegrationTestHelpers.JsonOptions)!;
    }
}
