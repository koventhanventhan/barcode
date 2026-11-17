using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Application.Auth;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class AuthAndPermissionPolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Refresh_token_rotation_and_reuse_detection_revokes_entire_token_family()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "rot-tenant");

        var initialLogin = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AdminUser@123!");

        var rotate1Res = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(initialLogin.RefreshToken));
        rotate1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotatedAuth = (await rotate1Res.Content.ReadFromJsonAsync<AuthResponse>(IntegrationTestHelpers.JsonOptions))!;
        rotatedAuth.RefreshToken.Should().NotBe(initialLogin.RefreshToken);

        var reuseRes = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(initialLogin.RefreshToken));
        reuseRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using (var doc = JsonDocument.Parse(await reuseRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("REFRESH_TOKEN_REUSED");
        }

        var familyRevokedRes = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(rotatedAuth.RefreshToken));
        familyRevokedRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Account_locks_out_after_five_failed_login_attempts_and_password_reset_unlocks_it()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "lock-tenant");
        var email = $"admin@{tenant.Subdomain}.lk";

        for (var i = 1; i <= 4; i++)
        {
            using var wrongReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest(email, "WrongPassword@123!"))
            };
            wrongReq.Headers.Add("X-Tenant", tenant.Subdomain);
            var wrongRes = await client.SendAsync(wrongReq);
            wrongRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using var fifthReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, "WrongPassword@123!"))
        };
        fifthReq.Headers.Add("X-Tenant", tenant.Subdomain);
        var fifthRes = await client.SendAsync(fifthReq);
        fifthRes.StatusCode.Should().Be(HttpStatusCode.Locked);
        using (var doc = JsonDocument.Parse(await fifthRes.Content.ReadAsStringAsync()))
        {
            doc.RootElement.GetProperty("code").GetString().Should().Be("ACCOUNT_LOCKED");
        }

        using var forgotReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new ForgotPasswordRequest(email))
        };
        forgotReq.Headers.Add("X-Tenant", tenant.Subdomain);
        (await client.SendAsync(forgotReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var emailStore = factory.Services.GetRequiredService<InMemoryEmailStore>();
        var resetEmail = emailStore.FindLatest(email, "PasswordReset");
        resetEmail.Should().NotBeNull();
        var resetToken = resetEmail!.Metadata!["Token"];

        using var resetReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/reset-password")
        {
            Content = JsonContent.Create(new ResetPasswordRequest(email, resetToken, "NewAdminPass@456!"))
        };
        resetReq.Headers.Add("X-Tenant", tenant.Subdomain);
        (await client.SendAsync(resetReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var relogin = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenant.Subdomain, email, "NewAdminPass@456!");
        relogin.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Invite_user_and_custom_role_permission_policies_enforce_api_access()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "perm-tenant", "Pro");

        var adminAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client,
            tenant.Subdomain,
            $"admin@{tenant.Subdomain}.lk",
            "AdminUser@123!");

        using var createRoleReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/roles",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new CreateRoleRequest("BranchViewer", "Can only view branches", [Permissions.BranchesView]));
        var createRoleRes = await client.SendAsync(createRoleReq);
        createRoleRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var customRole = (await createRoleRes.Content.ReadFromJsonAsync<RoleDto>(IntegrationTestHelpers.JsonOptions))!;

        var invitedEmail = $"clerk@{tenant.Subdomain}.lk";
        using var inviteReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/auth/invite",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new InviteUserRequest(invitedEmail, "Nadeesha Clerk", RoleType.Staff, CustomRoleId: customRole.Id));
        (await client.SendAsync(inviteReq)).StatusCode.Should().Be(HttpStatusCode.Created);

        var emailStore = factory.Services.GetRequiredService<InMemoryEmailStore>();
        var inviteMessage = emailStore.FindLatest(invitedEmail, "UserInvite");
        inviteMessage.Should().NotBeNull();
        var inviteToken = inviteMessage!.Metadata!["Token"];

        using var acceptReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/accept-invite")
        {
            Content = JsonContent.Create(new AcceptInviteRequest(invitedEmail, inviteToken, "ClerkPass@123!"))
        };
        acceptReq.Headers.Add("X-Tenant", tenant.Subdomain);
        var acceptRes = await client.SendAsync(acceptReq);
        acceptRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var clerkAuth = (await acceptRes.Content.ReadFromJsonAsync<AuthResponse>(IntegrationTestHelpers.JsonOptions))!;

        using var getBranchesReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/branches",
            clerkAuth.AccessToken,
            tenant.Subdomain);
        (await client.SendAsync(getBranchesReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var createBranchForbiddenReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/branches",
            clerkAuth.AccessToken,
            tenant.Subdomain,
            new CreateBranchRequest("Galle Branch", "GLE"));
        (await client.SendAsync(createBranchForbiddenReq)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var getRolesForbiddenReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/roles",
            clerkAuth.AccessToken,
            tenant.Subdomain);
        (await client.SendAsync(getRolesForbiddenReq)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var updateRoleReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            $"/api/roles/{customRole.Id}",
            adminAuth.AccessToken,
            tenant.Subdomain,
            new UpdateRoleRequest("BranchViewer", "Now can manage branches", [Permissions.BranchesView, Permissions.BranchesManage]));
        (await client.SendAsync(updateRoleReq)).StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshRes = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new RefreshRequest(clerkAuth.RefreshToken));
        var refreshedClerkAuth = (await refreshRes.Content.ReadFromJsonAsync<AuthResponse>(IntegrationTestHelpers.JsonOptions))!;

        using var createBranchAllowedReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/branches",
            refreshedClerkAuth.AccessToken,
            tenant.Subdomain,
            new CreateBranchRequest("Galle Branch", "GLE"));
        (await client.SendAsync(createBranchAllowedReq)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Rate_limiter_throttles_excessive_auth_requests_and_swagger_includes_endpoints_and_attributes()
    {
        var swaggerClient = factory.CreateClient();
        var swaggerRes = await swaggerClient.GetAsync("/swagger/v1/swagger.json");
        swaggerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var swaggerJson = await swaggerRes.Content.ReadAsStringAsync();
        swaggerJson.Should().Contain("/api/auth/login");
        swaggerJson.Should().Contain("/api/super-admin/tenants");
        swaggerJson.Should().Contain("Requires Feature(s): ParentPortal");
        swaggerJson.Should().Contain("Requires Policy: Permission:branches.manage");

        await using var throttledFactory = new ApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:AuthPermitLimit", "2");
            builder.UseSetting("RateLimiting:WindowSeconds", "60");
        });
        var throttledClient = throttledFactory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
            {
                Content = JsonContent.Create(new ForgotPasswordRequest("nobody@eduflow.lk"))
            };
            req.Headers.Add("X-Forwarded-For", "203.0.113.42");
            (await throttledClient.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var blockedReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/forgot-password")
        {
            Content = JsonContent.Create(new ForgotPasswordRequest("nobody@eduflow.lk"))
        };
        blockedReq.Headers.Add("X-Forwarded-For", "203.0.113.42");
        var blockedRes = await throttledClient.SendAsync(blockedReq);
        blockedRes.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var doc = JsonDocument.Parse(await blockedRes.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("RATE_LIMIT_EXCEEDED");
    }
}
