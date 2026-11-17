using System.Net;
using System.Net.Http.Json;
using EduFlow.Api.Endpoints;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Notifications;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Notifications;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class NotificationEngineTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Switch_off_blocks_enqueue_and_skips_already_queued_messages_at_send_time()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "notif-sw", "Pro");
        var adminAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AdminUser@123!");

        var smsStore = factory.Services.GetRequiredService<FakeSmsStore>();
        var beforeCount = smsStore.Messages.Count(m => m.TenantId == tenant.Id);

        // 1. Enqueue while switch is ON (EnqueueOnly = true so it stays Queued in outbox)
        using var enqueueOnReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/test-send",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new TestSendNotificationRequest(
                MessageType: NotificationMessageType.AttendancePresent,
                RecipientPhone: "+94771112233",
                Language: "en",
                EnqueueOnly: true));
        var enqueueOnRes = await client.SendAsync(enqueueOnReq);
        enqueueOnRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var queuedResult = (await enqueueOnRes.Content.ReadFromJsonAsync<TestSendNotificationResultDto>(IntegrationTestHelpers.JsonOptions))!;
        queuedResult.EnqueuedCount.Should().Be(1);
        queuedResult.Messages.Should().ContainSingle(m => m.Status == nameof(NotificationStatus.Queued));

        // 2. Turn AttendanceSms feature switch OFF before dispatcher runs
        using var disableFeatureReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/AttendanceSms",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new UpdateFeatureSwitchRequest(IsEnabled: false));
        (await client.SendAsync(disableFeatureReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Try to enqueue another event while switch is OFF => nothing enqueued
        using var enqueueWhenOffReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/test-send",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new TestSendNotificationRequest(
                MessageType: NotificationMessageType.AttendancePresent,
                RecipientPhone: "+94771112244",
                Language: "en"));
        var enqueueWhenOffRes = await client.SendAsync(enqueueWhenOffReq);
        enqueueWhenOffRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var offResult = (await enqueueWhenOffRes.Content.ReadFromJsonAsync<TestSendNotificationResultDto>(IntegrationTestHelpers.JsonOptions))!;
        offResult.EnqueuedCount.Should().Be(0);
        offResult.SkippedReason.Should().Be("Skipped:FeatureOff");

        // 4. Run dispatcher now => the previously queued item must be marked Skipped:FeatureOff and NOT sent
        using var dispatchReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/dispatch",
            adminAuth.AccessToken,
            tenant.Subdomain);
        var dispatchRes = await client.SendAsync(dispatchReq);
        dispatchRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var dispatchSummary = (await dispatchRes.Content.ReadFromJsonAsync<DispatchResultDto>(IntegrationTestHelpers.JsonOptions))!;
        dispatchSummary.SkippedCount.Should().Be(1);
        dispatchSummary.SentCount.Should().Be(0);

        smsStore.Messages.Count(m => m.TenantId == tenant.Id).Should().Be(beforeCount);

        using var logReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/notifications/messages",
            adminAuth.AccessToken,
            tenant.Subdomain);
        var logRes = await (await client.SendAsync(logReq))
            .Content.ReadFromJsonAsync<NotificationMessageLogResponse>(IntegrationTestHelpers.JsonOptions);
        logRes!.Items.Should().ContainSingle(
            m => m.Id == queuedResult.Messages[0].Id
                 && m.Status == nameof(NotificationStatus.Skipped)
                 && m.StatusReason == "Skipped:FeatureOff");

        // 5. Re-enable AttendanceSms, then turn per-message-type switch AttendanceAbsent OFF => AttendanceAbsent blocked, AttendancePresent works
        using var enableFeatureReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/AttendanceSms",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new UpdateFeatureSwitchRequest(IsEnabled: true));
        (await client.SendAsync(enableFeatureReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var disableTypeReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/notifications/settings/types/AttendanceAbsent",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new UpdateNotificationTypeSettingRequest(
                IsEnabled: false,
                SmsEnabled: true,
                EmailEnabled: false,
                WhatsAppEnabled: false));
        (await client.SendAsync(disableTypeReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var absentSendReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/notifications/test-send",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new TestSendNotificationRequest(
                MessageType: NotificationMessageType.AttendanceAbsent,
                RecipientPhone: "+94771112255"));
        var absentSendRes = await (await client.SendAsync(absentSendReq))
            .Content.ReadFromJsonAsync<TestSendNotificationResultDto>(IntegrationTestHelpers.JsonOptions);
        absentSendRes!.EnqueuedCount.Should().Be(0);
        absentSendRes.SkippedReason.Should().Be("Skipped:FeatureOff");
    }

    [Fact]
    public async Task Idempotency_retry_backoff_and_dead_letter_after_five_tries_work_correctly()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "notif-retry", "Pro");

        using var scope = factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenant.Id, tenant.Subdomain);

        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        var smsStore = scope.ServiceProvider.GetRequiredService<FakeSmsStore>();

        // 1. Configure quiet hours off so we can test arbitrary timestamps
        await notificationService.UpdateConfigAsync(new UpdateNotificationConfigRequest(
            QuietHoursEnabled: false,
            QuietHoursStartMinutes: 21 * 60,
            QuietHoursEndMinutes: 6 * 60,
            FeeReminderDaysBefore: 3,
            FeeReminderOnDueDate: true,
            FeeReminderDaysAfterOverdue: 3,
            ExamReminderDaysBefore: 1));

        var baseTime = new DateTimeOffset(2026, 11, 17, 6, 0, 0, TimeSpan.Zero);

        // 2. Idempotency check
        var firstEnqueue = await notificationService.EnqueueAsync(new EnqueueNotificationRequest(
            MessageType: NotificationMessageType.AttendancePresent,
            RecipientPhone: "+94778889900",
            IdempotencyKey: "attendance-student-1-2026-11-17",
            ScheduledAtUtc: baseTime));
        firstEnqueue.EnqueuedCount.Should().Be(1);
        firstEnqueue.WasDuplicate.Should().BeFalse();

        var duplicateEnqueue = await notificationService.EnqueueAsync(new EnqueueNotificationRequest(
            MessageType: NotificationMessageType.AttendancePresent,
            RecipientPhone: "+94778889900",
            IdempotencyKey: "attendance-student-1-2026-11-17",
            ScheduledAtUtc: baseTime));
        duplicateEnqueue.EnqueuedCount.Should().Be(0);
        duplicateEnqueue.WasDuplicate.Should().BeTrue();
        duplicateEnqueue.Messages.Single().Id.Should().Be(firstEnqueue.Messages.Single().Id);

        var dispatchOk = await dispatcher.DispatchPendingAsync(baseTime, tenant.Id);
        dispatchOk.SentCount.Should().Be(1);

        // 3. Retry with exponential backoff & Dead-letter after 5 tries
        const string failingPhone = "+94770009999";
        smsStore.SetRecipientFailure(failingPhone, shouldFail: true);

        var failingEnqueue = await notificationService.EnqueueAsync(new EnqueueNotificationRequest(
            MessageType: NotificationMessageType.ClassStarted,
            RecipientPhone: failingPhone,
            IdempotencyKey: "failing-msg-1",
            ScheduledAtUtc: baseTime));
        var msgId = failingEnqueue.Messages.Single().Id;

        var currentTime = baseTime;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var result = await dispatcher.DispatchPendingAsync(currentTime, tenant.Id);
            result.ProcessedCount.Should().Be(1);
            result.FailedCount.Should().Be(1);

            var log = await notificationService.GetMessageLogAsync(new NotificationMessageFilter());
            var item = log.Items.Single(m => m.Id == msgId);
            item.AttemptCount.Should().Be(attempt);
            item.Status.Should().Be(nameof(NotificationStatus.Failed));

            if (attempt < 5)
            {
                item.IsDeadLetter.Should().BeFalse();
                item.NextAttemptAt.Should().BeAfter(currentTime);
                currentTime = item.NextAttemptAt.AddSeconds(1);
            }
            else
            {
                item.IsDeadLetter.Should().BeTrue();
                item.StatusReason.Should().StartWith("DeadLetter:");
            }
        }

        // 6th dispatch call does NOT pick up the dead-lettered message
        var sixthDispatch = await dispatcher.DispatchPendingAsync(currentTime.AddHours(2), tenant.Id);
        sixthDispatch.ProcessedCount.Should().Be(0);

        // 4. Clear failure and manual Retry succeeds
        smsStore.SetRecipientFailure(failingPhone, shouldFail: false);
        var retried = await notificationService.RetryMessageAsync(msgId, currentTime.AddHours(3));
        retried.Status.Should().Be(nameof(NotificationStatus.Sent));
        retried.IsDeadLetter.Should().BeFalse();
        retried.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task Quiet_hours_defers_regular_sms_but_allows_payment_confirmations_immediately()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "notif-quiet", "Pro");

        using var scope = factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenant.Id, tenant.Subdomain);

        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // 22:00 Asia/Colombo = 16:30 UTC (inside 21:00 - 06:00 quiet hours)
        var quietTimeUtc = new DateTimeOffset(2026, 11, 17, 16, 30, 0, TimeSpan.Zero);

        var attendanceEnqueue = await notificationService.EnqueueAsync(new EnqueueNotificationRequest(
            MessageType: NotificationMessageType.AttendanceAbsent,
            RecipientPhone: "+94772223333",
            ScheduledAtUtc: quietTimeUtc));

        var paymentEnqueue = await notificationService.EnqueueAsync(new EnqueueNotificationRequest(
            MessageType: NotificationMessageType.PaymentReceivedParent,
            RecipientPhone: "+94772223333",
            ScheduledAtUtc: quietTimeUtc));

        var duringQuietResult = await dispatcher.DispatchPendingAsync(quietTimeUtc, tenant.Id);
        duringQuietResult.DeferredCount.Should().Be(1);
        duringQuietResult.SentCount.Should().Be(1);

        var logDuringQuiet = await notificationService.GetMessageLogAsync(new NotificationMessageFilter());
        var absentMsg = logDuringQuiet.Items.Single(m => m.Id == attendanceEnqueue.Messages[0].Id);
        absentMsg.Status.Should().Be(nameof(NotificationStatus.Queued));
        absentMsg.StatusReason.Should().Be("Deferred:QuietHours");
        absentMsg.NextAttemptAt.Should().Be(new DateTimeOffset(2026, 11, 18, 0, 30, 0, TimeSpan.Zero));

        var paymentMsg = logDuringQuiet.Items.Single(m => m.Id == paymentEnqueue.Messages[0].Id);
        paymentMsg.Status.Should().Be(nameof(NotificationStatus.Sent));

        // 06:05 Asia/Colombo next morning = 00:35 UTC on 2026-11-18
        var afterQuietUtc = new DateTimeOffset(2026, 11, 18, 0, 35, 0, TimeSpan.Zero);
        var afterQuietResult = await dispatcher.DispatchPendingAsync(afterQuietUtc, tenant.Id);
        afterQuietResult.SentCount.Should().Be(1);

        var logAfterQuiet = await notificationService.GetMessageLogAsync(new NotificationMessageFilter());
        logAfterQuiet.Items.Single(m => m.Id == absentMsg.Id).Status.Should().Be(nameof(NotificationStatus.Sent));
    }

    [Fact]
    public async Task Sms_quota_counters_track_segments_warnings_extra_segments_and_block_policy()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "notif-quota", "Starter");

        using var scope = factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        tenantContext.SetTenant(tenant.Id, tenant.Subdomain);

        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        await notificationService.UpdateConfigAsync(new UpdateNotificationConfigRequest(
            QuietHoursEnabled: false,
            QuietHoursStartMinutes: 21 * 60,
            QuietHoursEndMinutes: 6 * 60,
            FeeReminderDaysBefore: 3,
            FeeReminderOnDueDate: true,
            FeeReminderDaysAfterOverdue: 3,
            ExamReminderDaysBefore: 1,
            SmsOverQuotaPolicy: SmsOverQuotaPolicy.AllowAndRecordExtra));

        // Starter plan has 300 SMS segments/month.
        // Set Broadcast template to a 10-segment GSM-7 message (1530 chars) so 30 messages = 300 segments.
        await notificationService.UpdateTemplateAsync(
            NotificationMessageType.Broadcast,
            "en",
            new UpdateNotificationTemplateRequest("Announcement", new string('X', 1530)));

        var daytimeUtc = new DateTimeOffset(2026, 11, 17, 5, 0, 0, TimeSpan.Zero);

        // Send 25 messages * 10 segments = 250 segments (83.3% of 300 -> triggers 80% warning)
        for (var i = 1; i <= 25; i++)
        {
            await notificationService.EnqueueAsync(new EnqueueNotificationRequest(
                MessageType: NotificationMessageType.Broadcast,
                RecipientPhone: $"+947730000{i:00}",
                ScheduledAtUtc: daytimeUtc));
        }

        await dispatcher.DispatchPendingAsync(daytimeUtc, tenant.Id);

        var quotaAt250 = await notificationService.GetSmsQuotaAsync(tenant.Id, daytimeUtc);
        quotaAt250.MonthlyQuota.Should().Be(300);
        quotaAt250.UsedSegments.Should().Be(250);
        quotaAt250.RemainingSegments.Should().Be(50);
        quotaAt250.ExtraSegments.Should().Be(0);
        quotaAt250.IsWarning80Percent.Should().BeTrue();
        quotaAt250.IsExceeded100Percent.Should().BeFalse();

        // Send 6 more messages * 10 segments = 60 segments -> total 310 segments (10 extra segments recorded)
        for (var i = 26; i <= 31; i++)
        {
            await notificationService.EnqueueAsync(new EnqueueNotificationRequest(
                MessageType: NotificationMessageType.Broadcast,
                RecipientPhone: $"+947730000{i:00}",
                ScheduledAtUtc: daytimeUtc));
        }

        await dispatcher.DispatchPendingAsync(daytimeUtc, tenant.Id);

        var quotaAt310 = await notificationService.GetSmsQuotaAsync(tenant.Id, daytimeUtc);
        quotaAt310.UsedSegments.Should().Be(310);
        quotaAt310.RemainingSegments.Should().Be(0);
        quotaAt310.ExtraSegments.Should().Be(10);
        quotaAt310.IsExceeded100Percent.Should().BeTrue();

        // Switch policy to Block -> next SMS is skipped with Skipped:QuotaExceeded
        await notificationService.UpdateConfigAsync(new UpdateNotificationConfigRequest(
            QuietHoursEnabled: false,
            QuietHoursStartMinutes: 21 * 60,
            QuietHoursEndMinutes: 6 * 60,
            FeeReminderDaysBefore: 3,
            FeeReminderOnDueDate: true,
            FeeReminderDaysAfterOverdue: 3,
            ExamReminderDaysBefore: 1,
            SmsOverQuotaPolicy: SmsOverQuotaPolicy.Block));

        var blockedEnqueue = await notificationService.EnqueueAsync(new EnqueueNotificationRequest(
            MessageType: NotificationMessageType.Broadcast,
            RecipientPhone: "+94773999999",
            ScheduledAtUtc: daytimeUtc));

        var blockDispatch = await dispatcher.DispatchPendingAsync(daytimeUtc, tenant.Id);
        blockDispatch.SkippedCount.Should().Be(1);
        blockDispatch.SentCount.Should().Be(0);

        var log = await notificationService.GetMessageLogAsync(new NotificationMessageFilter());
        var skippedMsg = log.Items.Single(m => m.Id == blockedEnqueue.Messages[0].Id);
        skippedMsg.Status.Should().Be(nameof(NotificationStatus.Skipped));
        skippedMsg.StatusReason.Should().Be("Skipped:QuotaExceeded");
    }
}
