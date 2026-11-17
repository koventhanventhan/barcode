using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Api.Endpoints;
using EduFlow.Application.Academics;
using EduFlow.Application.Auth;
using EduFlow.Application.Fees;
using EduFlow.Application.Parents;
using EduFlow.Application.Students;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Notifications;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;

namespace EduFlow.IntegrationTests;

public sealed class ParentPortalTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Parent_can_switch_siblings_and_cannot_read_another_child()
    {
        var client = factory.CreateClient();
        var seed = await SeedInstituteAsync(client, "par-sib", "Pro");
        var outsider = await CreateStudentAsync(client, seed, "Outsider Child", "+94770000001");
        var parent = await CreateParentAsync(client, seed, "07771001001", [seed.StudentA, seed.StudentB]);
        var otherParent = await CreateParentAsync(client, seed, "07771001002", [outsider.Id], password: "OtherParent@123!");

        var home = await SendAsync<ParentHomeDto>(client, seed, parent, HttpMethod.Get, "/api/parent/children");
        home.Children.Select(c => c.Id).Should().BeEquivalentTo([seed.StudentA, seed.StudentB]);

        (await SendRawAsync(client, seed, parent, HttpMethod.Get, $"/api/parent/children/{seed.StudentA}/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendRawAsync(client, seed, parent, HttpMethod.Get, $"/api/parent/children/{seed.StudentB}/dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = await SendRawAsync(client, seed, parent, HttpMethod.Get, $"/api/parent/children/{outsider.Id}/dashboard");
        blocked.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var otherHome = await SendAsync<ParentHomeDto>(client, seed, otherParent, HttpMethod.Get, "/api/parent/children");
        otherHome.Children.Should().ContainSingle(c => c.Id == outsider.Id);
        (await SendRawAsync(client, seed, otherParent, HttpMethod.Get, $"/api/parent/children/{seed.StudentA}/invoices")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Parent_portal_switch_blocks_login_and_every_parent_endpoint()
    {
        var client = factory.CreateClient();
        var seed = await SeedInstituteAsync(client, "par-off", "Starter");
        var parent = await CreateParentAsync(client, seed, "07771002001", [seed.StudentA]);

        (await SendRawAsync(client, seed, parent, HttpMethod.Get, "/api/parent/portal")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendRawAsync(client, seed, parent, HttpMethod.Get, "/api/parent/messages")).StatusCode.Should().Be(HttpStatusCode.OK);

        await SetFeatureAsync(client, seed, FeatureKey.ParentPortal, false);

        var api = await SendRawAsync(client, seed, parent, HttpMethod.Get, "/api/parent/children");
        api.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await api.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_DISABLED");
            doc.RootElement.GetProperty("feature").GetString().Should().Be("ParentPortal");
        }

        var login = await LoginRawAsync(client, seed.Tenant.Subdomain, "07771002001", "ParentPass@123!");
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("feature").GetString().Should().Be("ParentPortal");
        }
    }

    [Fact]
    public async Task Marks_switch_blocks_report_cards()
    {
        var client = factory.CreateClient();
        var seed = await SeedInstituteAsync(client, "par-marks", "Pro");
        var parent = await CreateParentAsync(client, seed, "07771003001", [seed.StudentA]);

        await SetFeatureAsync(client, seed, FeatureKey.ParentCanSeeMarks, false);
        var hidden = await SendRawAsync(
            client,
            seed,
            parent,
            HttpMethod.Get,
            $"/api/parent/children/{seed.StudentA}/exams/{Guid.NewGuid()}/report-card");
        hidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await hidden.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("feature").GetString().Should().Be(nameof(FeatureKey.ParentCanSeeMarks));
        }

        await SetFeatureAsync(client, seed, FeatureKey.ParentCanSeeMarks, true);
        var missing = await SendRawAsync(
            client,
            seed,
            parent,
            HttpMethod.Get,
            $"/api/parent/children/{seed.StudentA}/exams/{Guid.NewGuid()}/report-card");
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var doc = JsonDocument.Parse(await missing.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("EXAM_NOT_FOUND");
        }
    }

    [Fact]
    public async Task Parent_pay_online_starts_payhere_checkout_for_a_linked_invoice_only()
    {
        var client = factory.CreateClient();
        var seed = await SeedInstituteAsync(client, "par-pay", "Enterprise");
        using var structureReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/structures",
            seed.Admin.AccessToken,
            seed.Tenant.Subdomain,
            new UpsertFeeStructureRequest("Monthly Tuition", FeeFrequency.Monthly, 2500m, seed.LevelId, seed.BatchId, 10));
        var structure = (await (await client.SendAsync(structureReq)).Content.ReadFromJsonAsync<FeeStructureDto>(IntegrationTestHelpers.JsonOptions))!;

        using var invoiceReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices",
            seed.Admin.AccessToken,
            seed.Tenant.Subdomain,
            new CreateSingleInvoiceRequest(seed.StudentA, structure.Id, "2026-11"));
        var invoice = (await (await client.SendAsync(invoiceReq)).Content.ReadFromJsonAsync<FeeInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;

        using var otherInvoiceReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/fees/invoices",
            seed.Admin.AccessToken,
            seed.Tenant.Subdomain,
            new CreateSingleInvoiceRequest(seed.StudentB, structure.Id, "2026-11"));
        var otherInvoice = (await (await client.SendAsync(otherInvoiceReq)).Content.ReadFromJsonAsync<FeeInvoiceDto>(IntegrationTestHelpers.JsonOptions))!;

        using var gatewayReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/fees/payhere/settings",
            seed.Admin.AccessToken,
            seed.Tenant.Subdomain,
            new UpdatePayHereGatewaySettingsRequest("1211144", "PARENT_PORTAL_SECRET", true, true));
        (await client.SendAsync(gatewayReq)).EnsureSuccessStatusCode();

        var parent = await CreateParentAsync(client, seed, "07771004001", [seed.StudentA]);
        var checkout = await SendAsync<PayHereCheckoutSessionDto>(
            client,
            seed,
            parent,
            HttpMethod.Post,
            $"/api/parent/children/{seed.StudentA}/invoices/{invoice.Id}/pay",
            new ParentPayInvoiceRequest());
        checkout.OrderId.Should().StartWith("PH-");
        checkout.Amount.Should().Be(invoice.BalanceAmount);
        checkout.StudentId.Should().Be(seed.StudentA);

        var other = await SendRawAsync(
            client,
            seed,
            parent,
            HttpMethod.Post,
            $"/api/parent/children/{seed.StudentB}/invoices/{otherInvoice.Id}/pay",
            new ParentPayInvoiceRequest());
        other.StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetFeatureAsync(client, seed, FeatureKey.ParentCanPayOnline, false);
        var blocked = await SendRawAsync(
            client,
            seed,
            parent,
            HttpMethod.Post,
            $"/api/parent/children/{seed.StudentA}/invoices/{invoice.Id}/pay",
            new ParentPayInvoiceRequest());
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var doc = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("feature").GetString().Should().Be(nameof(FeatureKey.ParentCanPayOnline));
    }

    [Fact]
    public async Task Invite_sms_and_mobile_password_reset_sign_the_parent_in()
    {
        var client = factory.CreateClient();
        var seed = await SeedInstituteAsync(client, "par-inv", "Growth");
        var sms = factory.Services.GetRequiredService<FakeSmsStore>();
        const string phone = "07771005001";

        using var createReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/parents",
            seed.Admin.AccessToken,
            seed.Tenant.Subdomain,
            new CreateParentAccountRequest("Rani Fernando", phone, [seed.StudentA], SendInvite: true));
        var created = await client.SendAsync(createReq);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var account = (await created.Content.ReadFromJsonAsync<ParentAccountDto>(IntegrationTestHelpers.JsonOptions))!;
        account.InvitePending.Should().BeTrue();
        account.Email.Should().BeNull();

        var invite = sms.Messages.Last(m => m.Message.Contains("invite-token:", StringComparison.Ordinal) && m.To.EndsWith("771005001", StringComparison.Ordinal));
        var inviteToken = invite.Message.Split("invite-token:", StringSplitOptions.None)[1].Split(' ')[0].Trim();

        using var acceptReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/accept-invite")
        {
            Content = JsonContent.Create(new AcceptInviteRequest(null, inviteToken, "ParentPass@123!", "Rani Fernando", phone))
        };
        acceptReq.Headers.Add("X-Tenant", seed.Tenant.Subdomain);
        (await client.SendAsync(acceptReq)).EnsureSuccessStatusCode();

        (await LoginRawAsync(client, seed.Tenant.Subdomain, phone, "ParentPass@123!")).EnsureSuccessStatusCode();

        using var forgotReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new ForgotPasswordRequest(Phone: phone))
        };
        forgotReq.Headers.Add("X-Tenant", seed.Tenant.Subdomain);
        (await client.SendAsync(forgotReq)).EnsureSuccessStatusCode();

        var reset = sms.Messages.Last(m => m.Message.Contains("reset-token:", StringComparison.Ordinal) && m.To.EndsWith("771005001", StringComparison.Ordinal));
        var resetToken = reset.Message.Split("reset-token:", StringSplitOptions.None)[1].Split(' ')[0].Trim();

        using var resetReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(null, resetToken, "NewParent@123!", phone))
        };
        resetReq.Headers.Add("X-Tenant", seed.Tenant.Subdomain);
        (await client.SendAsync(resetReq)).EnsureSuccessStatusCode();
        (await LoginRawAsync(client, seed.Tenant.Subdomain, phone, "NewParent@123!")).EnsureSuccessStatusCode();
    }

    private static async Task<SeededInstitute> SeedInstituteAsync(HttpClient client, string prefix, string plan)
    {
        var super = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, super.AccessToken, prefix, plan);
        var admin = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");
        using var levelsReq = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/education-levels", admin.AccessToken, tenant.Subdomain);
        var levels = (await (await client.SendAsync(levelsReq)).Content.ReadFromJsonAsync<List<EducationLevelDto>>(IntegrationTestHelpers.JsonOptions))!;
        var levelId = levels.First(l => l.IsEnabled).Id;
        using var batchReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/batches",
            admin.AccessToken,
            tenant.Subdomain,
            new UpsertBatchRequest("Parent Batch", $"PB{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}", levelId, null, null, "2026"));
        var batch = (await (await client.SendAsync(batchReq)).Content.ReadFromJsonAsync<BatchDto>(IntegrationTestHelpers.JsonOptions))!;
        var seed = new SeededInstitute(admin, tenant, levelId, batch.Id, Guid.Empty, Guid.Empty);
        var a = await CreateStudentAsync(client, seed, "Amali Perera", "+94771110001");
        var b = await CreateStudentAsync(client, seed, "Bimsara Perera", "+94771110002");
        return seed with { StudentA = a.Id, StudentB = b.Id };
    }

    private static async Task<StudentDto> CreateStudentAsync(HttpClient client, SeededInstitute seed, string name, string phone)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            seed.Admin.AccessToken,
            seed.Tenant.Subdomain,
            new UpsertStudentRequest(name, "Guardian", phone, "Colombo", seed.LevelId, BatchIds: [seed.BatchId]));
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<StudentDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<AuthResponse> CreateParentAsync(
        HttpClient client,
        SeededInstitute seed,
        string phone,
        IReadOnlyList<Guid> studentIds,
        string password = "ParentPass@123!")
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/parents",
            seed.Admin.AccessToken,
            seed.Tenant.Subdomain,
            new CreateParentAccountRequest("Parent User", phone, studentIds, Password: password));
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        var login = await LoginRawAsync(client, seed.Tenant.Subdomain, phone, password);
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<AuthResponse>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task SetFeatureAsync(HttpClient client, SeededInstitute seed, FeatureKey key, bool enabled)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            $"/api/settings/features/{key}",
            seed.Admin.AccessToken,
            seed.Tenant.Subdomain,
            new UpdateFeatureSwitchRequest(enabled));
        (await client.SendAsync(req)).EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> LoginRawAsync(HttpClient client, string subdomain, string phone, string password)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(null, password, phone))
        };
        req.Headers.Add("X-Tenant", subdomain);
        return await client.SendAsync(req);
    }

    private static async Task<T> SendAsync<T>(HttpClient client, SeededInstitute seed, AuthResponse parent, HttpMethod method, string url, object? body = null)
    {
        var response = await SendRawAsync(client, seed, parent, method, url, body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static async Task<HttpResponseMessage> SendRawAsync(
        HttpClient client,
        SeededInstitute seed,
        AuthResponse parent,
        HttpMethod method,
        string url,
        object? body = null)
    {
        using var req = IntegrationTestHelpers.CreateTenantRequest(method, url, parent.AccessToken, seed.Tenant.Subdomain, body);
        return await client.SendAsync(req);
    }

    private sealed record SeededInstitute(
        AuthResponse Admin,
        EduFlow.Application.SuperAdmin.TenantDetailDto Tenant,
        Guid LevelId,
        Guid BatchId,
        Guid StudentA,
        Guid StudentB);
}
