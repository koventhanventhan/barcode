using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduFlow.Application.Auth;
using EduFlow.Application.Marketing;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Marketing;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class MarketingFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Signup_stores_a_trial_sends_welcome_and_tracks_onboarding()
    {
        var client = factory.CreateClient();
        var subdomain = $"p10{Guid.NewGuid():N}"[..12];
        var email = $"{subdomain}@admino.test";
        var availability = await client.GetAsync($"/api/public/subdomains/{subdomain}/availability");
        availability.StatusCode.Should().Be(HttpStatusCode.OK);
        (await availability.Content.ReadFromJsonAsync<SubdomainAvailabilityDto>(IntegrationTestHelpers.JsonOptions))!
            .Available.Should().BeTrue();

        var reserved = await client.GetAsync("/api/public/subdomains/demo/availability");
        (await reserved.Content.ReadFromJsonAsync<SubdomainAvailabilityDto>(IntegrationTestHelpers.JsonOptions))!
            .Available.Should().BeFalse();

        var signup = await client.PostAsJsonAsync("/api/public/signup", new SignupRequest(
            "Phase Ten Institute",
            subdomain,
            "Amal Fernando",
            email,
            "TrialAdmin@123!",
            "Starter",
            "0771230099"));
        signup.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await signup.Content.ReadFromJsonAsync<SignupResultDto>(IntegrationTestHelpers.JsonOptions))!;
        created.Subdomain.Should().Be(subdomain);
        (created.TrialEndsAt - DateTimeOffset.UtcNow).TotalDays.Should().BeGreaterThan(13);

        var emails = factory.Services.GetRequiredService<InMemoryEmailStore>();
        emails.FindLatest(email, "Welcome").Should().NotBeNull();

        var login = await IntegrationTestHelpers.LoginTenantUserAsync(client, subdomain, email, "TrialAdmin@123!");
        using var checklistRequest = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/onboarding", login.AccessToken, subdomain);
        var checklist = (await (await client.SendAsync(checklistRequest)).Content.ReadFromJsonAsync<OnboardingChecklistDto>(IntegrationTestHelpers.JsonOptions))!;
        checklist.Total.Should().Be(5);
        checklist.Steps.Single(step => step.Key == "batches").Done.Should().BeFalse();

        using var printedRequest = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Post, "/api/onboarding/cards-printed", login.AccessToken, subdomain);
        var printed = (await (await client.SendAsync(printedRequest)).Content.ReadFromJsonAsync<OnboardingChecklistDto>(IntegrationTestHelpers.JsonOptions))!;
        printed.Steps.Single(step => step.Key == "cards").Done.Should().BeTrue();

        using var scope = factory.Services.CreateScope();
        var notices = scope.ServiceProvider.GetRequiredService<ITrialNoticeService>();
        var ending = await notices.SendDueNoticesAsync(created.TrialEndsAt.AddDays(-3));
        ending.Should().BeGreaterThan(0);
        emails.FindLatest(email, "TrialEnding").Should().NotBeNull();
        var again = await notices.SendDueNoticesAsync(created.TrialEndsAt.AddDays(-3));
        again.Should().Be(0);
        var expired = await notices.SendDueNoticesAsync(created.TrialEndsAt.AddMinutes(1));
        expired.Should().BeGreaterThan(0);
        emails.FindLatest(email, "TrialExpired").Should().NotBeNull();
    }

    [Fact]
    public async Task Demo_request_is_stored_and_emailed_and_demo_login_is_read_only()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/public/demo-requests", new DemoRequestInput(
            "Hillside Tuition",
            "Kumari Silva",
            "kumari@hillside.test",
            "0775550101",
            "Kandy",
            180,
            "Please show barcode attendance."));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.Services.GetRequiredService<InMemoryEmailStore>().FindLatest("superadmin@admino.lk", "DemoRequest")
            .Should().NotBeNull();

        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<AppDbContext>().DemoRequests
            .IgnoreQueryFilters()
            .AnyAsync(row => row.Email == "kumari@hillside.test");
        stored.Should().BeTrue();

        var login = await client.PostAsJsonAsync("/api/public/demo/login", new { });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(IntegrationTestHelpers.JsonOptions))!;
        using var write = new HttpRequestMessage(HttpMethod.Post, "/api/students");
        write.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        write.Headers.Add("X-Tenant", "demo");
        write.Content = JsonContent.Create(new { fullName = "Should Fail" });
        var denied = await client.SendAsync(write);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync()).Should().Contain("DEMO_READ_ONLY");
    }

    [Fact]
    public async Task Demo_history_covers_six_months_and_nightly_reset_rebuilds_it()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var demoId = await db.Tenants.IgnoreQueryFilters()
            .Where(t => !t.IsDeleted && t.Subdomain == "demo")
            .Select(t => t.Id)
            .SingleAsync();
        var levelId = await db.EducationLevels.IgnoreQueryFilters()
            .Where(level => !level.IsDeleted && level.Code == "OL")
            .Select(level => level.Id)
            .SingleAsync();

        db.Students.Add(new Student
        {
            TenantId = demoId,
            StudentCode = "S999999",
            FullName = "Reset Marker",
            GuardianName = "Marker",
            GuardianPhone = "0770000000",
            Address = "1 Marker Road",
            EducationLevelId = levelId,
            AdmissionDate = new DateOnly(2026, 1, 1),
            Status = StudentStatus.Active
        });
        var tenantContext = scope.ServiceProvider.GetRequiredService<EduFlow.Application.Common.Interfaces.ITenantContext>();
        using (tenantContext.BeginSystemScope(demoId))
        {
            await db.SaveChangesAsync();
        }

        var reset = scope.ServiceProvider.GetRequiredService<IDemoTenantResetService>();
        await reset.ResetAsync();

        using var check = factory.Services.CreateScope();
        var checkDb = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var students = await checkDb.Students.IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.TenantId == demoId)
            .Select(s => s.FullName)
            .ToListAsync();
        students.Should().NotContain("Reset Marker");
        students.Should().Contain(name => name.Contains('க') || name.Contains('அ') || name.Contains("அருண்"));
        students.Should().Contain(name => name.Contains('ක') || name.Contains("කසුන්"));

        var dates = await checkDb.ClassSessions.IgnoreQueryFilters()
            .Where(s => !s.IsDeleted && s.TenantId == demoId)
            .Select(s => s.SessionDate)
            .ToListAsync();
        dates.Should().NotBeEmpty();
        (dates.Max().DayNumber - dates.Min().DayNumber).Should().BeGreaterThan(150);

        var periods = await checkDb.FeeInvoices.IgnoreQueryFilters()
            .Where(i => !i.IsDeleted && i.TenantId == demoId)
            .Select(i => i.BillingPeriod)
            .Distinct()
            .CountAsync();
        periods.Should().BeGreaterOrEqualTo(6);

        var exams = await checkDb.Exams.IgnoreQueryFilters().CountAsync(e => !e.IsDeleted && e.TenantId == demoId);
        exams.Should().BeGreaterThan(0);
    }
}
