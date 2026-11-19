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
    private readonly string _sqliteConnectionString = $"Data Source=Admino_{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Default Timeout=30";

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
                InitialCatalog = $"AdminoTest_{Guid.NewGuid():N}"
            };
            _sqlServerConnectionString = builder.ConnectionString;
        }
        else
        {
            _sqliteConnection = new SqliteConnection(_sqliteConnectionString);
            await _sqliteConnection.OpenAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        if (_sqlServerConnectionString is null && _sqliteConnection is null)
        {
            _sqliteConnection = new SqliteConnection(_sqliteConnectionString);
            _sqliteConnection.Open();
        }

        builder.UseEnvironment("Development");
        builder.UseSetting("Sms:Provider", "Fake");
        builder.UseSetting("Sms:TestMode", "false");
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
                    options.UseSqlite(_sqliteConnectionString);
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
