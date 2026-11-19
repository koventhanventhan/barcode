using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EduFlow.Application.Auth;
using EduFlow.Application.Staff;
using EduFlow.Application.Support;
using EduFlow.Application.Tenancy;
using EduFlow.Domain.Enums;
using EduFlow.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EduFlow.IntegrationTests;

public sealed class Phase13DStaffRegistrationAndSupportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Staff_self_registration_qr_approval_rotation_limit_and_isolation_flow()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var tenantA = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13d-staff-a", "Starter");
        var tenantB = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13d-staff-b", "Starter");

        var adminA = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenantA.Subdomain, $"admin@{tenantA.Subdomain}.lk", "AdminUser@123!");
        var adminB = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenantB.Subdomain, $"admin@{tenantB.Subdomain}.lk", "AdminUser@123!");

        // Before generating a link, GET is read-only and HasLink is false
        using var initialLinkReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/staff/registration-link",
            adminA.AccessToken,
            tenantA.Subdomain);
        var initialLink = await (await client.SendAsync(initialLinkReq))
            .Content.ReadFromJsonAsync<StaffRegistrationLinkDto>(IntegrationTestHelpers.JsonOptions);
        initialLink!.HasLink.Should().BeFalse();

        // Admin enables link -> token + QR matrix generated
        using var enableReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Put,
            "/api/staff/registration-link",
            adminA.AccessToken,
            tenantA.Subdomain,
            new UpdateStaffRegistrationLinkRequest(IsEnabled: true));
        var enabledLink = await (await client.SendAsync(enableReq))
            .Content.ReadFromJsonAsync<StaffRegistrationLinkDto>(IntegrationTestHelpers.JsonOptions);
        enabledLink!.HasLink.Should().BeTrue();
        enabledLink.IsEnabled.Should().BeTrue();
        enabledLink.Token.Should().NotBeNullOrWhiteSpace();
        enabledLink.RegistrationUrl.Should().Contain(enabledLink.Token);
        enabledLink.Qr.Should().NotBeNull();
        enabledLink.Qr!.Modules.Length.Should().Be(enabledLink.Qr.Size * enabledLink.Qr.Size);

        // Public GET returns institute display info without leaking TenantId
        var publicInfoRes = await client.GetAsync($"/api/public/staff-registration/{enabledLink.Token}");
        publicInfoRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var publicInfo = await publicInfoRes.Content.ReadFromJsonAsync<PublicStaffRegistrationInfoDto>(IntegrationTestHelpers.JsonOptions);
        publicInfo!.InstituteName.Should().Be(tenantA.Name);

        // Invalid token returns 404
        var badTokenRes = await client.GetAsync("/api/public/staff-registration/non-existent-token");
        badTokenRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Role elevation on public submission is rejected
        var elevateRes = await client.PostAsJsonAsync(
            $"/api/public/staff-registration/{enabledLink.Token}",
            new SubmitStaffRegistrationRequest("Bad Role", "bad@staff.lk", "StrongPass@123!", null, RoleType.InstituteAdmin));
        elevateRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Valid staff self-registration #1 (will be approved)
        var submit1Res = await client.PostAsJsonAsync(
            $"/api/public/staff-registration/{enabledLink.Token}",
            new SubmitStaffRegistrationRequest("Kavindi Perera", "kavindi@staff.lk", "StaffPass@123!", "0771234567", RoleType.Teacher, null, "Mathematics"));
        submit1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted1 = await submit1Res.Content.ReadFromJsonAsync<PublicStaffRegistrationSubmittedDto>(IntegrationTestHelpers.JsonOptions);
        submitted1!.Status.Should().Be("Pending");

        // Duplicate pending email is rejected with 409
        var dupRes = await client.PostAsJsonAsync(
            $"/api/public/staff-registration/{enabledLink.Token}",
            new SubmitStaffRegistrationRequest("Kavindi Perera", "kavindi@staff.lk", "StaffPass@123!", null, RoleType.Teacher));
        dupRes.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Valid staff self-registration #2 (will be rejected)
        var submit2Res = await client.PostAsJsonAsync(
            $"/api/public/staff-registration/{enabledLink.Token}",
            new SubmitStaffRegistrationRequest("Sahan Fernando", "sahan@staff.lk", "StaffPass@123!", null, RoleType.Staff));
        submit2Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted2 = await submit2Res.Content.ReadFromJsonAsync<PublicStaffRegistrationSubmittedDto>(IntegrationTestHelpers.JsonOptions);

        // Rotating link invalidates the previous token
        using var rotateReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/staff/registration-link/rotate",
            adminA.AccessToken,
            tenantA.Subdomain);
        var rotated = await (await client.SendAsync(rotateReq))
            .Content.ReadFromJsonAsync<StaffRegistrationLinkDto>(IntegrationTestHelpers.JsonOptions);
        rotated!.Token.Should().NotBe(enabledLink.Token);

        var oldTokenAfterRotate = await client.GetAsync($"/api/public/staff-registration/{enabledLink.Token}");
        oldTokenAfterRotate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Tenant isolation: Tenant B sees zero pending requests and cannot approve Tenant A's request
        using var listBReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/staff/registrations",
            adminB.AccessToken,
            tenantB.Subdomain);
        var listB = await (await client.SendAsync(listBReq))
            .Content.ReadFromJsonAsync<StaffRegistrationPageDto>(IntegrationTestHelpers.JsonOptions);
        listB!.Items.Should().BeEmpty();

        using var crossApproveReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/staff/registrations/{submitted1.RequestId}/approve",
            adminB.AccessToken,
            tenantB.Subdomain,
            new ApproveStaffRegistrationRequest(TemplateKey: "class-teacher"));
        (await client.SendAsync(crossApproveReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Tenant A rejects #2 and approves #1
        using var rejectReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/staff/registrations/{submitted2!.RequestId}/reject",
            adminA.AccessToken,
            tenantA.Subdomain,
            new RejectStaffRegistrationRequest("Position filled"));
        var rejectedDto = await (await client.SendAsync(rejectReq))
            .Content.ReadFromJsonAsync<StaffRegistrationRequestDto>(IntegrationTestHelpers.JsonOptions);
        rejectedDto!.Status.Should().Be("Rejected");

        using var approveReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/staff/registrations/{submitted1.RequestId}/approve",
            adminA.AccessToken,
            tenantA.Subdomain,
            new ApproveStaffRegistrationRequest(TemplateKey: "class-teacher"));
        var approveRes = await client.SendAsync(approveReq);
        approveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var approvedDto = await approveRes.Content.ReadFromJsonAsync<StaffRegistrationRequestDto>(IntegrationTestHelpers.JsonOptions);
        approvedDto!.Status.Should().Be("Approved");
        approvedDto.CreatedUserId.Should().NotBeNull();

        // Approval email was sent
        using (var scope = factory.Services.CreateScope())
        {
            var emails = scope.ServiceProvider.GetRequiredService<InMemoryEmailStore>();
            emails.Messages.Should().Contain(m => m.To == "kavindi@staff.lk" && m.Subject.Contains("approved"));
        }

        // Approved staff member can sign in immediately with their self-chosen password
        var staffAuth = await IntegrationTestHelpers.LoginTenantUserAsync(client, tenantA.Subdomain, "kavindi@staff.lk", "StaffPass@123!");
        staffAuth.User.Role.Should().Be("Teacher");

        // Staff member without users.manage cannot approve registrations
        var extraSubmitRes = await client.PostAsJsonAsync(
            $"/api/public/staff-registration/{rotated.Token}",
            new SubmitStaffRegistrationRequest("Kasun Jayawardena", "kasun@staff.lk", "StaffPass@123!", null, RoleType.Staff));
        extraSubmitRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var extraSubmitted = await extraSubmitRes.Content.ReadFromJsonAsync<PublicStaffRegistrationSubmittedDto>(IntegrationTestHelpers.JsonOptions);

        using var unauthApproveReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/staff/registrations/{extraSubmitted!.RequestId}/approve",
            staffAuth.AccessToken,
            tenantA.Subdomain,
            new ApproveStaffRegistrationRequest(TemplateKey: "front-desk"));
        (await client.SendAsync(unauthApproveReq)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Starter plan StaffAccounts limit is 3: add 2 more staff directly, then approval must hit LIMIT_REACHED
        for (var i = 1; i <= 2; i++)
        {
            using var createStaffReq = IntegrationTestHelpers.CreateTenantRequest(
                HttpMethod.Post,
                "/api/staff",
                adminA.AccessToken,
                tenantA.Subdomain,
                new CreateStaffRequest($"Direct {i}", $"direct{i}@{tenantA.Subdomain}.lk", "StaffPass@123!", "front-desk"));
            (await client.SendAsync(createStaffReq)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        using var overLimitApproveReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/staff/registrations/{extraSubmitted.RequestId}/approve",
            adminA.AccessToken,
            tenantA.Subdomain,
            new ApproveStaffRegistrationRequest(TemplateKey: "front-desk"));
        var overLimitRes = await client.SendAsync(overLimitApproveReq);
        overLimitRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var overLimitBody = await overLimitRes.Content.ReadAsStringAsync();
        overLimitBody.Should().Contain("LIMIT_REACHED");
    }

    [Fact]
    public async Task Support_tickets_and_super_admin_inbox_prioritize_priority_support_and_isolate_tenants()
    {
        var client = factory.CreateClient();
        var superAdmin = await IntegrationTestHelpers.LoginAsSuperAdminAsync(client);
        var starterTenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13d-sup-std", "Starter");
        var proTenant = await IntegrationTestHelpers.CreateTestTenantAsync(client, superAdmin.AccessToken, "13d-sup-pro", "Pro");

        var starterAdmin = await IntegrationTestHelpers.LoginTenantUserAsync(client, starterTenant.Subdomain, $"admin@{starterTenant.Subdomain}.lk", "AdminUser@123!");
        var proAdmin = await IntegrationTestHelpers.LoginTenantUserAsync(client, proTenant.Subdomain, $"admin@{proTenant.Subdomain}.lk", "AdminUser@123!");

        // Starter tenant creates a standard support ticket
        using var createStdReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/support/tickets",
            starterAdmin.AccessToken,
            starterTenant.Subdomain,
            new CreateSupportTicketRequest("CSV import question", "How do I format guardian phone numbers?", SupportTicketCategory.General));
        var createStdRes = await client.SendAsync(createStdReq);
        createStdRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var stdTicket = await createStdRes.Content.ReadFromJsonAsync<SupportTicketDetailDto>(IntegrationTestHelpers.JsonOptions);
        stdTicket!.IsPriority.Should().BeFalse();
        stdTicket.TicketNumber.Should().StartWith("TKT-");

        // Pro tenant (PrioritySupport enabled) creates a priority support ticket
        using var createProReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            "/api/support/tickets",
            proAdmin.AccessToken,
            proTenant.Subdomain,
            new CreateSupportTicketRequest("Urgent backup restore", "Please help restore yesterday's archive.", SupportTicketCategory.BackupRestore));
        var createProRes = await client.SendAsync(createProReq);
        createProRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var proTicket = await createProRes.Content.ReadFromJsonAsync<SupportTicketDetailDto>(IntegrationTestHelpers.JsonOptions);
        proTicket!.IsPriority.Should().BeTrue();

        // Tenant isolation: Starter tenant cannot see or reply to Pro tenant's ticket
        using var crossGetReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            $"/api/support/tickets/{proTicket.Id}",
            starterAdmin.AccessToken,
            starterTenant.Subdomain);
        (await client.SendAsync(crossGetReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var crossReplyReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/support/tickets/{proTicket.Id}/reply",
            starterAdmin.AccessToken,
            starterTenant.Subdomain,
            new ReplySupportTicketRequest("Cross-tenant probe"));
        (await client.SendAsync(crossReplyReq)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Non-SuperAdmin cannot access Super Admin inbox
        using var forbiddenInboxReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Get,
            "/api/super-admin/support/tickets",
            proAdmin.AccessToken,
            proTenant.Subdomain);
        (await client.SendAsync(forbiddenInboxReq)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Super Admin inbox shows both tickets, sorting PrioritySupport first
        using var inboxReq = new HttpRequestMessage(HttpMethod.Get, "/api/super-admin/support/tickets");
        inboxReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var inbox = await (await client.SendAsync(inboxReq))
            .Content.ReadFromJsonAsync<SuperAdminSupportInboxDto>(IntegrationTestHelpers.JsonOptions);
        inbox!.Items.Should().Contain(t => t.Id == proTicket.Id && t.IsPriority);
        inbox.Items.Should().Contain(t => t.Id == stdTicket.Id && !t.IsPriority);

        var proIdx = inbox.Items.ToList().FindIndex(t => t.Id == proTicket.Id);
        var stdIdx = inbox.Items.ToList().FindIndex(t => t.Id == stdTicket.Id);
        proIdx.Should().BeLessThan(stdIdx);

        // Priority-only filter returns only PrioritySupport tickets
        using var prioOnlyReq = new HttpRequestMessage(HttpMethod.Get, "/api/super-admin/support/tickets?priorityOnly=true");
        prioOnlyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var prioInbox = await (await client.SendAsync(prioOnlyReq))
            .Content.ReadFromJsonAsync<SuperAdminSupportInboxDto>(IntegrationTestHelpers.JsonOptions);
        prioInbox!.Items.Should().OnlyContain(t => t.IsPriority);

        // Super Admin replies to Pro ticket -> transitions to WaitingOnInstitute and emails creator
        using var saReplyReq = new HttpRequestMessage(HttpMethod.Post, $"/api/super-admin/support/tickets/{proTicket.Id}/reply")
        {
            Content = JsonContent.Create(new SuperAdminReplySupportTicketRequest("We validated your dry run and can proceed."))
        };
        saReplyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdmin.AccessToken);
        var saReplyRes = await client.SendAsync(saReplyReq);
        saReplyRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterSaReply = await saReplyRes.Content.ReadFromJsonAsync<SuperAdminSupportTicketDetailDto>(IntegrationTestHelpers.JsonOptions);
        afterSaReply!.Status.Should().Be("WaitingOnInstitute");
        afterSaReply.Messages.Should().HaveCount(2);
        afterSaReply.Messages[1].IsFromSuperAdmin.Should().BeTrue();

        // Tenant replies -> transitions back to Open
        using var tenantReplyReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/support/tickets/{proTicket.Id}/reply",
            proAdmin.AccessToken,
            proTenant.Subdomain,
            new ReplySupportTicketRequest("Confirmed, please restore now."));
        var afterTenantReply = await (await client.SendAsync(tenantReplyReq))
            .Content.ReadFromJsonAsync<SupportTicketDetailDto>(IntegrationTestHelpers.JsonOptions);
        afterTenantReply!.Status.Should().Be("Open");
        afterTenantReply.Messages.Should().HaveCount(3);

        // Tenant closes ticket -> subsequent reply is rejected
        using var closeReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/support/tickets/{proTicket.Id}/close",
            proAdmin.AccessToken,
            proTenant.Subdomain);
        var closed = await (await client.SendAsync(closeReq))
            .Content.ReadFromJsonAsync<SupportTicketDetailDto>(IntegrationTestHelpers.JsonOptions);
        closed!.Status.Should().Be("Closed");

        using var replyAfterCloseReq = IntegrationTestHelpers.CreateTenantRequest(
            HttpMethod.Post,
            $"/api/support/tickets/{proTicket.Id}/reply",
            proAdmin.AccessToken,
            proTenant.Subdomain,
            new ReplySupportTicketRequest("One more thing"));
        (await client.SendAsync(replyAfterCloseReq)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
