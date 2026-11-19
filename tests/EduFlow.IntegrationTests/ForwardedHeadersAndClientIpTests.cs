using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Application.Auth;
using EduFlow.Application.SuperAdmin;
using EduFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class ForwardedHeadersAndClientIpTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nxt) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Remote-Ip", out var ipHeader)
                    && IPAddress.TryParse(ipHeader.FirstOrDefault(), out var remoteIp))
                {
                    context.Connection.RemoteIpAddress = remoteIp;
                }

                await nxt(context);
            });
            next(app);
        };
    }

    [Fact]
    public async Task Rate_limiter_ignores_spoofed_XForwardedFor_from_untrusted_ip_and_throttles_by_remote_ip()
    {
        await using var limitedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:PublicPermitLimit", "2");
            builder.UseSetting("RateLimiting:WindowSeconds", "60");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>());
        });

        using var client = limitedFactory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding?code=");
            req.Headers.Add("X-Test-Remote-Ip", "198.51.100.10");
            req.Headers.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            var res = await client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var blockedReq = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding?code=");
        blockedReq.Headers.Add("X-Test-Remote-Ip", "198.51.100.10");
        blockedReq.Headers.Add("X-Forwarded-For", "203.0.113.99");
        var blocked = await client.SendAsync(blockedReq);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var doc = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("code").GetString().Should().Be("RATE_LIMIT_EXCEEDED");
    }

    [Fact]
    public async Task Rate_limiter_uses_XForwardedFor_from_trusted_docker_network_and_isolates_clients()
    {
        await using var limitedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:PublicPermitLimit", "2");
            builder.UseSetting("RateLimiting:WindowSeconds", "60");
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>());
        });

        using var client = limitedFactory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding?code=");
            req.Headers.Add("X-Test-Remote-Ip", "172.18.0.4");
            req.Headers.Add("X-Forwarded-For", "203.0.113.42");
            var res = await client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var blockedReq = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding?code=");
        blockedReq.Headers.Add("X-Test-Remote-Ip", "172.18.0.4");
        blockedReq.Headers.Add("X-Forwarded-For", "203.0.113.42");
        var blocked = await client.SendAsync(blockedReq);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        using var otherClientReq = new HttpRequestMessage(HttpMethod.Get, "/api/public/branding?code=");
        otherClientReq.Headers.Add("X-Test-Remote-Ip", "::ffff:172.18.0.4");
        otherClientReq.Headers.Add("X-Forwarded-For", "203.0.113.43");
        var otherClientRes = await client.SendAsync(otherClientReq);
        otherClientRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Forwarded_headers_update_scheme_and_TenantResolutionMiddleware_client_ip_only_from_docker_network()
    {
        await using var testFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>());
        });

        using var client = testFactory.CreateClient();

        using var trustedLogin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("superadmin@admino.lk", "SuperAdmin@123!"))
        };
        trustedLogin.Headers.Add("X-Test-Remote-Ip", "172.18.0.4");
        trustedLogin.Headers.Add("X-Forwarded-For", "203.0.113.88");
        trustedLogin.Headers.Add("X-Forwarded-Proto", "https");
        var trustedLoginRes = await client.SendAsync(trustedLogin);
        trustedLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        trustedLoginRes.Headers.TryGetValues("Set-Cookie", out var trustedCookies).Should().BeTrue();
        trustedCookies!.Should().Contain(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase));

        var auth = (await trustedLoginRes.Content.ReadFromJsonAsync<AuthResponse>(IntegrationTestHelpers.JsonOptions))!;

        using var untrustedLogin = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("superadmin@admino.lk", "SuperAdmin@123!"))
        };
        untrustedLogin.Headers.Add("X-Test-Remote-Ip", "198.51.100.20");
        untrustedLogin.Headers.Add("X-Forwarded-For", "203.0.113.99");
        untrustedLogin.Headers.Add("X-Forwarded-Proto", "https");
        var untrustedLoginRes = await client.SendAsync(untrustedLogin);
        untrustedLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
        untrustedLoginRes.Headers.TryGetValues("Set-Cookie", out var untrustedCookies).Should().BeTrue();
        untrustedCookies!.Should().NotContain(c => c.Contains("secure", StringComparison.OrdinalIgnoreCase));

        var trustedSlug = $"fwdok-{Guid.NewGuid().ToString("N")[..8]}".ToLowerInvariant();
        using var trustedCreateReq = new HttpRequestMessage(HttpMethod.Post, "/api/super-admin/tenants")
        {
            Content = JsonContent.Create(new CreateTenantRequest(
                Name: $"Institute {trustedSlug}",
                Subdomain: trustedSlug,
                AdminEmail: $"admin@{trustedSlug}.lk",
                AdminFullName: $"Admin {trustedSlug}",
                AdminPassword: "AdminUser@123!",
                PlanCode: "Starter"))
        };
        trustedCreateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        trustedCreateReq.Headers.Add("X-Test-Remote-Ip", "172.18.0.4");
        trustedCreateReq.Headers.Add("X-Forwarded-For", "203.0.113.88");
        trustedCreateReq.Headers.Add("X-Forwarded-Proto", "https");
        var trustedCreateRes = await client.SendAsync(trustedCreateReq);
        trustedCreateRes.EnsureSuccessStatusCode();
        var trustedTenant = (await trustedCreateRes.Content.ReadFromJsonAsync<TenantDetailDto>(IntegrationTestHelpers.JsonOptions))!;

        var untrustedSlug = $"fwdbad-{Guid.NewGuid().ToString("N")[..8]}".ToLowerInvariant();
        using var untrustedCreateReq = new HttpRequestMessage(HttpMethod.Post, "/api/super-admin/tenants")
        {
            Content = JsonContent.Create(new CreateTenantRequest(
                Name: $"Institute {untrustedSlug}",
                Subdomain: untrustedSlug,
                AdminEmail: $"admin@{untrustedSlug}.lk",
                AdminFullName: $"Admin {untrustedSlug}",
                AdminPassword: "AdminUser@123!",
                PlanCode: "Starter"))
        };
        untrustedCreateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        untrustedCreateReq.Headers.Add("X-Test-Remote-Ip", "198.51.100.20");
        untrustedCreateReq.Headers.Add("X-Forwarded-For", "203.0.113.99");
        var untrustedCreateRes = await client.SendAsync(untrustedCreateReq);
        untrustedCreateRes.EnsureSuccessStatusCode();
        var untrustedTenant = (await untrustedCreateRes.Content.ReadFromJsonAsync<TenantDetailDto>(IntegrationTestHelpers.JsonOptions))!;

        using var scope = testFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var trustedAudit = await db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(a => a.EntityId == trustedTenant.Id.ToString() && a.Action == "SuperAdmin.TenantCreated");
        trustedAudit.IpAddress.Should().Be("203.0.113.88");

        var untrustedAudit = await db.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(a => a.EntityId == untrustedTenant.Id.ToString() && a.Action == "SuperAdmin.TenantCreated");
        untrustedAudit.IpAddress.Should().Be("198.51.100.20");
    }
}
