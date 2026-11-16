using FluentAssertions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace EduFlow.IntegrationTests;

public sealed class SqlServerContainerTests
{
    [DockerFact]
    public async Task Container_accepts_a_query()
    {
        await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

        await container.StartAsync();

        await using var connection = new SqlConnection(container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        var result = await command.ExecuteScalarAsync();

        result.Should().Be(1);
    }
}
