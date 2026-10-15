using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class IsolatedPostgresReadinessFactAttribute : FactAttribute
{
    public IsolatedPostgresReadinessFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES")) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES_VOLUME")))
        {
            Skip = "Set PITCHMIND_TEST_POSTGRES and PITCHMIND_TEST_POSTGRES_VOLUME to a dedicated local test database and an explicit tmpfs provenance label; this test must not use pitchmind_postgres_data.";
        }
    }
}

public sealed class PostgresReadinessIntegrationTests
{
    [IsolatedPostgresReadinessFact]
    public async Task Readiness_returns_ok_when_isolated_postgres_is_reachable()
    {
        var connectionString = Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES")!;
        var volumeName = Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES_VOLUME")!;
        var connection = new NpgsqlConnectionStringBuilder(connectionString);

        // Guard the database name, loopback-only port, and explicit ephemeral-storage label.
        // The invoking harness must verify the actual container mount; this test performs only
        // the readiness probe's SELECT 1 against the dedicated test database.
        Assert.StartsWith("tmpfs:", volumeName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("pitchmind_readiness_test", connection.Database);
        Assert.True(connection.Port != 5432, "The application PostgreSQL port must not be used.");
        Assert.True(IsLoopbackHost(connection.Host), "The test database must be reachable only through loopback.");

        await using (var database = new NpgsqlConnection(connectionString))
        {
            await database.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT 1", database);
            Assert.Equal(1, await command.ExecuteScalarAsync());
        }

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:PitchMind", connectionString);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PitchMind"] = connectionString
                }));
        });
        var configuredConnection = new NpgsqlConnectionStringBuilder(
            factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("PitchMind"));
        Assert.Equal(connection.Host, configuredConnection.Host);
        Assert.Equal(connection.Port, configuredConnection.Port);
        Assert.Equal(connection.Database, configuredConnection.Database);
        Assert.NotNull(factory.Services.GetService<NpgsqlDataSource>());
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Empty(await ready.Content.ReadAsStringAsync());
    }

    private static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
