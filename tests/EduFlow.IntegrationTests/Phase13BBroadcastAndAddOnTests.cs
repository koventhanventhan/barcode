using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Api.Endpoints;
using EduFlow.Application.Academics;
using EduFlow.Application.Auth;
using EduFlow.Application.Broadcasts;
using EduFlow.Application.Fees;
using EduFlow.Application.Notifications;
using EduFlow.Application.Students;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Enums;
using FluentAssertions;

namespace EduFlow.IntegrationTests;

public sealed class Phase13BBroadcastAndAddOnTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Audience_resolution_segment_estimate_schedule_and_quiet_hours()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "bcast", "Pro");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");

        var levels = await SendAsync<List<EducationLevelDto>>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/education-levels");
        var enabled = levels.Where(level => level.IsEnabled).Take(2).ToList();
        enabled.Should().HaveCount(2);

        var group = await SendAsync<BatchGroupDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/batch-groups",
            new UpsertBatchGroupRequest("Science group", $"G{Guid.NewGuid():N}"[..6]));
        var groupedBatch = await SendAsync<BatchDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/batches",
            new UpsertBatchRequest("Grouped batch", $"GB{Guid.NewGuid():N}"[..6], enabled[0].Id, null, group.Id, "2026"));
        var otherBatch = await SendAsync<BatchDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/batches",
            new UpsertBatchRequest("Other batch", $"OB{Guid.NewGuid():N}"[..6], enabled[1].Id, null, null, "2026"));

        var amaya = await CreateStudent(client, auth.AccessToken, tenant.Subdomain, "Amaya Fernando", "+94771110001", enabled[0].Id, groupedBatch.Id);
        var nimal = await CreateStudent(client, auth.AccessToken, tenant.Subdomain, "Nimal Perera", "+94771110002", enabled[0].Id, groupedBatch.Id);
        var kavindi = await CreateStudent(client, auth.AccessToken, tenant.Subdomain, "Kavindi Silva", "+94771110003", enabled[1].Id, otherBatch.Id);

        var structure = await SendAsync<FeeStructureDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/fees/structures",
            new UpsertFeeStructureRequest("Monthly", FeeFrequency.Monthly, 2500m, enabled[0].Id, groupedBatch.Id));
        await SendAsync<FeeInvoiceDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/fees/invoices",
            new CreateSingleInvoiceRequest(nimal.Id, structure.Id, DueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3))));

        var all = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(BroadcastAudienceKind.All, NotificationChannel.Sms, "Hello parents"));
        all.RecipientCount.Should().Be(3);
        all.EstimatedSegments.Should().Be(3);
        all.IsUnicode.Should().BeFalse();
        all.CharacterLimit.Should().Be(160);

        var level = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(
            BroadcastAudienceKind.Level, NotificationChannel.Sms, "Hello parents", AudienceId: enabled[0].Id));
        level.RecipientCount.Should().Be(2);

        var batch = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(
            BroadcastAudienceKind.Batch, NotificationChannel.Sms, "Hello parents", AudienceId: otherBatch.Id));
        batch.RecipientCount.Should().Be(1);

        var grouped = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(
            BroadcastAudienceKind.BatchGroup, NotificationChannel.Sms, "Hello parents", AudienceId: group.Id));
        grouped.RecipientCount.Should().Be(2);

        var selected = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(
            BroadcastAudienceKind.SelectedStudents, NotificationChannel.Sms, "Hello parents", StudentIds: [amaya.Id]));
        selected.RecipientCount.Should().Be(1);

        var overdue = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(
            BroadcastAudienceKind.FeeOverdue, NotificationChannel.Sms, "Please settle fees"));
        overdue.RecipientCount.Should().Be(1);

        var tamil = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(
            BroadcastAudienceKind.All, NotificationChannel.Sms, "வணக்கம்", BodyTemplate: "{message}", Language: "ta"));
        tamil.IsUnicode.Should().BeTrue();
        tamil.CharacterLimit.Should().Be(70);
        tamil.EstimatedSegments.Should().Be(3);

        var longEnglish = new string('A', 161);
        var multipart = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(
            BroadcastAudienceKind.All, NotificationChannel.Sms, longEnglish, BodyTemplate: "{message}"));
        multipart.SampleSegments.Should().Be(2);
        multipart.EstimatedSegments.Should().Be(6);
        multipart.CharacterLimit.Should().Be(153);

        using var whatsApp = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/broadcasts/preview", auth.AccessToken, tenant.Subdomain,
            new BroadcastComposeRequest(BroadcastAudienceKind.All, NotificationChannel.WhatsApp, "Hello parents"));
        var whatsAppResponse = await client.SendAsync(whatsApp);
        whatsAppResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await whatsAppResponse.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_NOT_IN_PLAN");
        }

        var scheduled = await SendAsync<BroadcastSendResultDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/broadcasts",
            new BroadcastComposeRequest(BroadcastAudienceKind.SelectedStudents, NotificationChannel.Sms, "See you Monday", StudentIds: [kavindi.Id], ScheduledAt: DateTimeOffset.UtcNow.AddDays(2)));
        scheduled.Status.Should().Be(nameof(BroadcastCampaignStatus.Scheduled));
        scheduled.EnqueuedCount.Should().Be(1);

        await SendAsync<TenantNotificationConfigDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Put, "/api/notifications/settings/config",
            new UpdateNotificationConfigRequest(true, 0, 1439, 3, true, 3, 1));
        var quiet = await Preview(client, auth, tenant.Subdomain, new BroadcastComposeRequest(
            BroadcastAudienceKind.SelectedStudents, NotificationChannel.Sms, "Held for morning", StudentIds: [amaya.Id]));
        quiet.QuietHoursActive.Should().BeTrue();

        await SendAsync<BroadcastSendResultDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/broadcasts",
            new BroadcastComposeRequest(BroadcastAudienceKind.SelectedStudents, NotificationChannel.Sms, "Held for morning", StudentIds: [amaya.Id]));
        var dispatch = await SendAsync<DispatchResultDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/notifications/dispatch");
        dispatch.DeferredCount.Should().BeGreaterThan(0);
        dispatch.SentCount.Should().Be(0);

        var detail = await SendAsync<BroadcastCampaignDetailDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Get, $"/api/broadcasts/{scheduled.CampaignId}");
        detail.Recipients.Should().ContainSingle(r => r.StudentId == kavindi.Id && r.Status == nameof(NotificationStatus.Queued));
    }

    [Fact]
    public async Task Broadcast_switch_off_blocks_sending_and_skips_queued_messages()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "bcast-off", "Starter");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");
        var levels = await SendAsync<List<EducationLevelDto>>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/education-levels");
        var level = levels.First(item => item.IsEnabled);
        var student = await CreateStudent(client, auth.AccessToken, tenant.Subdomain, "Ravi Kumar", "+94772220001", level.Id, null);

        await SendAsync<TenantNotificationConfigDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Put, "/api/notifications/settings/config",
            new UpdateNotificationConfigRequest(false, 21 * 60, 6 * 60, 3, true, 3, 1));

        using var off = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put, "/api/settings/features/BroadcastMessages", auth.AccessToken, tenant.Subdomain,
            new UpdateFeatureSwitchRequest(false));
        (await client.SendAsync(off)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var blocked = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/broadcasts", auth.AccessToken, tenant.Subdomain,
            new BroadcastComposeRequest(BroadcastAudienceKind.All, NotificationChannel.Sms, "This must not send"));
        var blockedResponse = await client.SendAsync(blocked);
        blockedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await blockedResponse.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_DISABLED");
        }

        using var on = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put, "/api/settings/features/BroadcastMessages", auth.AccessToken, tenant.Subdomain,
            new UpdateFeatureSwitchRequest(true));
        (await client.SendAsync(on)).StatusCode.Should().Be(HttpStatusCode.OK);

        var queued = await SendAsync<BroadcastSendResultDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/broadcasts",
            new BroadcastComposeRequest(BroadcastAudienceKind.SelectedStudents, NotificationChannel.Sms, "Queued then stopped", StudentIds: [student.Id]));
        queued.EnqueuedCount.Should().Be(1);

        using var offAgain = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put, "/api/settings/features/BroadcastMessages", auth.AccessToken, tenant.Subdomain,
            new UpdateFeatureSwitchRequest(false));
        (await client.SendAsync(offAgain)).StatusCode.Should().Be(HttpStatusCode.OK);

        var dispatch = await SendAsync<DispatchResultDto>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/notifications/dispatch");
        dispatch.SkippedCount.Should().BeGreaterThan(0);
        dispatch.SentCount.Should().Be(0);
        var log = await SendAsync<NotificationMessageLogResponse>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/notifications/messages");
        log.Items.Should().Contain(m => m.Status == nameof(NotificationStatus.Skipped) && m.StatusReason == "Skipped:FeatureOff");
    }

    [Fact]
    public async Task Add_ons_raise_plan_limits_and_extra_students_require_enterprise()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var enterprise = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "addon-ent", "Enterprise");
        var starter = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "addon-st", "Starter");
        var admin = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, enterprise.Subdomain, $"admin@{enterprise.Subdomain}.lk", "AdminUser@123!");

        (await LimitMax(client, admin, enterprise.Subdomain, "Students")).Should().Be(2500);
        (await LimitMax(client, admin, enterprise.Subdomain, "SmsSegmentsPerMonth")).Should().Be(2500);
        (await LimitMax(client, admin, enterprise.Subdomain, "CloudStorageGb")).Should().Be(50);

        using var expired = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/super-admin/tenants/{enterprise.Id}/add-ons", superAdmin.AccessToken, null,
            new GrantTenantAddOnRequest(TenantAddOnType.ExtraStudents, 500, DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddMinutes(-5)));
        (await client.SendAsync(expired)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await LimitMax(client, admin, enterprise.Subdomain, "Students")).Should().Be(2500);

        using var students = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/super-admin/tenants/{enterprise.Id}/add-ons", superAdmin.AccessToken, null,
            new GrantTenantAddOnRequest(TenantAddOnType.ExtraStudents, 100));
        var granted = await (await client.SendAsync(students)).Content.ReadFromJsonAsync<TenantAddOnDto>(IntegrationTestHelpers.JsonOptions);
        (await LimitMax(client, admin, enterprise.Subdomain, "Students")).Should().Be(2600);

        using var sms = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/super-admin/tenants/{enterprise.Id}/add-ons", superAdmin.AccessToken, null,
            new GrantTenantAddOnRequest(TenantAddOnType.ExtraSmsSegments, 400));
        (await client.SendAsync(sms)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await LimitMax(client, admin, enterprise.Subdomain, "SmsSegmentsPerMonth")).Should().Be(2900);

        using var storage = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/super-admin/tenants/{enterprise.Id}/add-ons", superAdmin.AccessToken, null,
            new GrantTenantAddOnRequest(TenantAddOnType.ExtraStorageGb, 5));
        (await client.SendAsync(storage)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await LimitMax(client, admin, enterprise.Subdomain, "CloudStorageGb")).Should().Be(55);

        using var revoke = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Delete, $"/api/super-admin/tenants/{enterprise.Id}/add-ons/{granted!.Id}", superAdmin.AccessToken);
        (await client.SendAsync(revoke)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LimitMax(client, admin, enterprise.Subdomain, "Students")).Should().Be(2500);

        using var denied = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/super-admin/tenants/{starter.Id}/add-ons", superAdmin.AccessToken, null,
            new GrantTenantAddOnRequest(TenantAddOnType.ExtraStudents, 10));
        var deniedResponse = await client.SendAsync(denied);
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var deniedDoc = JsonDocument.Parse(await deniedResponse.Content.ReadAsStringAsync());
        deniedDoc.RootElement.GetProperty("code").GetString().Should().Be("ADDON_NOT_ALLOWED");
    }

    [Fact]
    public async Task Upload_is_blocked_when_storage_quota_is_reached()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "stor", "Starter");
        var code = $"zero{Guid.NewGuid():N}"[..12];
        using var planReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/super-admin/plans", superAdmin.AccessToken, null,
            new CreatePlanRequest(code, "Zero storage", null, null, "LKR", 90,
                [FeatureKey.BroadcastMessages],
                [new PlanLimitInput(LimitKey.Students, 20), new PlanLimitInput(LimitKey.CloudStorageGb, 0)]));
        (await client.SendAsync(planReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        using var assign = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/super-admin/tenants/{tenant.Id}/subscription/assign-plan", superAdmin.AccessToken, null,
            new AssignPlanRequest(PlanCode: code));
        (await client.SendAsync(assign)).EnsureSuccessStatusCode();

        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");
        var levels = await SendAsync<List<EducationLevelDto>>(client, auth.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/education-levels");
        var student = await CreateStudent(client, auth.AccessToken, tenant.Subdomain, "Photo Student", "+94773330001", levels.First(l => l.IsEnabled).Id, null);

        using var upload = new HttpRequestMessage(HttpMethod.Post, $"/api/students/{student.Id}/photo");
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        upload.Headers.Add("X-Tenant", tenant.Subdomain);
        var file = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0x00]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var form = new MultipartFormDataContent();
        form.Add(file, "file", "photo.jpg");
        upload.Content = form;
        var response = await client.SendAsync(upload);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("LIMIT_REACHED");
        doc.RootElement.GetProperty("limit").GetString().Should().Be("CloudStorageGb");
    }

    private static async Task<BroadcastPreviewDto> Preview(HttpClient client, AuthResponse auth, string subdomain, BroadcastComposeRequest request)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Post, "/api/broadcasts/preview", auth.AccessToken, subdomain, request);
        var response = await client.SendAsync(req);
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<BroadcastPreviewDto>(body, IntegrationTestHelpers.JsonOptions)!;
    }

    private static async Task<T> SendAsync<T>(HttpClient client, string token, string subdomain, HttpMethod method, string url, object? body = null)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(method, url, token, subdomain, body);
        var response = await client.SendAsync(req);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        if (typeof(T) == typeof(string) || response.StatusCode == HttpStatusCode.NoContent)
        {
            return default!;
        }

        return JsonSerializer.Deserialize<T>(text, IntegrationTestHelpers.JsonOptions)!;
    }

    private static async Task<StudentDto> CreateStudent(HttpClient client, string token, string subdomain, string name, string phone, Guid levelId, Guid? batchId)
    {
        return await SendAsync<StudentDto>(client, token, subdomain, HttpMethod.Post, "/api/students",
            new UpsertStudentRequest(name, "Guardian " + name, phone, "Colombo", levelId, BatchIds: batchId is null ? null : [batchId.Value]));
    }

    private static async Task<int?> LimitMax(HttpClient client, AuthResponse auth, string subdomain, string key)
    {
        var summary = await SendAsync<TenantSubscriptionSummaryDto>(client, auth.AccessToken, subdomain, HttpMethod.Get, "/api/subscription");
        return summary.Limits.First(limit => limit.LimitKey == key).MaxAllowed;
    }
}
