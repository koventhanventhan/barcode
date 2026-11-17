using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Api.Endpoints;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Enums;
using FluentAssertions;

namespace EduFlow.IntegrationTests;

public sealed class FeatureSwitchAndPlanLimitsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Seeded_plans_match_spec_and_demo_tenant_exists()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/plans");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var plans = await response.Content.ReadFromJsonAsync<List<PlanDto>>(IntegrationTestHelpers.JsonOptions);
        plans.Should().NotBeNull();
        plans!.Select(p => p.Code).Should().ContainInOrder("Starter", "Growth", "Pro", "Enterprise");

        var starter = plans.Single(p => p.Code == "Starter");
        starter.Limits["Students"].Should().Be(250);
        starter.Limits["AdminAccounts"].Should().Be(1);
        starter.Limits["StaffAccounts"].Should().Be(3);
        starter.Limits["SmsSegmentsPerMonth"].Should().Be(300);

        var enterprise = plans.Single(p => p.Code == "Enterprise");
        enterprise.Limits["Students"].Should().Be(2500);
        enterprise.Limits["Batches"].Should().BeNull();
        enterprise.Limits["Subjects"].Should().BeNull();
        enterprise.Limits["StaffAccounts"].Should().BeNull();
        enterprise.Features.Should().Contain(nameof(FeatureKey.OnlinePayments));
    }

    [Fact]
    public async Task Feature_switches_enforce_plan_entitlement_tenant_toggle_parent_portal_and_audit_log()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);

        var starterTenant = await IntegrationTestHelpers.CreateTestTenantAsync(
            client,
            superAdmin.AccessToken,
            "feat-starter",
            planCode: "Starter");

        var adminAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            starterTenant.Subdomain,
            $"admin@{starterTenant.Subdomain}.lk",
            "AdminUser@123!");

        using var createParentReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            adminAuth.AccessToken,
            starterTenant.Subdomain,
            new CreateTenantUserRequest(
                $"parent@{starterTenant.Subdomain}.lk",
                "Nimal Parent",
                "ParentPass@123!",
                RoleType.Parent));
        (await client.SendAsync(createParentReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        var parentAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            starterTenant.Subdomain,
            $"parent@{starterTenant.Subdomain}.lk",
            "ParentPass@123!");

        using var portalOnReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/parent/portal",
            parentAuth.AccessToken,
            starterTenant.Subdomain);
        (await client.SendAsync(portalOnReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var disablePortalReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/settings/features/ParentPortal",
            adminAuth.AccessToken,
            starterTenant.Subdomain,
            new UpdateFeatureSwitchRequest(IsEnabled: false));
        (await client.SendAsync(disablePortalReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var portalOffReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/parent/portal",
            parentAuth.AccessToken,
            starterTenant.Subdomain);
        var portalOffRes = await client.SendAsync(portalOffReq);
        portalOffRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await portalOffRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_DISABLED");
            doc.RootElement.GetProperty("feature").GetString().Should().Be("ParentPortal");
        }

        using var parentLoginWhenOffReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = $"parent@{starterTenant.Subdomain}.lk", password = "ParentPass@123!" })
        };
        parentLoginWhenOffReq.Headers.Add("X-Tenant", starterTenant.Subdomain);
        var parentLoginOffRes = await client.SendAsync(parentLoginWhenOffReq);
        parentLoginOffRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await parentLoginOffRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_DISABLED");
            doc.RootElement.GetProperty("feature").GetString().Should().Be("ParentPortal");
        }

        using var auditReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/super-admin/audit-logs?tenantId={starterTenant.Id}&action=FeatureSwitch.Changed",
            superAdmin.AccessToken);
        var auditLogs = await (await client.SendAsync(auditReq))
            .Content.ReadFromJsonAsync<List<AuditLogDto>>(IntegrationTestHelpers.JsonOptions);
        auditLogs.Should().NotBeEmpty();

        using var onlinePayNotInPlanReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/payments/online/status",
            adminAuth.AccessToken,
            starterTenant.Subdomain);
        var notInPlanRes = await client.SendAsync(onlinePayNotInPlanReq);
        notInPlanRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await notInPlanRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("FEATURE_NOT_IN_PLAN");
            doc.RootElement.GetProperty("feature").GetString().Should().Be("OnlinePayments");
        }

        using var overrideReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            $"/api/super-admin/tenants/{starterTenant.Id}/feature-overrides/OnlinePayments",
            superAdmin.AccessToken,
            body: new SetFeatureOverrideRequest(IsEnabled: true, Reason: "Approved special request"));
        (await client.SendAsync(overrideReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var onlinePayAfterOverrideReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/payments/online/status",
            adminAuth.AccessToken,
            starterTenant.Subdomain);
        (await client.SendAsync(onlinePayAfterOverrideReq)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Plan_limits_block_exceeding_admin_and_staff_quotas_until_upgraded()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);

        var starterTenant = await IntegrationTestHelpers.CreateTestTenantAsync(
            client,
            superAdmin.AccessToken,
            "limit-starter",
            planCode: "Starter");

        var adminAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            starterTenant.Subdomain,
            $"admin@{starterTenant.Subdomain}.lk",
            "AdminUser@123!");

        using var secondAdminReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            adminAuth.AccessToken,
            starterTenant.Subdomain,
            new CreateTenantUserRequest(
                $"admin2@{starterTenant.Subdomain}.lk",
                "Second Admin",
                "AdminPass@123!",
                RoleType.InstituteAdmin));
        var secondAdminRes = await client.SendAsync(secondAdminReq);
        secondAdminRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await secondAdminRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("LIMIT_REACHED");
            doc.RootElement.GetProperty("limit").GetString().Should().Be("AdminAccounts");
            doc.RootElement.GetProperty("max").GetInt32().Should().Be(1);
        }

        for (var i = 1; i <= 3; i++)
        {
            using var staffReq = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Post,
                "/api/users",
                adminAuth.AccessToken,
                starterTenant.Subdomain,
                new CreateTenantUserRequest(
                    $"staff{i}@{starterTenant.Subdomain}.lk",
                    $"Staff {i}",
                    "StaffPass@123!",
                    RoleType.Staff));
            (await client.SendAsync(staffReq)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var fourthStaffReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            adminAuth.AccessToken,
            starterTenant.Subdomain,
            new CreateTenantUserRequest(
                $"staff4@{starterTenant.Subdomain}.lk",
                "Staff 4",
                "StaffPass@123!",
                RoleType.Teacher));
        var fourthStaffRes = await client.SendAsync(fourthStaffReq);
        fourthStaffRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var doc = JsonDocument.Parse(await fourthStaffRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("LIMIT_REACHED");
            doc.RootElement.GetProperty("limit").GetString().Should().Be("StaffAccounts");
            doc.RootElement.GetProperty("max").GetInt32().Should().Be(3);
        }

        using var upgradePlanReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/super-admin/tenants/{starterTenant.Id}/subscription/assign-plan",
            superAdmin.AccessToken,
            body: new AssignPlanRequest(PlanCode: "Growth"));
        (await client.SendAsync(upgradePlanReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var retryFourthStaffReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            adminAuth.AccessToken,
            starterTenant.Subdomain,
            new CreateTenantUserRequest(
                $"staff4@{starterTenant.Subdomain}.lk",
                "Staff 4",
                "StaffPass@123!",
                RoleType.Teacher));
        (await client.SendAsync(retryFourthStaffReq)).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
