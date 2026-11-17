using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using FluentAssertions;

namespace EduFlow.IntegrationTests;

public sealed class SubscriptionAndSuperAdminTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Expired_subscription_is_read_only_and_suspended_subscription_is_completely_blocked()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);

        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(
            client,
            superAdmin.AccessToken,
            "sub-state",
            planCode: "Pro");

        var adminAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AdminUser@123!");

        using var expireReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tenant.Id}/subscription/extend",
            superAdmin.AccessToken,
            body: new ExtendSubscriptionRequest(NewEndDate: DateTimeOffset.UtcNow.AddDays(-1)));
        (await client.SendAsync(expireReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var getWhenExpiredReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/branches",
            adminAuth.AccessToken,
            tenant.Subdomain);
        (await client.SendAsync(getWhenExpiredReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var postWhenExpiredReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/branches",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new CreateBranchRequest("Matara Branch", "MTR"));
        var expiredWriteRes = await client.SendAsync(postWhenExpiredReq);
        expiredWriteRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await expiredWriteRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("SUBSCRIPTION_EXPIRED_READ_ONLY");
        }

        using var activateReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tenant.Id}/subscription/activate",
            superAdmin.AccessToken,
            body: new ActivateSubscriptionRequest(DurationMonths: 1));
        (await client.SendAsync(activateReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var postWhenActiveReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/branches",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new CreateBranchRequest("Matara Branch", "MTR"));
        (await client.SendAsync(postWhenActiveReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        using var suspendReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tenant.Id}/subscription/suspend",
            superAdmin.AccessToken,
            body: new SuspendSubscriptionRequest("Overdue invoice"));
        (await client.SendAsync(suspendReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var getWhenSuspendedReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/branches",
            adminAuth.AccessToken,
            tenant.Subdomain);
        var suspendedGetRes = await client.SendAsync(getWhenSuspendedReq);
        suspendedGetRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await suspendedGetRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("SUBSCRIPTION_SUSPENDED");
        }
    }

    [Fact]
    public async Task SuperAdmin_impersonation_is_read_only_and_audited()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);

        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(
            client,
            superAdmin.AccessToken,
            "imp-tenant",
            planCode: "Starter");

        using var impersonateReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{tenant.Id}/impersonate",
            superAdmin.AccessToken);
        var impersonateRes = await client.SendAsync(impersonateReq);
        impersonateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var imp = (await impersonateRes.Content.ReadFromJsonAsync<ImpersonationResponse>(IntegrationTestHelpers.JsonOptions))!;
        imp.IsReadOnly.Should().BeTrue();

        using var readReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/branches",
            imp.AccessToken,
            tenant.Subdomain);
        (await client.SendAsync(readReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var writeReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/branches",
            imp.AccessToken,
            tenant.Subdomain,
            new CreateBranchRequest("Blocked Branch", "BLK"));
        var writeRes = await client.SendAsync(writeReq);
        writeRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await writeRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("IMPERSONATION_READ_ONLY");
        }

        using var auditReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/super-admin/audit-logs?tenantId={tenant.Id}&action=SuperAdmin.ImpersonationStarted",
            superAdmin.AccessToken);
        var logs = await (await client.SendAsync(auditReq))
            .Content.ReadFromJsonAsync<List<AuditLogDto>>(IntegrationTestHelpers.JsonOptions);
        logs.Should().ContainSingle();
    }
}
