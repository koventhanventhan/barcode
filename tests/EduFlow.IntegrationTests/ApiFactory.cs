using EduFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace EduFlow.IntegrationTests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly SemaphoreSlim SharedContainerLock = new(1, 1);
    private static MsSqlContainer? _sharedMsSqlContainer;
    private static bool _sharedMsSqlFailed;

    private string? _sqlServerConnectionString;
    private SqliteConnection? _sqliteConnection;

    public async Task InitializeAsync()
    {
        if (DockerFactAttribute.DockerAvailable.Value && !_sharedMsSqlFailed)
        {
            await SharedContainerLock.WaitAsync();
            try
            {
                if (_sharedMsSqlContainer is null && !_sharedMsSqlFailed)
                {
                    try
                    {
                        var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
                            .Build();
                        await container.StartAsync();
                        _sharedMsSqlContainer = container;
                    }
                    catch
                    {
                        _sharedMsSqlFailed = true;
                    }
                }
            }
            finally
            {
                SharedContainerLock.Release();
            }
        }

        if (_sharedMsSqlContainer is not null)
        {
            var builder = new SqlConnectionStringBuilder(_sharedMsSqlContainer.GetConnectionString())
            {
                InitialCatalog = $"EduFlowTest_{Guid.NewGuid():N}"
            };
            _sqlServerConnectionString = builder.ConnectionString;
        }
        else
        {
            _sqliteConnection = new SqliteConnection("DataSource=:memory:");
            await _sqliteConnection.OpenAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_sqlServerConnectionString is null && _sqliteConnection is null)
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
                if (_sqlServerConnectionString is not null)
                {
                    options.UseSqlServer(
                        _sqlServerConnectionString,
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
        if (_sqliteConnection is not null)
        {
            await _sqliteConnection.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
