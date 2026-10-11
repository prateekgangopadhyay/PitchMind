using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace PitchMind.Api;

public sealed class ReadinessHealthCheck(
    IHostEnvironment environment,
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
            var syntheticLocalMode = environment.IsDevelopment() && configuration["Runtime:Profile"] == "local";
            return syntheticLocalMode
                ? HealthCheckResult.Healthy("local synthetic mode")
                : HealthCheckResult.Unhealthy("required data dependency is not configured");
        }

        var dataSource = services.GetService<NpgsqlDataSource>();
        if (dataSource is null)
            return HealthCheckResult.Unhealthy("PostgreSQL adapter is unavailable");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DatabaseProbeTimeout);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            command.CommandTimeout = (int)DatabaseProbeTimeout.TotalSeconds;
            await command.ExecuteScalarAsync(timeout.Token);
            return HealthCheckResult.Healthy("PostgreSQL is reachable");
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
