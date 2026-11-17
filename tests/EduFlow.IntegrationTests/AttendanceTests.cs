using System.Net;
using System.Net.Http.Json;
using EduFlow.Application.Academics;
using EduFlow.Application.Attendance;
using EduFlow.Application.Auth;
using EduFlow.Application.Notifications;
using EduFlow.Application.Students;
using EduFlow.Domain.Enums;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace EduFlow.IntegrationTests;

public sealed class AttendanceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Session_lifecycle_marks_scans_once_and_finalizes_absences()
    {
        var client = factory.CreateClient();
        var (auth, tenant, studentCode, otherCode) = await SeedClassAsync(client, "attend-life", "Starter");

        using var openReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/attendance/sessions", auth.AccessToken, tenant.Subdomain,
            new OpenClassSessionRequest(await LatestScheduleIdAsync(client, auth, tenant.Subdomain)));
        var openRes = await client.SendAsync(openReq);
        openRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = (await openRes.Content.ReadFromJsonAsync<ClassSessionDto>(IntegrationTestHelpers.JsonOptions))!;

        using var earlyStop = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/attendance/sessions/{session.Id}/stop", auth.AccessToken, tenant.Subdomain);
        (await client.SendAsync(earlyStop)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var startReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/attendance/sessions/{session.Id}/start", auth.AccessToken, tenant.Subdomain);
        var startRes = await client.SendAsync(startReq);
        startRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = (await startRes.Content.ReadFromJsonAsync<ClassSessionDto>(IntegrationTestHelpers.JsonOptions))!;
        started.Status.Should().Be(nameof(ClassSessionStatus.Started));

        using var restart = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/attendance/sessions/{session.Id}/start", auth.AccessToken, tenant.Subdomain);
        (await client.SendAsync(restart)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var first = await ScanAsync(client, auth.AccessToken, tenant.Subdomain, studentCode, session.Id);
        first.Outcome.Should().Be(AttendanceRules.OutcomeMarked);
        first.Status.Should().Be(nameof(AttendanceMarkStatus.Present));

        var duplicate = await ScanAsync(client, auth.AccessToken, tenant.Subdomain, studentCode, session.Id);
        duplicate.Outcome.Should().Be(AttendanceRules.OutcomeAlreadyMarked);
        duplicate.Message.Should().Contain("Already marked at");

        using var unknown = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/attendance/scan", auth.AccessToken, tenant.Subdomain,
            new ScanAttendanceRequest("S999999", session.Id));
        (await client.SendAsync(unknown)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var stopReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/attendance/sessions/{session.Id}/stop", auth.AccessToken, tenant.Subdomain);
        var stopRes = await client.SendAsync(stopReq);
        stopRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var stopped = (await stopRes.Content.ReadFromJsonAsync<ClassSessionDto>(IntegrationTestHelpers.JsonOptions))!;
        stopped.Status.Should().Be(nameof(ClassSessionStatus.Stopped));
        stopped.PresentCount.Should().Be(1);
        stopped.AbsentCount.Should().Be(1);

        using var registerReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get, $"/api/attendance/register?date={session.SessionDate:yyyy-MM-dd}", auth.AccessToken, tenant.Subdomain);
        var register = (await (await client.SendAsync(registerReq)).Content.ReadFromJsonAsync<List<AttendanceRegisterSessionDto>>(IntegrationTestHelpers.JsonOptions))!;
        register.Single().Students.Should().Contain(item => item.StudentCode == otherCode && item.Status == nameof(AttendanceMarkStatus.Absent));

        var startedMessages = await MessageCountAsync(client, auth.AccessToken, tenant.Subdomain, NotificationMessageType.ClassStarted);
        var presentMessages = await MessageCountAsync(client, auth.AccessToken, tenant.Subdomain, NotificationMessageType.AttendancePresent);
        var absentMessages = await MessageCountAsync(client, auth.AccessToken, tenant.Subdomain, NotificationMessageType.AttendanceAbsent);
        startedMessages.Should().Be(2);
        presentMessages.Should().Be(1);
        absentMessages.Should().Be(1);
    }

    [Fact]
    public async Task Switches_off_skip_class_and_attendance_messages()
    {
        var client = factory.CreateClient();
        var (auth, tenant, studentCode, _) = await SeedClassAsync(client, "attend-off", "Starter");
        await SetFeatureAsync(client, auth.AccessToken, tenant.Subdomain, FeatureKey.ClassSessionSms, false);
        await SetFeatureAsync(client, auth.AccessToken, tenant.Subdomain, FeatureKey.AttendanceSms, false);

        using var openReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/attendance/sessions", auth.AccessToken, tenant.Subdomain,
            new OpenClassSessionRequest(await LatestScheduleIdAsync(client, auth, tenant.Subdomain)));
        var session = (await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<ClassSessionDto>(IntegrationTestHelpers.JsonOptions))!;
        using var startReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/attendance/sessions/{session.Id}/start", auth.AccessToken, tenant.Subdomain);
        (await client.SendAsync(startReq)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ScanAsync(client, auth.AccessToken, tenant.Subdomain, studentCode, session.Id)).Outcome.Should().Be(AttendanceRules.OutcomeMarked);
        using var stopReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/attendance/sessions/{session.Id}/stop", auth.AccessToken, tenant.Subdomain);
        (await client.SendAsync(stopReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await MessageCountAsync(client, auth.AccessToken, tenant.Subdomain, NotificationMessageType.ClassStarted)).Should().Be(0);
        (await MessageCountAsync(client, auth.AccessToken, tenant.Subdomain, NotificationMessageType.AttendancePresent)).Should().Be(0);
        (await MessageCountAsync(client, auth.AccessToken, tenant.Subdomain, NotificationMessageType.AttendanceAbsent)).Should().Be(0);
    }

    [Fact]
    public async Task Scan_cannot_see_another_tenants_student()
    {
        var client = factory.CreateClient();
        var super = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenantA = await IntegrationTestHelpers.CreateTestTenantAsync(client, super.AccessToken, "attend-a");
        var tenantB = await IntegrationTestHelpers.CreateTestTenantAsync(client, super.AccessToken, "attend-b");
        var authA = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenantA.Subdomain, $"admin@{tenantA.Subdomain}.lk", "AdminUser@123!");
        var seededB = await SeedClassAsync(client, "attend-bseed", "Starter", tenantB, super);
        using var request = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/attendance/scan", authA.AccessToken, tenantA.Subdomain,
            new ScanAttendanceRequest(seededB.StudentCode));
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Online_join_is_plan_gated_and_marks_the_named_student()
    {
        var client = factory.CreateClient();
        var starter = await SeedClassAsync(client, "attend-join-no", "Starter");
        using var denied = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/attendance/join", starter.Auth.AccessToken, starter.Tenant.Subdomain,
            new JoinAttendanceRequest("ABCDEF", starter.StudentCode));
        (await client.SendAsync(denied)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var enterprise = await SeedClassAsync(client, "attend-join", "Enterprise");
        using var openReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/attendance/sessions", enterprise.Auth.AccessToken, enterprise.Tenant.Subdomain,
            new OpenClassSessionRequest(await LatestScheduleIdAsync(client, enterprise.Auth, enterprise.Tenant.Subdomain)));
        var session = (await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<ClassSessionDto>(IntegrationTestHelpers.JsonOptions))!;
        using var startReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, $"/api/attendance/sessions/{session.Id}/start", enterprise.Auth.AccessToken, enterprise.Tenant.Subdomain);
        var started = (await (await client.SendAsync(startReq)).Content.ReadFromJsonAsync<ClassSessionDto>(IntegrationTestHelpers.JsonOptions))!;
        started.JoinCode.Should().NotBeNullOrWhiteSpace();

        using var join = new HttpRequestMessage(HttpMethod.Post, "/api/attendance/join")
        {
            Content = JsonContent.Create(new JoinAttendanceRequest(started.JoinCode!, enterprise.StudentCode))
        };
        join.Headers.Add("X-Tenant", enterprise.Tenant.Subdomain);
        var joinRes = await client.SendAsync(join);
        joinRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var marked = (await joinRes.Content.ReadFromJsonAsync<ScanAttendanceResultDto>(IntegrationTestHelpers.JsonOptions))!;
        marked.Outcome.Should().Be(AttendanceRules.OutcomeMarked);
        marked.StudentCode.Should().Be(enterprise.StudentCode);
    }

    [Fact]
    public async Task Attendance_hub_requires_an_authenticated_watcher()
    {
        var client = factory.CreateClient();
        var anonymous = BuildHub(null, null);
        var act = async () => await anonymous.StartAsync();
        await act.Should().ThrowAsync<HttpRequestException>();

        var (auth, tenant, _, _) = await SeedClassAsync(client, "attend-hub", "Starter");
        var connection = BuildHub(auth.AccessToken, tenant.Subdomain);
        await connection.StartAsync();
        connection.State.Should().Be(HubConnectionState.Connected);
        await connection.DisposeAsync();
    }

    private HubConnection BuildHub(string? token, string? tenant)
    {
        var client = factory.CreateClient();
        return new HubConnectionBuilder()
            .WithUrl(new Uri(client.BaseAddress!, "hubs/attendance"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                if (!string.IsNullOrWhiteSpace(token))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                }

                if (!string.IsNullOrWhiteSpace(tenant))
                {
                    options.Headers.Add("X-Tenant", tenant);
                }
            })
            .Build();
    }

    private async Task<SeededClass> SeedClassAsync(
        HttpClient client,
        string prefix,
        string plan,
        EduFlow.Application.SuperAdmin.TenantDetailDto? existing = null,
        AuthResponse? super = null)
    {
        super ??= await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = existing ?? await IntegrationTestHelpers.CreateTestTenantAsync(client, super.AccessToken, prefix, plan);
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");
        using var levelsReq = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/education-levels", auth.AccessToken, tenant.Subdomain);
        var levels = (await (await client.SendAsync(levelsReq)).Content.ReadFromJsonAsync<List<EducationLevelDto>>(IntegrationTestHelpers.JsonOptions))!;
        var levelId = levels.First(level => level.IsEnabled).Id;
        using var batchReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/batches", auth.AccessToken, tenant.Subdomain,
            new UpsertBatchRequest("Morning", $"AM{prefix[..Math.Min(4, prefix.Length)]}{Guid.NewGuid().ToString("N")[..4]}", levelId, null, null, "2026"));
        var batch = (await (await client.SendAsync(batchReq)).Content.ReadFromJsonAsync<BatchDto>(IntegrationTestHelpers.JsonOptions))!;
        using var subjectReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/subjects", auth.AccessToken, tenant.Subdomain,
            new UpsertSubjectRequest("Maths", $"M{Guid.NewGuid().ToString("N")[..6]}", levelId));
        var subject = (await (await client.SendAsync(subjectReq)).Content.ReadFromJsonAsync<SubjectDto>(IntegrationTestHelpers.JsonOptions))!;
        using var scheduleReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/class-schedules", auth.AccessToken, tenant.Subdomain,
            new UpsertClassScheduleRequest("Combined Maths", batch.Id, subject.Id, auth.User.Id, 1, 960, 1080, "Hall 1"));
        (await client.SendAsync(scheduleReq)).EnsureSuccessStatusCode();
        var first = await CreateStudentAsync(client, auth, tenant.Subdomain, "Nimal Perera", "0771111111", levelId, batch.Id);
        var second = await CreateStudentAsync(client, auth, tenant.Subdomain, "Amaya Silva", "0772222222", levelId, batch.Id);
        return new SeededClass(auth, tenant, first, second);
    }

    private static async Task<Guid> LatestScheduleIdAsync(HttpClient client, AuthResponse auth, string subdomain)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/class-schedules", auth.AccessToken, subdomain);
        var schedules = (await (await client.SendAsync(request)).Content.ReadFromJsonAsync<List<ClassScheduleDto>>(IntegrationTestHelpers.JsonOptions))!;
        return schedules.Last().Id;
    }

    private static async Task<string> CreateStudentAsync(HttpClient client, AuthResponse auth, string subdomain, string name, string phone, Guid levelId, Guid batchId)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/students", auth.AccessToken, subdomain,
            new UpsertStudentRequest(name, "Guardian", phone, "Colombo", levelId, BatchIds: [batchId]));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var student = (await response.Content.ReadFromJsonAsync<StudentDto>(IntegrationTestHelpers.JsonOptions))!;
        return student.StudentCode;
    }

    private static async Task<ScanAttendanceResultDto> ScanAsync(HttpClient client, string token, string subdomain, string code, Guid sessionId)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/attendance/scan", token, subdomain, new ScanAttendanceRequest(code, sessionId));
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ScanAttendanceResultDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<int> MessageCountAsync(HttpClient client, string token, string subdomain, NotificationMessageType type)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get, $"/api/notifications/messages?messageType={type}&pageSize=100", token, subdomain);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var log = (await response.Content.ReadFromJsonAsync<NotificationMessageLogResponse>(IntegrationTestHelpers.JsonOptions))!;
        return log.TotalCount;
    }

    private static async Task SetFeatureAsync(HttpClient client, string token, string subdomain, FeatureKey feature, bool enabled)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put, $"/api/settings/features/{feature}", token, subdomain, new EduFlow.Api.Endpoints.UpdateFeatureSwitchRequest(enabled));
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private sealed record SeededClass(AuthResponse Auth, EduFlow.Application.SuperAdmin.TenantDetailDto Tenant, string StudentCode, string OtherCode);
}
