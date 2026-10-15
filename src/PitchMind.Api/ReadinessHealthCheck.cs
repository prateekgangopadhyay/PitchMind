using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.Api;

public sealed class ReadinessHealthCheck(
    IConfiguration configuration,
    IServiceProvider services,
    ILogger<ReadinessHealthCheck> logger) : IHealthCheck
{
    private static readonly TimeSpan DatabaseProbeTimeout = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionStringConfigured = !string.IsNullOrWhiteSpace(
            configuration.GetConnectionString("PitchMind") ?? configuration["PITCHMIND_POSTGRES"]);

        if (!connectionStringConfigured)
        {
            return HealthCheckResult.Unhealthy("canonical data dependency is not configured");
        }

        var dataSource = services.GetService<NpgsqlDataSource>();
        if (dataSource is null)
            return HealthCheckResult.Unhealthy("PostgreSQL adapter is unavailable");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DatabaseProbeTimeout);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(timeout.Token);
            await using var command = new NpgsqlCommand(
                "SELECT COALESCE(MAX(version), 0) FROM pitchmind.schema_versions", connection);
            command.CommandTimeout = (int)DatabaseProbeTimeout.TotalSeconds;
            var schemaVersion = (int)(await command.ExecuteScalarAsync(timeout.Token) ?? 0);
            if (schemaVersion != PostgresSchemaMigrator.CurrentSchemaVersion)
                return HealthCheckResult.Unhealthy("canonical PostgreSQL schema is not current");

            return HealthCheckResult.Healthy("PostgreSQL is reachable and the canonical schema version is current");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Readiness PostgreSQL probe timed out");
            return HealthCheckResult.Unhealthy("PostgreSQL readiness probe timed out");
        }
        catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
        {
            // Avoid returning connection details, hostnames, or provider error text to callers.
            logger.LogWarning("Readiness PostgreSQL probe failed ({FailureType})", exception.GetType().Name);
            return HealthCheckResult.Unhealthy("PostgreSQL is unavailable");
        }
    }
}
