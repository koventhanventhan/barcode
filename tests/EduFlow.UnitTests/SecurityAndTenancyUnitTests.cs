using EduFlow.Application.Common.Security;
using EduFlow.Domain.Common;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Tenancy;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.UnitTests;

public sealed class SecurityAndTenancyUnitTests
{
    [Fact]
    public void PasswordHasher_hashes_and_verifies_password()
    {
        var hash = PasswordHasher.HashPassword("StrongPass@123");

        PasswordHasher.VerifyPassword("StrongPass@123", hash).Should().BeTrue();
        PasswordHasher.VerifyPassword("WrongPass@123", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("short1!")]
    [InlineData("alllowercase123!")]
    [InlineData("ALLUPPERCASE123!")]
    [InlineData("NoNumbersHere!")]
    [InlineData("NoSpecialChar123")]
    public void PasswordPolicy_rejects_weak_passwords(string weakPassword)
    {
        var act = () => PasswordPolicy.ValidateOrThrow(weakPassword);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("PASSWORD_POLICY_VIOLATION");
    }

    [Fact]
    public void PasswordPolicy_accepts_strong_password()
    {
        var errors = PasswordPolicy.Validate("ValidPass#2026");
        errors.Should().BeEmpty();
    }

    [Fact]
    public async Task AppDbContext_stamps_tenant_id_soft_deletes_and_blocks_cross_tenant_writes()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var tenantContext = new TenantContext();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await using (var setupDb = new AppDbContext(options, tenantContext))
        {
            await setupDb.Database.EnsureCreatedAsync();
        }

        tenantContext.SetTenant(tenantA);
        Guid branchAId;
        await using (var dbA = new AppDbContext(options, tenantContext))
        {
            var branch = new Branch { Name = "Kandy Branch", Code = "KDY" };
            dbA.Branches.Add(branch);
            await dbA.SaveChangesAsync();

            branch.TenantId.Should().Be(tenantA);
            branchAId = branch.Id;
        }

        tenantContext.SetTenant(tenantB);
        await using (var dbB = new AppDbContext(options, tenantContext))
        {
            var visibleBranches = await dbB.Branches.ToListAsync();
            visibleBranches.Should().BeEmpty();

            var crossWriteBranch = new Branch
            {
                TenantId = tenantA,
                Name = "Spoofed Branch",
                Code = "SPF"
            };
            dbB.Branches.Add(crossWriteBranch);

            var act = async () => await dbB.SaveChangesAsync();
            await act.Should().ThrowAsync<CrossTenantOperationException>();
        }

        tenantContext.SetTenant(tenantA);
        await using (var dbA = new AppDbContext(options, tenantContext))
        {
            var branch = await dbA.Branches.SingleAsync(b => b.Id == branchAId);
            dbA.Branches.Remove(branch);
            await dbA.SaveChangesAsync();

            (await dbA.Branches.CountAsync()).Should().Be(0);

            var softDeleted = await dbA.Branches.IgnoreQueryFilters().SingleAsync(b => b.Id == branchAId);
            softDeleted.IsDeleted.Should().BeTrue();
        }
    }

    [Fact]
    public void Every_ITenantEntity_has_a_global_query_filter_in_AppDbContext()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        using var db = new AppDbContext(options, new TenantContext());

        var tenantEntityTypes = typeof(TenantEntity).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ITenantEntity).IsAssignableFrom(t))
            .ToList();

        tenantEntityTypes.Should().NotBeEmpty();

        foreach (var clrType in tenantEntityTypes)
        {
            var entityType = db.Model.FindEntityType(clrType);
            entityType.Should().NotBeNull($"Entity {clrType.Name} must be registered in AppDbContext");
            entityType!.GetQueryFilter().Should().NotBeNull($"Entity {clrType.Name} must have a global query filter");
        }
    }
}
