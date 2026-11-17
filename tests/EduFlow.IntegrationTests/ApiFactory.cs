using EduFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

namespace EduFlow.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private MsSqlContainer? _msSqlContainer;
    private SqliteConnection? _sqliteConnection;

    public async Task InitializeAsync()
    {
        if (DockerFactAttribute.DockerAvailable.Value)
        {
            _msSqlContainer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                .Build();
            await _msSqlContainer.StartAsync();
        }
        else
        {
            _sqliteConnection = new SqliteConnection("DataSource=:memory:");
            await _sqliteConnection.OpenAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_msSqlContainer is null && _sqliteConnection is null)
        {
            _sqliteConnection = new SqliteConnection("DataSource=:memory:");
            _sqliteConnection.Open();
        }

        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
            {
                if (_msSqlContainer is not null)
                {
                    options.UseSqlServer(
                        _msSqlContainer.GetConnectionString(),
                        sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
                }
                else
                {
                    options.UseSqlite(_sqliteConnection!);
                }
            });
        });
    }

    public new async Task DisposeAsync()
    {
        if (_msSqlContainer is not null)
        {
            await _msSqlContainer.DisposeAsync();
        }

        if (_sqliteConnection is not null)
        {
            await _sqliteConnection.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
