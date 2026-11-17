using System.Net;
using System.Net.Http.Json;
using EduFlow.Application.Academics;
using EduFlow.Application.Students;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Enums;
using FluentAssertions;

namespace EduFlow.IntegrationTests;

public sealed class BranchIsolationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Branch_limited_staff_cannot_read_another_branch_and_admin_can_consolidate()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "branch", "Pro");
        var admin = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");

        var branches = await SendAsync<List<BranchDto>>(client, admin.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/branches");
        var main = branches.Single(branch => branch.IsMain);
        var created = await SendAsync<BranchDto>(
            client, admin.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/branches",
            new CreateBranchRequest("Kandy", "KDY"));

        var levels = await SendAsync<List<EducationLevelDto>>(client, admin.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/education-levels");
        var levelId = levels.First(level => level.IsEnabled).Id;
        var mainStudent = await SendAsync<StudentDto>(
            client, admin.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/students",
            new UpsertStudentRequest("Main Student", "Guardian", "0771000001", "Colombo", levelId, BranchId: main.Id));
        var otherStudent = await SendAsync<StudentDto>(
            client, admin.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/students",
            new UpsertStudentRequest("Kandy Student", "Guardian", "0771000002", "Kandy", levelId, BranchId: created.Id));
        await SendAsync<BatchDto>(
            client, admin.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/batches",
            new UpsertBatchRequest("Main Batch", "MN1", levelId, main.Id, null, "2026"));
        var otherBatch = await SendAsync<BatchDto>(
            client, admin.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/batches",
            new UpsertBatchRequest("Kandy Batch", "KD1", levelId, created.Id, null, "2026"));

        var staff = await SendAsync<TenantUserDto>(
            client, admin.AccessToken, tenant.Subdomain, HttpMethod.Post, "/api/users",
            new CreateTenantUserRequest($"staff@{tenant.Subdomain}.lk", "Counter", "StaffPass@123!", RoleType.Staff, BranchId: main.Id));
        staff.BranchId.Should().Be(main.Id);
        var staffAuth = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, tenant.Subdomain, $"staff@{tenant.Subdomain}.lk", "StaffPass@123!");

        var staffStudents = await SendAsync<StudentPage>(client, staffAuth.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/students");
        staffStudents.Items.Select(item => item.Id).Should().Contain(mainStudent.Id).And.NotContain(otherStudent.Id);
        var staffBatches = await SendAsync<List<BatchDto>>(client, staffAuth.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/batches");
        staffBatches.Select(item => item.Id).Should().NotContain(otherBatch.Id);

        using var hidden = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get, $"/api/students/{otherStudent.Id}", staffAuth.AccessToken, tenant.Subdomain);
        (await client.SendAsync(hidden)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var forbidden = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get, "/api/students", staffAuth.AccessToken, tenant.Subdomain);
        forbidden.Headers.Add("X-Branch", created.Id.ToString());
        var forbiddenResponse = await client.SendAsync(forbidden);
        forbiddenResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var consolidated = await SendAsync<StudentPage>(client, admin.AccessToken, tenant.Subdomain, HttpMethod.Get, "/api/students");
        consolidated.Items.Select(item => item.Id).Should().Contain([mainStudent.Id, otherStudent.Id]);

        using var onlyKandy = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get, "/api/students", admin.AccessToken, tenant.Subdomain);
        onlyKandy.Headers.Add("X-Branch", created.Id.ToString());
        var kandyPage = await (await client.SendAsync(onlyKandy)).Content.ReadFromJsonAsync<StudentPage>(IntegrationTestHelpers.JsonOptions);
        kandyPage!.Items.Select(item => item.Id).Should().Contain(otherStudent.Id).And.NotContain(mainStudent.Id);
    }

    [Fact]
    public async Task Starter_plan_cannot_open_a_second_branch()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "onebranch", "Starter");
        var admin = await IntegrationTestHelpers.LoginTenantUserAsync(
            client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");

        using var request = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post, "/api/branches", admin.AccessToken, tenant.Subdomain,
            new CreateBranchRequest("Extra", "EXT"));
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>(IntegrationTestHelpers.JsonOptions);
        body!["code"].Should().Be("FEATURE_NOT_IN_PLAN");
    }

    private static async Task<T> SendAsync<T>(
        HttpClient client,
        string token,
        string subdomain,
        HttpMethod method,
        string url,
        object? body = null)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(method, url, token, subdomain, body);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(IntegrationTestHelpers.JsonOptions))!;
    }

    private sealed record StudentPage(List<StudentRow> Items);
    private sealed record StudentRow(Guid Id, string FullName);
}
