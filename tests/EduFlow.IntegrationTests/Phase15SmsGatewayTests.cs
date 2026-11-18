using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using EduFlow.Api.Endpoints;
using EduFlow.Application.Broadcasts;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Notifications;
using EduFlow.Application.SuperAdmin;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Notifications;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class Phase15SmsGatewayTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task SuperAdmin_sms_settings_encrypts_api_key_never_exposes_it_and_tracks_balance_test_and_errors()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        const string secretKey = "notify-live-secret-key-xyz-987";

        using var updateReq = new HttpRequestMessage(HttpMethod.Put, "/api/super-admin/settings/sms")
        {
            Content = JsonContent.Create(new UpdateSuperAdminSmsSettingsRequest(
                Provider: "Fake",
                UserId: "user-999",
                ApiKey: secretKey,
                SenderId: "Admino",
                PromotionalSenderId: "EduPromo",
                BaseUrl: "https://app.notify.lk/api/v1",
                TestMode: true,
                TestAllowList: "0771112233, +94712223344",
                LowBalanceThreshold: 500m))
        };
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var updateRes = await client.SendAsync(updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var updateBody = await updateRes.Content.ReadAsStringAsync();
        updateBody.Should().NotContain(secretKey);

        using var getReq = new HttpRequestMessage(HttpMethod.Get, "/api/super-admin/settings/sms");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var getRes = await client.SendAsync(getReq);
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getRaw = await getRes.Content.ReadAsStringAsync();
        getRaw.Should().NotContain(secretKey);
        var settings = await getRes.Content.ReadFromJsonAsync<SuperAdminSmsSettingsDto>(IntegrationTestHelpers.JsonOptions);
        settings!.HasApiKey.Should().BeTrue();
        settings.ApiKeySource.Should().Be("Database");
        settings.TestAllowList.Should().Contain(["94771112233", "94712223344"]);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.PlatformSmsSettings.IgnoreQueryFilters().SingleAsync(s => s.Key == "default");
            row.EncryptedApiKey.Should().NotBeNullOrWhiteSpace();
            row.EncryptedApiKey.Should().NotContain(secretKey);

            var audits = await db.AuditLogs.IgnoreQueryFilters()
                .Where(a => a.Action == "SuperAdmin.SmsSettingsUpdated")
                .ToListAsync();
            audits.Should().NotBeEmpty();
            audits.Should().OnlyContain(a => a.DetailsJson == null || !a.DetailsJson.Contains(secretKey, StringComparison.Ordinal));
        }

        // Balance check caches for 5 min
        using var bal1Req = new HttpRequestMessage(HttpMethod.Get, "/api/super-admin/settings/sms/balance");
        bal1Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var bal1 = await (await client.SendAsync(bal1Req)).Content.ReadFromJsonAsync<SmsGatewayBalanceDto>(IntegrationTestHelpers.JsonOptions);
        bal1!.Succeeded.Should().BeTrue();
        bal1.FromCache.Should().BeFalse();

        using var bal2Req = new HttpRequestMessage(HttpMethod.Get, "/api/super-admin/settings/sms/balance");
        bal2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var bal2 = await (await client.SendAsync(bal2Req)).Content.ReadFromJsonAsync<SmsGatewayBalanceDto>(IntegrationTestHelpers.JsonOptions);
        bal2!.FromCache.Should().BeTrue();

        // Update settings without apiKey preserves existing encrypted key
        using var updateKeepReq = new HttpRequestMessage(HttpMethod.Put, "/api/super-admin/settings/sms")
        {
            Content = JsonContent.Create(new UpdateSuperAdminSmsSettingsRequest(
                Provider: "Fake",
                UserId: "user-999",
                ApiKey: null,
                SenderId: "Admino",
                PromotionalSenderId: "EduPromo",
                BaseUrl: "https://app.notify.lk/api/v1",
                TestMode: true,
                TestAllowList: "0771112233",
                LowBalanceThreshold: 400m))
        };
        updateKeepReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var kept = await (await client.SendAsync(updateKeepReq)).Content.ReadFromJsonAsync<SuperAdminSmsSettingsDto>(IntegrationTestHelpers.JsonOptions);
        kept!.HasApiKey.Should().BeTrue();
        kept.LowBalanceThreshold.Should().Be(400m);

        // Test SMS sends cleanly
        using var testReq = new HttpRequestMessage(HttpMethod.Post, "/api/super-admin/settings/sms/test")
        {
            Content = JsonContent.Create(new SendSuperAdminTestSmsRequest("0771112233", "Gateway check"))
        };
        testReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var testRes = await client.SendAsync(testReq);
        testRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var testDto = await testRes.Content.ReadFromJsonAsync<SuperAdminTestSmsResultDto>(IntegrationTestHelpers.JsonOptions);
        testDto!.Succeeded.Should().BeTrue();

        // Reset back to clean defaults
        using var resetReq = new HttpRequestMessage(HttpMethod.Put, "/api/super-admin/settings/sms")
        {
            Content = JsonContent.Create(new UpdateSuperAdminSmsSettingsRequest(
                Provider: "Fake",
                UserId: "",
                ApiKey: null,
                SenderId: "Admino",
                PromotionalSenderId: null,
                BaseUrl: "https://app.notify.lk/api/v1",
                TestMode: true,
                TestAllowList: "",
                LowBalanceThreshold: 500m,
                ClearApiKey: true))
        };
        resetReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        (await client.SendAsync(resetReq)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NotifyLk_TestMode_phone_normalization_and_feature_switch_off_work_end_to_end()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "p15-nlk", "Enterprise");
        var adminAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AdminUser@123!");

        // Disable quiet hours so test sends immediately
        using var cfgReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/config",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new UpdateNotificationConfigRequest(false, 21 * 60, 6 * 60, 3, true, 3, 1));
        (await client.SendAsync(cfgReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Configure NotifyLk + TestMode allow-list in DB
        using var smsCfgReq = new HttpRequestMessage(HttpMethod.Put, "/api/super-admin/settings/sms")
        {
            Content = JsonContent.Create(new UpdateSuperAdminSmsSettingsRequest(
                Provider: "NotifyLk",
                UserId: "u-100",
                ApiKey: "secret-key-for-nlk-test",
                SenderId: "Admino",
                PromotionalSenderId: "EduPromo",
                BaseUrl: "https://app.notify.lk/api/v1",
                TestMode: true,
                TestAllowList: "0771112233",
                LowBalanceThreshold: 500m))
        };
        smsCfgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        (await client.SendAsync(smsCfgReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetTenant(tenant.Id, tenant.Subdomain);

            var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

            // 1. Not in allow-list -> Skipped:TestMode
            var notAllowed = await notificationService.TriggerTestSendAsync(new TestSendNotificationRequest(
                NotificationMessageType.AttendancePresent,
                RecipientPhone: "0779998888",
                IdempotencyKey: "p15-not-allowed"));
            notAllowed.SkippedCount.Should().Be(1);
            notAllowed.Messages.Single().StatusReason.Should().Be("Skipped:TestMode");

            // 2. Landline -> Skipped:InvalidNumber before sending
            var landline = await notificationService.TriggerTestSendAsync(new TestSendNotificationRequest(
                NotificationMessageType.AttendancePresent,
                RecipientPhone: "0112345678",
                IdempotencyKey: "p15-landline"));
            landline.SkippedCount.Should().Be(1);
            landline.Messages.Single().StatusReason.Should().StartWith("Skipped:InvalidNumber");
        }

        // 3. Feature switch OFF still blocks sending even for an allowed number
        using var offReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/AttendanceSms",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new UpdateFeatureSwitchRequest(false));
        (await client.SendAsync(offReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var blockedReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/test-send",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new TestSendNotificationRequest(
                NotificationMessageType.AttendancePresent,
                RecipientPhone: "0771112233",
                IdempotencyKey: "p15-feature-off"));
        var blockedRes = await (await client.SendAsync(blockedReq))
            .Content.ReadFromJsonAsync<TestSendNotificationResultDto>(IntegrationTestHelpers.JsonOptions);
        blockedRes!.EnqueuedCount.Should().Be(0);
        blockedRes.SkippedReason.Should().Be("Skipped:FeatureOff");

        // Restore Fake provider so other tests remain isolated
        using var restoreReq = new HttpRequestMessage(HttpMethod.Put, "/api/super-admin/settings/sms")
        {
            Content = JsonContent.Create(new UpdateSuperAdminSmsSettingsRequest(
                Provider: "Fake",
                UserId: "",
                ApiKey: null,
                SenderId: "Admino",
                PromotionalSenderId: null,
                BaseUrl: "https://app.notify.lk/api/v1",
                TestMode: true,
                TestAllowList: "",
                LowBalanceThreshold: 500m,
                ClearApiKey: true))
        };
        restoreReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        (await client.SendAsync(restoreReq)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Low_balance_job_emails_SuperAdmin_once_per_day_when_below_threshold()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);

        // Set threshold above FakeSmsStore default balance (5000.00)
        using var setThresholdReq = new HttpRequestMessage(HttpMethod.Put, "/api/super-admin/settings/sms")
        {
            Content = JsonContent.Create(new UpdateSuperAdminSmsSettingsRequest(
                Provider: "Fake",
                UserId: "",
                ApiKey: null,
                SenderId: "Admino",
                PromotionalSenderId: null,
                BaseUrl: "https://app.notify.lk/api/v1",
                TestMode: true,
                TestAllowList: "",
                LowBalanceThreshold: 6000m))
        };
        setThresholdReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        (await client.SendAsync(setThresholdReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using (tenantContext.BeginSystemScope())
            {
                var row = await db.PlatformSmsSettings.IgnoreQueryFilters().SingleAsync(s => s.Key == "default");
                row.LastLowBalanceAlertSentAt = null;
                await db.SaveChangesAsync();
            }

            var smsAdmin = scope.ServiceProvider.GetRequiredService<ISmsGatewayAdminService>();
            var emailStore = scope.ServiceProvider.GetRequiredService<InMemoryEmailStore>();
            var beforeCount = emailStore.Messages.Count(m =>
                m.Metadata is not null && m.Metadata.TryGetValue("Type", out var t) && t == "SmsLowBalanceAlert");

            var day1 = new DateTimeOffset(2026, 11, 18, 3, 0, 0, TimeSpan.Zero); // 08:30 Colombo
            var firstCheck = await smsAdmin.CheckLowBalanceAndNotifyAsync(day1);
            firstCheck.IsLow.Should().BeTrue();
            firstCheck.AlertEmailSent.Should().BeTrue();

            var secondCheckSameDay = await smsAdmin.CheckLowBalanceAndNotifyAsync(day1.AddHours(4));
            secondCheckSameDay.IsLow.Should().BeTrue();
            secondCheckSameDay.AlertEmailSent.Should().BeFalse();

            var afterCount = emailStore.Messages.Count(m =>
                m.Metadata is not null && m.Metadata.TryGetValue("Type", out var t) && t == "SmsLowBalanceAlert");
            (afterCount - beforeCount).Should().Be(1);
        }

        // Restore threshold
        using var restoreReq = new HttpRequestMessage(HttpMethod.Put, "/api/super-admin/settings/sms")
        {
            Content = JsonContent.Create(new UpdateSuperAdminSmsSettingsRequest(
                Provider: "Fake",
                UserId: "",
                ApiKey: null,
                SenderId: "Admino",
                PromotionalSenderId: null,
                BaseUrl: "https://app.notify.lk/api/v1",
                TestMode: true,
                TestAllowList: "",
                LowBalanceThreshold: 500m))
        };
        restoreReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        (await client.SendAsync(restoreReq)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Tenant_custom_SmsSenderId_is_gated_by_Enterprise_or_SuperAdmin_override_and_broadcast_warns_when_promotional_unset()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var starter = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "p15-starter", "Starter");
        var starterAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            starter.Subdomain,
            $"admin@{starter.Subdomain}.lk",
            "AdminUser@123!");

        // 1. Starter cannot customize Sender ID on its own
        using var deniedReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/sender-id",
            starterAuth.AccessToken,
            starter.Subdomain,
            new EduFlow.Application.Notifications.UpdateTenantSmsSenderIdRequest("STARTER"));
        var deniedRes = await client.SendAsync(deniedReq);
        deniedRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Super Admin grants custom Sender ID + override on Starter
        using var grantReq = new HttpRequestMessage(HttpMethod.Put, $"/api/super-admin/tenants/{starter.Id}/sms-sender-id")
        {
            Content = JsonContent.Create(new SetTenantSmsSenderIdRequest("ROYAL", true))
        };
        grantReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var grantRes = await client.SendAsync(grantReq);
        grantRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Now Starter admin can update its Sender ID and FakeSmsStore records it
        using var allowReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/sender-id",
            starterAuth.AccessToken,
            starter.Subdomain,
            new EduFlow.Application.Notifications.UpdateTenantSmsSenderIdRequest("ROYAL2"));
        (await client.SendAsync(allowReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var cfgReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/config",
            starterAuth.AccessToken,
            starter.Subdomain,
            new UpdateNotificationConfigRequest(false, 21 * 60, 6 * 60, 3, true, 3, 1));
        (await client.SendAsync(cfgReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var sendReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/test-send",
            starterAuth.AccessToken,
            starter.Subdomain,
            new TestSendNotificationRequest(NotificationMessageType.AttendancePresent, RecipientPhone: "0774445566"));
        (await client.SendAsync(sendReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var smsStore = factory.Services.GetRequiredService<FakeSmsStore>();
        smsStore.Messages.Should().Contain(m => m.TenantId == starter.Id && m.SenderId == "ROYAL2");

        // 4. Broadcast sms-status reports promotionalSenderIdConfigured = false when unset
        using var statusReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/broadcasts/sms-status",
            starterAuth.AccessToken,
            starter.Subdomain);
        var statusDto = await (await client.SendAsync(statusReq))
            .Content.ReadFromJsonAsync<BroadcastSmsStatusDto>(IntegrationTestHelpers.JsonOptions);
        statusDto!.PromotionalSenderIdConfigured.Should().BeFalse();
    }
}
