using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Api.Endpoints;
using EduFlow.Application.Academics;
using EduFlow.Application.Backups;
using EduFlow.Application.Common.Interfaces;
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

public sealed class Phase9BackupsAndEnterpriseTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Backup_produces_valid_archive_with_strict_tenant_isolation_and_sends_admin_notification()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);

        var tenantA = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "bkp-iso-a", "Pro");
        var tenantB = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "bkp-iso-b", "Pro");

        var authA = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenantA.Subdomain, $"admin@{tenantA.Subdomain}.lk", "AdminUser@123!");
        var authB = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenantB.Subdomain, $"admin@{tenantB.Subdomain}.lk", "AdminUser@123!");

        var levelA = await FirstEnabledLevelAsync(client, authA.AccessToken, tenantA.Subdomain);
        var levelB = await FirstEnabledLevelAsync(client, authB.AccessToken, tenantB.Subdomain);

        var studentA = await CreateStudentAsync(client, authA.AccessToken, tenantA.Subdomain, "Nimal TenantA", "0771112233", levelA);
        var studentB = await CreateStudentAsync(client, authB.AccessToken, tenantB.Subdomain, "Kamal TenantB", "0774445566", levelB);

        // Run manual backup for Tenant A
        using var runReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/backups/run",
            authA.AccessToken,
            tenantA.Subdomain);
        var runRes = await client.SendAsync(runReq);
        runRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var backupA = (await runRes.Content.ReadFromJsonAsync<TenantBackupRecordDto>(IntegrationTestHelpers.JsonOptions))!;
        backupA.TenantId.Should().Be(tenantA.Id);
        backupA.Status.Should().Be(nameof(BackupStatus.Completed));
        backupA.SizeBytes.Should().BeGreaterThan(0);

        // Download archive and inspect contents
        using var downloadReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/backups/{backupA.Id}/download",
            authA.AccessToken,
            tenantA.Subdomain);
        var downloadRes = await client.SendAsync(downloadReq);
        downloadRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var zipBytes = await downloadRes.Content.ReadAsByteArrayAsync();

        await using var zipStream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        archive.Entries.Select(e => e.FullName).Should().Contain(
        [
            "manifest.json",
            "data/snapshot.json",
            "files_manifest.json",
            "csv/students.csv",
            "csv/batches.csv",
            "csv/attendance.csv",
            "csv/fee_invoices.csv"
        ]);

        await using var snapshotStream = archive.GetEntry("data/snapshot.json")!.Open();
        var snapshot = (await JsonSerializer.DeserializeAsync<TenantBackupSnapshot>(snapshotStream, IntegrationTestHelpers.JsonOptions))!;
        snapshot.TenantId.Should().Be(tenantA.Id);
        snapshot.Students.Should().ContainSingle(s => s.Id == studentA.Id && s.FullName == "Nimal TenantA");
        snapshot.Students.Should().NotContain(s => s.Id == studentB.Id || s.TenantId == tenantB.Id);

        using var csvReader = new StreamReader(archive.GetEntry("csv/students.csv")!.Open());
        var csvText = await csvReader.ReadToEndAsync();
        csvText.Should().Contain("Nimal TenantA");
        csvText.Should().NotContain("Kamal TenantB");

        // Tenant B cannot download Tenant A's backup
        using var crossDownloadReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/backups/{backupA.Id}/download",
            authB.AccessToken,
            tenantB.Subdomain);
        (await client.SendAsync(crossDownloadReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // BackupResultAdmin notification was logged for Tenant A
        using var msgReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/notifications/messages?messageType={NotificationMessageType.BackupResultAdmin}",
            authA.AccessToken,
            tenantA.Subdomain);
        var msgRes = await client.SendAsync(msgReq);
        var msgLog = (await msgRes.Content.ReadFromJsonAsync<NotificationMessageLogResponse>(IntegrationTestHelpers.JsonOptions))!;
        msgLog.TotalCount.Should().BeGreaterThanOrEqualTo(1);

        // Super Admin dry-run restore and live restore after soft-deleting studentA
        using var deleteStudentReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Delete,
            $"/api/students/{studentA.Id}",
            authA.AccessToken,
            tenantA.Subdomain);
        (await client.SendAsync(deleteStudentReq)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var dryRunReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tenantA.Id}/backups/{backupA.Id}/restore",
            superAdmin.AccessToken,
            body: new RestoreTenantBackupRequest(DryRun: true));
        var dryRunRes = await client.SendAsync(dryRunReq);
        dryRunRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var dryRunDto = (await dryRunRes.Content.ReadFromJsonAsync<BackupRestoreResultDto>(IntegrationTestHelpers.JsonOptions))!;
        dryRunDto.DryRun.Should().BeTrue();
        dryRunDto.IsValid.Should().BeTrue();
        dryRunDto.RestorableCounts["students"].Should().Be(1);
        dryRunDto.RestoredTotal.Should().Be(0);

        // Cross-tenant restore is blocked
        using var crossRestoreReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tenantB.Id}/backups/{backupA.Id}/restore",
            superAdmin.AccessToken,
            body: new RestoreTenantBackupRequest(DryRun: true));
        (await client.SendAsync(crossRestoreReq)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Live restore brings back studentA
        using var liveRestoreReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tenantA.Id}/backups/{backupA.Id}/restore",
            superAdmin.AccessToken,
            body: new RestoreTenantBackupRequest(DryRun: false));
        var liveRestoreRes = await client.SendAsync(liveRestoreReq);
        liveRestoreRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var liveRestoreDto = (await liveRestoreRes.Content.ReadFromJsonAsync<BackupRestoreResultDto>(IntegrationTestHelpers.JsonOptions))!;
        liveRestoreDto.RestoredTotal.Should().BeGreaterThanOrEqualTo(1);

        using var getStudentReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/students/{studentA.Id}",
            authA.AccessToken,
            tenantA.Subdomain);
        (await client.SendAsync(getStudentReq)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Backup_retention_purges_old_backups_and_feature_off_stops_scheduled_and_manual_backups()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "bkp-ret", "Starter");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");

        // Set MaxBackupsToKeep = 2
        using var policyReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/backups/policy",
            auth.AccessToken,
            tenant.Subdomain,
            new UpdateTenantBackupPolicyRequest(IsScheduledEnabled: true, RetentionDays: 30, MaxBackupsToKeep: 2, PreferredHourUtc: 20));
        (await client.SendAsync(policyReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        for (var i = 0; i < 3; i++)
        {
            using var runReq = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Post,
                "/api/backups/run",
                auth.AccessToken,
                tenant.Subdomain);
            (await client.SendAsync(runReq)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var workspaceReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/backups",
            auth.AccessToken,
            tenant.Subdomain);
        var workspace = (await (await client.SendAsync(workspaceReq)).Content.ReadFromJsonAsync<TenantBackupWorkspaceDto>(IntegrationTestHelpers.JsonOptions))!;
        workspace.History.Should().HaveCount(2);

        // Turn OFF AutoBackup feature switch
        using var turnOffReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/AutoBackup",
            auth.AccessToken,
            tenant.Subdomain,
            new UpdateFeatureSwitchRequest(IsEnabled: false));
        (await client.SendAsync(turnOffReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Manual backup is now blocked with FEATURE_DISABLED
        using var blockedManualReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/backups/run",
            auth.AccessToken,
            tenant.Subdomain);
        var blockedRes = await client.SendAsync(blockedManualReq);
        blockedRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await blockedRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_DISABLED");
        }

        // Scheduled backup job skips the tenant when AutoBackup is OFF
        using (var scope = factory.Services.CreateScope())
        {
            var backupService = scope.ServiceProvider.GetRequiredService<IBackupAndCleanupService>();
            var scheduledResult = await backupService.RunTenantBackupAsync(tenant.Id, BackupTrigger.Scheduled);
            scheduledResult.Should().BeNull();
        }

        using var supportListReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/super-admin/tenants/{tenant.Id}/backups",
            superAdmin.AccessToken);
        var historyAfterScheduled = (await (await client.SendAsync(supportListReq)).Content.ReadFromJsonAsync<List<TenantBackupRecordDto>>(IntegrationTestHelpers.JsonOptions))!;
        historyAfterScheduled.Should().HaveCount(2);
    }

    [Fact]
    public async Task Timetable_clash_detection_blocks_teacher_batch_and_room_conflicts_and_staff_leave_marks_attendance()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "ent-ops", "Enterprise");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");

        var levelId = await FirstEnabledLevelAsync(client, auth.AccessToken, tenant.Subdomain);

        // Create 2 batches, 2 subjects, and a second teacher
        using var batch1Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/batches", auth.AccessToken, tenant.Subdomain,
            new UpsertBatchRequest("Batch 1", "B01", levelId, null, null, "2026"));
        var batch1 = (await (await client.SendAsync(batch1Req)).Content.ReadFromJsonAsync<BatchDto>(IntegrationTestHelpers.JsonOptions))!;

        using var batch2Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/batches", auth.AccessToken, tenant.Subdomain,
            new UpsertBatchRequest("Batch 2", "B02", levelId, null, null, "2026"));
        var batch2 = (await (await client.SendAsync(batch2Req)).Content.ReadFromJsonAsync<BatchDto>(IntegrationTestHelpers.JsonOptions))!;

        using var sub1Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/subjects", auth.AccessToken, tenant.Subdomain,
            new UpsertSubjectRequest("Mathematics", "MATH", levelId));
        var sub1 = (await (await client.SendAsync(sub1Req)).Content.ReadFromJsonAsync<SubjectDto>(IntegrationTestHelpers.JsonOptions))!;

        using var sub2Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/subjects", auth.AccessToken, tenant.Subdomain,
            new UpsertSubjectRequest("Physics", "PHY", levelId));
        var sub2 = (await (await client.SendAsync(sub2Req)).Content.ReadFromJsonAsync<SubjectDto>(IntegrationTestHelpers.JsonOptions))!;

        using var teacher2Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/users", auth.AccessToken, tenant.Subdomain,
            new CreateTenantUserRequest($"teacher2@{tenant.Subdomain}.lk", "Rohan Silva", "TeacherPass@123!", RoleType.Teacher));
        var teacher2 = (await (await client.SendAsync(teacher2Req)).Content.ReadFromJsonAsync<TenantUserDto>(IntegrationTestHelpers.JsonOptions))!;

        // Create base timetable slot: Monday 08:00-10:00 (480-600), Batch 1, Teacher 1 (admin), Room "Lab 1"
        using var slot1Req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/timetable/slots", auth.AccessToken, tenant.Subdomain,
            new UpsertClassScheduleRequest("Maths Slot", batch1.Id, sub1.Id, auth.User.Id, 1, 480, 600, "Lab 1"));
        (await client.SendAsync(slot1Req)).StatusCode.Should().Be(HttpStatusCode.Created);

        // 1. Teacher clash: same teacher (auth.User.Id), Batch 2, Room "Lab 2", Monday 09:00-11:00
        using var teacherClashReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/timetable/slots", auth.AccessToken, tenant.Subdomain,
            new UpsertClassScheduleRequest("Physics Slot", batch2.Id, sub2.Id, auth.User.Id, 1, 540, 660, "Lab 2"));
        var teacherClashRes = await client.SendAsync(teacherClashReq);
        teacherClashRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 2. Batch clash: same batch (batch1), Teacher 2, Room "Lab 2", Monday 09:00-11:00
        using var batchClashReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/timetable/slots", auth.AccessToken, tenant.Subdomain,
            new UpsertClassScheduleRequest("Physics Slot", batch1.Id, sub2.Id, teacher2.Id, 1, 540, 660, "Lab 2"));
        var batchClashRes = await client.SendAsync(batchClashReq);
        batchClashRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 3. Room clash: Batch 2, Teacher 2, same Room "lab 1", Monday 08:30-09:30
        using var roomClashReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/timetable/slots", auth.AccessToken, tenant.Subdomain,
            new UpsertClassScheduleRequest("Physics Slot", batch2.Id, sub2.Id, teacher2.Id, 1, 510, 570, "lab 1"));
        var roomClashRes = await client.SendAsync(roomClashReq);
        roomClashRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Non-clashing slot succeeds: Batch 2, Teacher 2, Room "Lab 2", Monday 08:00-10:00
        using var okSlotReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/timetable/slots", auth.AccessToken, tenant.Subdomain,
            new UpsertClassScheduleRequest("Physics Slot", batch2.Id, sub2.Id, teacher2.Id, 1, 480, 600, "Lab 2"));
        (await client.SendAsync(okSlotReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        // Staff Leave request & approval flow
        var leaveDate = new DateOnly(2026, 11, 24);
        using var createLeaveReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/staff/leaves", auth.AccessToken, tenant.Subdomain,
            new CreateStaffLeaveRequest(StaffLeaveType.Medical, leaveDate, leaveDate, "Medical appointment", teacher2.Id));
        var createLeaveRes = await client.SendAsync(createLeaveReq);
        createLeaveRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var leave = (await createLeaveRes.Content.ReadFromJsonAsync<StaffLeaveRequestDto>(IntegrationTestHelpers.JsonOptions))!;

        using var approveLeaveReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/staff/leaves/{leave.Id}/review", auth.AccessToken, tenant.Subdomain,
            new ReviewStaffLeaveRequest(Approved: true, ReviewNotes: "Approved by principal"));
        (await client.SendAsync(approveLeaveReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var sheetReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get, $"/api/staff/attendance?date={leaveDate:yyyy-MM-dd}", auth.AccessToken, tenant.Subdomain);
        var sheet = (await (await client.SendAsync(sheetReq)).Content.ReadFromJsonAsync<StaffAttendanceSheetDto>(IntegrationTestHelpers.JsonOptions))!;
        sheet.OnLeaveCount.Should().Be(1);
        sheet.Entries.Should().Contain(e => e.StaffUserId == teacher2.Id && e.Status == nameof(StaffAttendanceStatus.OnLeave));
    }

    [Fact]
    public async Task SuperAdmin_dashboard_platform_backups_announcements_maintenance_mode_and_report_cleanup_work()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var proTenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "pro-clean", "Pro");
        var proAuth = await IntegrationTestHelpers.LoginTenantUserAsync(client, proTenant.Subdomain, $"admin@{proTenant.Subdomain}.lk", "AdminUser@123!");

        // Platform backup with 14-30 day retention validation
        using var badRetentionReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put, "/api/super-admin/platform-backups/retention", superAdmin.AccessToken,
            body: new UpdatePlatformBackupRetentionRequest(5));
        (await client.SendAsync(badRetentionReq)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var runPlatformBackupReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/super-admin/platform-backups/run", superAdmin.AccessToken,
            body: new RunPlatformBackupRequest(RetentionDays: 21));
        var platformBackupRes = await client.SendAsync(runPlatformBackupReq);
        platformBackupRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var platformBackup = (await platformBackupRes.Content.ReadFromJsonAsync<PlatformBackupRecordDto>(IntegrationTestHelpers.JsonOptions))!;
        platformBackup.RetentionDays.Should().Be(21);
        platformBackup.SqlCommandSummary.Should().Contain("BACKUP DATABASE");

        // Publish platform announcement to all tenants
        using var annReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/super-admin/announcements", superAdmin.AccessToken,
            body: new CreatePlatformAnnouncementRequest("Scheduled Upgrade", "System upgrade on Sunday at 11 PM.", AnnouncementSeverity.Info));
        (await client.SendAsync(annReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        using var activeAnnReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get, "/api/announcements/active", proAuth.AccessToken, proTenant.Subdomain);
        var activeAnns = (await (await client.SendAsync(activeAnnReq)).Content.ReadFromJsonAsync<List<PlatformAnnouncementDto>>(IntegrationTestHelpers.JsonOptions))!;
        activeAnns.Should().Contain(a => a.Title == "Scheduled Upgrade");

        // Super Admin dashboard
        using var dashReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get, "/api/super-admin/dashboard", superAdmin.AccessToken);
        var dashRes = await client.SendAsync(dashReq);
        dashRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var dash = (await dashRes.Content.ReadFromJsonAsync<SuperAdminDashboardDto>(IntegrationTestHelpers.JsonOptions))!;
        dash.Tenants.Total.Should().BeGreaterThanOrEqualTo(2);
        dash.SmsUsageByTenant.Should().NotBeEmpty();
        dash.StorageByTenant.Should().NotBeEmpty();

        // Report cleanup on Pro tenant
        using var cleanupReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/settings/cleanup/run", proAuth.AccessToken, proTenant.Subdomain,
            new RunReportCleanupRequest(DryRun: false));
        (await client.SendAsync(cleanupReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Maintenance mode blocks tenant requests with 503 while Super Admin remains operational
        using var maintOnReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put, "/api/super-admin/maintenance", superAdmin.AccessToken,
            body: new UpdateMaintenanceModeRequest(IsMaintenanceMode: true, Message: "Upgrading database"));
        (await client.SendAsync(maintOnReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        try
        {
            using var tenantDuringMaintReq = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Get, "/api/branches", proAuth.AccessToken, proTenant.Subdomain);
            var maintRes = await client.SendAsync(tenantDuringMaintReq);
            maintRes.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            using var doc = JsonDocument.Parse(await maintRes.Content.ReadAsStringAsync());
            doc.RootElement.GetProperty("code").GetString().Should().Be("MAINTENANCE_MODE");
        }
        finally
        {
            using var maintOffReq = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Put, "/api/super-admin/maintenance", superAdmin.AccessToken,
                body: new UpdateMaintenanceModeRequest(IsMaintenanceMode: false, Message: null));
            (await client.SendAsync(maintOffReq)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private static async Task<Guid> FirstEnabledLevelAsync(HttpClient client, string token, string subdomain)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/education-levels", token, subdomain);
        var levels = (await (await client.SendAsync(request)).Content.ReadFromJsonAsync<List<EducationLevelDto>>(IntegrationTestHelpers.JsonOptions))!;
        return levels.First(level => level.IsEnabled).Id;
    }

    private static async Task<StudentDto> CreateStudentAsync(HttpClient client, string token, string subdomain, string name, string phone, Guid levelId)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            token,
            subdomain,
            new UpsertStudentRequest(name, "Guardian", phone, "12 Temple Road, Colombo", levelId));
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentDto>(IntegrationTestHelpers.JsonOptions))!;
    }
}
