using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduFlow.Application.Academics;
using EduFlow.Application.Auth;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Students;
using EduFlow.Application.SuperAdmin;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class AcademicCoreTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Plan_limits_block_extra_levels_subjects_batches_and_students()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "limits", "Starter");
        var auth = await LoginAdminAsync(client, tenant);

        using var levelsRequest = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/education-levels", auth.AccessToken, tenant.Subdomain);
        var levels = (await (await client.SendAsync(levelsRequest)).Content.ReadFromJsonAsync<List<EducationLevelDto>>(IntegrationTestHelpers.JsonOptions))!;
        levels.Should().HaveCount(5);
        levels.Count(level => level.IsEnabled).Should().Be(2);

        var three = levels.OrderBy(level => level.SortOrder).Take(3).Select(level => level.Id).ToList();
        using var enableRequest = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/education-levels/selection",
            auth.AccessToken,
            tenant.Subdomain,
            new UpdateLevelSelectionRequest(three));
        var enableResponse = await client.SendAsync(enableRequest);
        enableResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(enableResponse)).Should().Be("LIMIT_REACHED");

        var levelId = levels.First(level => level.IsEnabled).Id;
        for (var i = 1; i <= 5; i++)
        {
            using var subjectRequest = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Post,
                "/api/subjects",
                auth.AccessToken,
                tenant.Subdomain,
                new UpsertSubjectRequest($"Subject {i}", $"SUB{i}", levelId));
            (await client.SendAsync(subjectRequest)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var extraSubject = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/subjects",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertSubjectRequest("Subject 6", "SUB6", levelId));
        var subjectResponse = await client.SendAsync(extraSubject);
        subjectResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(subjectResponse)).Should().Be("LIMIT_REACHED");

        for (var i = 1; i <= 10; i++)
        {
            using var batchRequest = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Post,
                "/api/batches",
                auth.AccessToken,
                tenant.Subdomain,
                new UpsertBatchRequest($"Batch {i}", $"B{i:00}", levelId, null, null, "2026"));
            (await client.SendAsync(batchRequest)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var extraBatch = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/batches",
            auth.AccessToken,
            tenant.Subdomain,
            new UpsertBatchRequest("Batch 11", "B11", levelId, null, null, "2026"));
        var batchResponse = await client.SendAsync(extraBatch);
        batchResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(batchResponse)).Should().Be("LIMIT_REACHED");

        await SeedStudentsAsync(tenant.Id, levelId, 250);
        using var studentRequest = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            auth.AccessToken,
            tenant.Subdomain,
            StudentRequest("Over Limit", "0770000000", levelId));
        var studentResponse = await client.SendAsync(studentRequest);
        studentResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(studentResponse)).Should().Be("LIMIT_REACHED");
    }

    [Fact]
    public async Task Import_reports_invalid_rows_and_saves_valid_ones()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "import", "Starter");
        var auth = await LoginAdminAsync(client, tenant);
        var csv = """
            fullName,guardianName,guardianPhone,guardianPhoneSecondary,address,city,educationLevelCode,batchCode,gender,dateOfBirth,admissionDate,status
            Kamal Fernando,Rani Fernando,0771111111,,12 Main Street,Kandy,PRIMARY,,Male,2014-02-02,2026-01-06,Active
            ,Rani Fernando,12,,12 Main Street,Kandy,PRIMARY,,Male,2014-02-02,2026-01-06,Active
            """;

        using var request = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Post, "/api/students/import", auth.AccessToken, tenant.Subdomain);
        request.Content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "students.csv" }
        };
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await response.Content.ReadFromJsonAsync<StudentImportReport>(IntegrationTestHelpers.JsonOptions))!;
        report.Imported.Should().Be(1);
        report.Failed.Should().BeGreaterThan(0);
        report.Errors.Should().Contain(error => error.RowNumber == 3);

        using var listRequest = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/students?search=Kamal", auth.AccessToken, tenant.Subdomain);
        var page = (await (await client.SendAsync(listRequest)).Content.ReadFromJsonAsync<StudentPageDto>(IntegrationTestHelpers.JsonOptions))!;
        page.Total.Should().Be(1);
        page.Items[0].StudentCode.Should().Be("S000001");
    }

    [Fact]
    public async Task Student_codes_and_barcodes_are_unique_inside_a_tenant_and_may_repeat_across_tenants()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenantA = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "code-a", "Growth");
        var tenantB = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "code-b", "Growth");
        var authA = await LoginAdminAsync(client, tenantA);
        var authB = await LoginAdminAsync(client, tenantB);
        var levelA = await FirstEnabledLevelAsync(client, authA.AccessToken, tenantA.Subdomain);
        var levelB = await FirstEnabledLevelAsync(client, authB.AccessToken, tenantB.Subdomain);

        var firstA = await CreateStudentAsync(client, authA.AccessToken, tenantA.Subdomain, "Nimal Perera", "0771000001", levelA);
        var firstB = await CreateStudentAsync(client, authB.AccessToken, tenantB.Subdomain, "Kamal Fernando", "0771000002", levelB);
        var secondA = await CreateStudentAsync(client, authA.AccessToken, tenantA.Subdomain, "Amaya Silva", "0771000003", levelA);

        firstA.StudentCode.Should().Be(firstB.StudentCode);
        secondA.StudentCode.Should().NotBe(firstA.StudentCode);
        new[] { firstA.StudentCode, secondA.StudentCode }.Should().OnlyHaveUniqueItems();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using (tenantContext.BeginSystemScope(tenantA.Id))
        {
            db.Students.Add(new Student
            {
                TenantId = tenantA.Id,
                StudentCode = firstA.StudentCode,
                FullName = "Duplicate",
                GuardianName = "Duplicate",
                GuardianPhone = "0771000099",
                Address = "Colombo",
                EducationLevelId = levelA,
                AdmissionDate = new DateOnly(2026, 1, 6)
            });
            var act = async () => await db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>();
        }

        using var cardRequest = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/students/{firstA.Id}/id-card",
            authA.AccessToken,
            tenantA.Subdomain);
        var card = await client.SendAsync(cardRequest);
        card.StatusCode.Should().Be(HttpStatusCode.OK);
        card.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await card.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes[..5]).Should().Be("%PDF-");
    }

    [Fact]
    public async Task Staff_without_edit_permission_cannot_create_students_and_starter_cannot_print_or_group()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "perms", "Starter");
        var admin = await LoginAdminAsync(client, tenant);

        using var roleRequest = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/roles",
            admin.AccessToken,
            tenant.Subdomain,
            new CreateRoleRequest("Viewer", "Read students", [Permissions.StudentsView]));
        var role = (await (await client.SendAsync(roleRequest)).Content.ReadFromJsonAsync<RoleDto>(IntegrationTestHelpers.JsonOptions))!;

        using var userRequest = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/users",
            admin.AccessToken,
            tenant.Subdomain,
            new CreateTenantUserRequest($"viewer@{tenant.Subdomain}.lk", "Viewer", "ViewerPass@123!", RoleType.Staff, CustomRoleId: role.Id));
        (await client.SendAsync(userRequest)).StatusCode.Should().Be(HttpStatusCode.Created);

        var viewer = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenant.Subdomain, $"viewer@{tenant.Subdomain}.lk", "ViewerPass@123!");
        using var listRequest = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/students", viewer.AccessToken, tenant.Subdomain);
        (await client.SendAsync(listRequest)).StatusCode.Should().Be(HttpStatusCode.OK);

        var levelId = await FirstEnabledLevelAsync(client, admin.AccessToken, tenant.Subdomain);
        using var createRequest = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            viewer.AccessToken,
            tenant.Subdomain,
            StudentRequest("Blocked", "0772222222", levelId));
        (await client.SendAsync(createRequest)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var cardsRequest = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students/id-cards",
            admin.AccessToken,
            tenant.Subdomain,
            new BulkStudentRequest(null, true));
        var cards = await client.SendAsync(cardsRequest);
        cards.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(cards)).Should().Be("FEATURE_NOT_IN_PLAN");

        using var exportRequest = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/students/export?format=csv",
            admin.AccessToken,
            tenant.Subdomain);
        var export = await client.SendAsync(exportRequest);
        export.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(export)).Should().Be("FEATURE_NOT_IN_PLAN");

        using var groupRequest = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/batch-groups", admin.AccessToken, tenant.Subdomain);
        var groups = await client.SendAsync(groupRequest);
        groups.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCodeAsync(groups)).Should().Be("FEATURE_NOT_IN_PLAN");
    }

    private async Task SeedStudentsAsync(Guid tenantId, Guid levelId, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using (tenantContext.BeginSystemScope(tenantId))
        {
            for (var i = 1; i <= count; i++)
            {
                db.Students.Add(new Student
                {
                    TenantId = tenantId,
                    StudentCode = $"S{i:000000}",
                    FullName = $"Seed {i}",
                    GuardianName = "Guardian",
                    GuardianPhone = "0771234567",
                    Address = "Colombo",
                    EducationLevelId = levelId,
                    AdmissionDate = new DateOnly(2026, 1, 6)
                });
            }

            await db.SaveChangesAsync();
        }
    }

    private static async Task<AuthResponse> LoginAdminAsync(HttpClient client, TenantDetailDto tenant) =>
        await IntegrationTestHelpers.LoginTenantUserAsync(client, tenant.Subdomain, $"admin@{tenant.Subdomain}.lk", "AdminUser@123!");

    private static async Task<Guid> FirstEnabledLevelAsync(HttpClient client, string token, string subdomain)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(HttpMethod.Get, "/api/education-levels", token, subdomain);
        var levels = (await (await client.SendAsync(request)).Content.ReadFromJsonAsync<List<EducationLevelDto>>(IntegrationTestHelpers.JsonOptions))!;
        return levels.First(level => level.IsEnabled).Id;
    }

    private static async Task<StudentDto> CreateStudentAsync(HttpClient client, string token, string subdomain, string name, string phone, Guid levelId)
    {
        using var request = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/students",
            token,
            subdomain,
            StudentRequest(name, phone, levelId));
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentDto>(IntegrationTestHelpers.JsonOptions))!;
    }

    private static UpsertStudentRequest StudentRequest(string name, string phone, Guid levelId) =>
        new(name, "Guardian", phone, "12 Temple Road, Colombo", levelId);

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("code").GetString()!;
    }
}
