using System.Net;
using EduFlow.Infrastructure.Persistence;
using EduFlow.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduFlow.IntegrationTests;

public sealed class ProductionSafetyIntegrationTests
{
    private const string StrongProdJwtKey = "Prod_Strong_Jwt_Signing_Key_For_Integration_Tests_2026_Min_32_Bytes!";
    private const string StrongProdPassword = "Prod_Strong_SuperAdmin_Password_2026!";

    [Theory]
    [InlineData("")]
    [InlineData("too-short-jwt-key")]
    [InlineData(JwtOptions.KnownDevSigningKey)]
    public void Production_startup_fails_fast_when_JwtSigningKey_is_missing_short_or_default(string signingKey)
    {
        using var factory = new ProductionApiFactory(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = signingKey,
            ["AllowedHosts"] = "admino.lk;*.admino.lk",
            ["SUPERADMIN_EMAIL"] = "ops@admino.lk",
            ["SUPERADMIN_PASSWORD"] = StrongProdPassword,
            ["Demo:Enabled"] = "false"
        });

        var act = () => factory.CreateClient();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Production_startup_fails_fast_when_AllowedHosts_is_wildcard()
    {
        using var factory = new ProductionApiFactory(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = StrongProdJwtKey,
            ["AllowedHosts"] = "*",
            ["SUPERADMIN_EMAIL"] = "ops@admino.lk",
            ["SUPERADMIN_PASSWORD"] = StrongProdPassword,
            ["Demo:Enabled"] = "false"
        });

        var act = () => factory.CreateClient();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Production_startup_fails_fast_when_SuperAdmin_uses_default_password()
    {
        using var factory = new ProductionApiFactory(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = StrongProdJwtKey,
            ["AllowedHosts"] = "admino.lk;*.admino.lk",
            ["SUPERADMIN_EMAIL"] = "ops@admino.lk",
            ["SUPERADMIN_PASSWORD"] = StartupSecurityValidator.KnownDefaultSuperAdminPassword,
            ["Demo:Enabled"] = "false"
        });

        var act = () => factory.CreateClient();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Production_enforces_AllowedHosts_and_skips_demo_seeding_when_DemoEnabled_is_false()
    {
        using var factory = new ProductionApiFactory(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = StrongProdJwtKey,
            ["AllowedHosts"] = "admino.lk;*.admino.lk",
            ["SUPERADMIN_EMAIL"] = "ops@admino.lk",
            ["SUPERADMIN_PASSWORD"] = StrongProdPassword,
            ["Demo:Enabled"] = "false"
        });

        var client = factory.CreateClient();

        using var badHostReq = new HttpRequestMessage(HttpMethod.Get, "/health");
        badHostReq.Headers.Host = "evil.example.com";
        var badHostRes = await client.SendAsync(badHostReq);
        badHostRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var rootHostReq = new HttpRequestMessage(HttpMethod.Get, "/health");
        rootHostReq.Headers.Host = "admino.lk";
        var rootHostRes = await client.SendAsync(rootHostReq);
        rootHostRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using var subHostReq = new HttpRequestMessage(HttpMethod.Get, "/health");
        subHostReq.Headers.Host = "royal.admino.lk";
        var subHostRes = await client.SendAsync(subHostReq);
        subHostRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Subdomain == "demo")).Should().BeFalse();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == "admin@demo.admino.lk")).Should().BeFalse();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == "superadmin@admino.lk")).Should().BeFalse();
        (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == "ops@admino.lk")).Should().BeTrue();
    }

    private sealed class ProductionApiFactory : WebApplicationFactory<Program>
    {
        private readonly IDictionary<string, string?> _overrides;
        private readonly SqliteConnection _sqliteConnection;
        private readonly string _sqliteConnString;

        public ProductionApiFactory(IDictionary<string, string?> overrides)
        {
            _overrides = new Dictionary<string, string?>(overrides)
            {
                ["Hangfire:Storage"] = "InMemory",
                ["Hangfire:EnableRecurringJobs"] = "false",
                ["ConnectionStrings:Default"] = string.Empty
            };
            _sqliteConnString = $"Data Source=AdminoProdSafety_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";
            _sqliteConnection = new SqliteConnection(_sqliteConnString);
            _sqliteConnection.Open();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(_overrides);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_sqliteConnString));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _sqliteConnection.Dispose();
            }
        }
    }
}
