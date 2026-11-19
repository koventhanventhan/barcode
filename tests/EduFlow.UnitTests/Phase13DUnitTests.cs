using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using EduFlow.Infrastructure.Documents;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.UnitTests;

public sealed class Phase13DUnitTests
{
    [Fact]
    public void ZxingBarcodeEncoder_EncodeQr_produces_square_binary_matrix()
    {
        var encoder = new ZxingBarcodeEncoder();

        var qr = encoder.EncodeQr("https://demo.admino.lk/staff-register?token=abc123");

        qr.Size.Should().BeGreaterThanOrEqualTo(21);
        qr.Modules.Length.Should().Be(qr.Size * qr.Size);
        qr.Modules.Should().MatchRegex("^[01]+$");
        qr.Modules.Should().Contain("1").And.Contain("0");

        var act = () => encoder.EncodeQr("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task New_Phase13D_entities_enforce_tenant_isolation_and_block_cross_tenant_writes()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var tenantContext = new TenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var setup = new AppDbContext(options, tenantContext))
        {
            await setup.Database.EnsureCreatedAsync();
        }

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        tenantContext.SetTenant(tenantA);
        await using (var dbA = new AppDbContext(options, tenantContext))
        {
            var link = new StaffRegistrationLink { Token = "tok-a", IsEnabled = true };
            var req = new StaffRegistrationRequest
            {
                FullName = "Nimal Silva",
                Email = "nimal@a.lk",
                PasswordHash = "hash",
                RequestedRoleType = RoleType.Teacher
            };
            var ticket = new SupportTicket
            {
                TicketSequence = 1,
                TicketNumber = "TKT-000001",
                Subject = "Printer issue",
                Category = SupportTicketCategory.Technical,
                CreatedByUserId = Guid.NewGuid(),
                CreatedByName = "Admin A",
                CreatedByEmail = "admin@a.lk",
                LastMessageAt = DateTimeOffset.UtcNow
            };
            dbA.StaffRegistrationLinks.Add(link);
            dbA.StaffRegistrationRequests.Add(req);
            dbA.SupportTickets.Add(ticket);
            dbA.SupportTicketMessages.Add(new SupportTicketMessage
            {
                TicketId = ticket.Id,
                SenderUserId = ticket.CreatedByUserId,
                SenderName = "Admin A",
                SenderRole = "InstituteAdmin",
                Body = "Details"
            });
            await dbA.SaveChangesAsync();

            link.TenantId.Should().Be(tenantA);
            req.TenantId.Should().Be(tenantA);
            ticket.TenantId.Should().Be(tenantA);
        }

        tenantContext.SetTenant(tenantB);
        await using (var dbB = new AppDbContext(options, tenantContext))
        {
            (await dbB.StaffRegistrationLinks.CountAsync()).Should().Be(0);
            (await dbB.StaffRegistrationRequests.CountAsync()).Should().Be(0);
            (await dbB.SupportTickets.CountAsync()).Should().Be(0);
            (await dbB.SupportTicketMessages.CountAsync()).Should().Be(0);

            dbB.SupportTickets.Add(new SupportTicket
            {
                TenantId = tenantA,
                TicketSequence = 2,
                TicketNumber = "TKT-000002",
                Subject = "Spoofed",
                CreatedByUserId = Guid.NewGuid(),
                CreatedByName = "X",
                CreatedByEmail = "x@b.lk",
                LastMessageAt = DateTimeOffset.UtcNow
            });

            var act = async () => await dbB.SaveChangesAsync();
            await act.Should().ThrowAsync<CrossTenantOperationException>();
        }
    }
}
