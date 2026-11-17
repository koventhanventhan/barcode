using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using EduFlow.Api.Endpoints;
using EduFlow.Application.Academics;
using EduFlow.Application.Attendance;
using EduFlow.Application.Students;
using EduFlow.Domain.Entities;
using EduFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class Phase11SecurityAndPerformanceTests
{
    [Fact]
    public async Task SecurityHeaders_CacheControl_And_CsrfProtection_AreEnforced()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        // 1. Security headers and Cache-Control on public endpoints
        var infoRes = await client.GetAsync("/api/info");
        infoRes.StatusCode.Should().Be(HttpStatusCode.OK);
        infoRes.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        infoRes.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        infoRes.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");
        infoRes.Headers.GetValues("Permissions-Policy").First().Should().Contain("camera=(self)");
        infoRes.Headers.GetValues("Content-Security-Policy").First().Should().Contain("default-src 'self'");
        infoRes.Headers.CacheControl?.Public.Should().BeTrue();

        var brandingRes = await client.GetAsync("/api/public/branding");
        brandingRes.StatusCode.Should().Be(HttpStatusCode.OK);
        brandingRes.Headers.Vary.Should().Contain("Host");
        brandingRes.Headers.Vary.Should().Contain("X-Tenant");

        // 2. CSRF protection blocks untrusted cross-site Origin when refresh cookie is sent
        var superAuth = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(
            client,
            superAuth.AccessToken,
            "sec",
            "Starter",
            "SecAdmin@123!");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "SecAdmin@123!");

        using var evilRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        evilRefresh.Headers.Add("Cookie", $"{AuthEndpoints.RefreshTokenCookieName}={auth.RefreshToken}");
        evilRefresh.Headers.Add("Origin", "https://evil-attacker.example.com");

        var evilRes = await client.SendAsync(evilRefresh);
        evilRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var evilBody = await evilRes.Content.ReadAsStringAsync();
        evilBody.Should().Contain("CSRF_ORIGIN_MISMATCH");

        // Same-origin / configured CORS origin succeeds
        using var trustedRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        trustedRefresh.Headers.Add("Cookie", $"{AuthEndpoints.RefreshTokenCookieName}={auth.RefreshToken}");
        trustedRefresh.Headers.Add("Origin", "http://localhost:5173");

        var trustedRes = await client.SendAsync(trustedRefresh);
        trustedRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InputSanitization_And_AntivirusUploadValidation_RejectMaliciousPayloads()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        // 1. XSS payload in JSON body is rejected by InputSanitizationMiddleware
        var xssRes = await client.PostAsJsonAsync("/api/public/demo-requests", new
        {
            instituteName = "Evil <script>alert(1)</script> Academy",
            contactName = "Attacker",
            email = "attacker@example.com"
        });
        xssRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await xssRes.Content.ReadAsStringAsync()).Should().Contain("UNSAFE_INPUT");

        // 2. EICAR virus signature in student CSV import is rejected with FILE_INFECTED
        var superAuth = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(
            client,
            superAuth.AccessToken,
            "av",
            "Growth",
            "AvAdmin@123!");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AvAdmin@123!");

        using var form = new MultipartFormDataContent();
        var eicarBytes = Encoding.ASCII.GetBytes("X5O!P%@AP[4\\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
        var fileContent = new ByteArrayContent(eicarBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "infected.csv");

        using var importReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students/import",
            auth.AccessToken,
            tenant.Subdomain);
        importReq.Content = form;

        var importRes = await client.SendAsync(importReq);
        importRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await importRes.Content.ReadAsStringAsync()).Should().Contain("FILE_INFECTED");
    }

    [Fact]
    public async Task HotQueryIndexes_PaginationHeaders_And_ScanLatencyTarget_AreMet()
    {
        await using var factory = new ApiFactory();
        await factory.InitializeAsync();

        // 1. Verify hot composite (TenantId, ...) indexes exist in EF Core model
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var studentIndexes = db.Model.FindEntityType(typeof(Student))!
                .GetIndexes()
                .Select(ix => string.Join(",", ix.Properties.Select(p => p.Name)))
                .ToList();
            studentIndexes.Should().Contain("TenantId,StudentCode");
            studentIndexes.Should().Contain("TenantId,Status,EducationLevelId");
            studentIndexes.Should().Contain("TenantId,FullName");

            var sessionIndexes = db.Model.FindEntityType(typeof(ClassSession))!
                .GetIndexes()
                .Select(ix => string.Join(",", ix.Properties.Select(p => p.Name)))
                .ToList();
            sessionIndexes.Should().Contain("TenantId,SessionDate,Status");
        }

        using var client = factory.CreateClient();
        var superAuth = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(
            client,
            superAuth.AccessToken,
            "perf",
            "Growth",
            "PerfAdmin@123!");
        var auth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "PerfAdmin@123!");

        // 2. Pagination headers on list endpoints
        using var tenantsReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/super-admin/tenants?page=1&pageSize=2",
            superAuth.AccessToken);
        var tenantsRes = await client.SendAsync(tenantsReq);
        tenantsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        tenantsRes.Headers.Contains("X-Total-Count").Should().BeTrue();
        tenantsRes.Headers.GetValues("X-Page").Should().Contain("1");
        tenantsRes.Headers.GetValues("X-Page-Size").Should().Contain("2");

        // 3. Scan endpoint p95 < 300ms benchmark
        using var levelsReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/education-levels",
            auth.AccessToken,
            tenant.Subdomain);
        var levels = (await (await client.SendAsync(levelsReq)).Content
            .ReadFromJsonAsync<List<EducationLevelDto>>(IntegrationTestHelpers.JsonOptions))!;
        var levelId = levels.First(l => l.IsEnabled).Id;

        using var batchReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/batches",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertBatchRequest("A/L 2026", "AL26", levelId, null, null, "2026", true));
        var batch = (await (await client.SendAsync(batchReq)).Content
            .ReadFromJsonAsync<BatchDto>(IntegrationTestHelpers.JsonOptions))!;

        using var subjectReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/subjects",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertSubjectRequest("Combined Maths", "CMATH", levelId, true));
        var subject = (await (await client.SendAsync(subjectReq)).Content
            .ReadFromJsonAsync<SubjectDto>(IntegrationTestHelpers.JsonOptions))!;

        using var scheduleReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/class-schedules",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertClassScheduleRequest("Morning Maths", batch.Id, subject.Id, auth.User.Id, 1, 480, 600, "Hall A", true));
        var schedule = (await (await client.SendAsync(scheduleReq)).Content
            .ReadFromJsonAsync<ClassScheduleDto>(IntegrationTestHelpers.JsonOptions))!;

        using var studentReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertStudentRequest(
                FullName: "Kavindi Perera",
                GuardianName: "Sunil Perera",
                GuardianPhone: "+94771234567",
                Address: "Colombo",
                EducationLevelId: levelId,
                Gender: "Female",
                Status: "Active",
                BatchIds: [batch.Id]));
        var student = (await (await client.SendAsync(studentReq)).Content
            .ReadFromJsonAsync<StudentDto>(IntegrationTestHelpers.JsonOptions))!;

        using var openReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/attendance/sessions",
            auth.AccessToken,
            tenant.Subdomain,
            new OpenClassSessionRequest(schedule.Id, null));
        var session = (await (await client.SendAsync(openReq)).Content
            .ReadFromJsonAsync<ClassSessionDto>(IntegrationTestHelpers.JsonOptions))!;

        using var startReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/attendance/sessions/{session.Id}/start",
            auth.AccessToken,
            tenant.Subdomain,
            new { });
        (await client.SendAsync(startReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Warmup scan
        using (var warmup = IntegrationTestHelpers.CreateTenantRequest(
                   HttpMethod.Post,
                   "/api/attendance/scan",
                   auth.AccessToken,
                   tenant.Subdomain,
                   new ScanAttendanceRequest(student.StudentCode, session.Id)))
        {
            (await client.SendAsync(warmup)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var durationsMs = new List<double>(20);
        for (var i = 0; i < 20; i++)
        {
            var sw = Stopwatch.StartNew();
            using var scanReq = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Post,
                "/api/attendance/scan",
                auth.AccessToken,
                tenant.Subdomain,
                new ScanAttendanceRequest(student.StudentCode, session.Id));
            var scanRes = await client.SendAsync(scanReq);
            sw.Stop();
            scanRes.StatusCode.Should().Be(HttpStatusCode.OK);
            durationsMs.Add(sw.Elapsed.TotalMilliseconds);
        }

        durationsMs.Sort();
        var p95 = durationsMs[(int)Math.Ceiling(durationsMs.Count * 0.95) - 1];
        p95.Should().BeLessThan(300, "scan endpoint p95 must stay under 300 ms");
    }
}
