using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduFlow.Application.Auth;
using EduFlow.Application.SuperAdmin;

namespace EduFlow.IntegrationTests;

public static class IntegrationTestHelpers
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<AuthResponse> LoginAsSuperAdminAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("superadmin@eduflow.lk", "SuperAdmin@123!"));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
    }

    public static async Task<TenantDetailDto> CreateTestTenantAsync(
        HttpClient client,
        string superAdminToken,
        string subdomainPrefix,
        string planCode = "Starter",
        string adminPassword = "AdminUser@123!")
    {
        var uniqueSlug = $"{subdomainPrefix}-{Guid.NewGuid().ToString("N")[..8]}".ToLowerInvariant();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/super-admin/tenants")
        {
            Content = JsonContent.Create(new CreateTenantRequest(
                Name: $"Institute {uniqueSlug}",
                Subdomain: uniqueSlug,
                AdminEmail: $"admin@{uniqueSlug}.lk",
                AdminFullName: $"Admin {uniqueSlug}",
                AdminPassword: adminPassword,
                PlanCode: planCode))
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);

        var response = await client.SendAsync(req);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TenantDetailDto>(JsonOptions))!;
    }

    public static async Task<AuthResponse> LoginTenantUserAsync(
        HttpClient client,
        string tenantSubdomain,
        string email,
        string password)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest(email, password))
        };
        req.Headers.Add("X-Tenant", tenantSubdomain);

        var response = await client.SendAsync(req);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
    }

    public static HttpRequestMessage CreateTenantRequest(
        HttpMethod method,
        string url,
        string bearerToken,
        string? tenantHeader = null,
        object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (!string.IsNullOrWhiteSpace(tenantHeader))
        {
            req.Headers.Add("X-Tenant", tenantHeader);
        }

        if (body is not null)
        {
            req.Content = JsonContent.Create(body);
        }

        return req;
    }
}
