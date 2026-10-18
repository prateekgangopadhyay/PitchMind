using Azure.Core;
using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Creates an Azure PostgreSQL data source authenticated with a workload identity token.</summary>
public static class AzurePostgresDataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.Host) ||
            !settings.Host.EndsWith(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.Database) || string.IsNullOrWhiteSpace(settings.Username) ||
            !string.IsNullOrEmpty(settings.Password))
            throw new InvalidOperationException("Azure PostgreSQL requires an Azure Flexible Server host, database and Entra database username; passwords are not accepted.");
        settings.SslMode = SslMode.VerifyFull;

        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        builder.UsePeriodicPasswordProvider(
            async (_, cancellationToken) =>
            {
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]), cancellationToken)
                    .ConfigureAwait(false);
                return token.Token;
            },
            successRefreshInterval: TimeSpan.FromMinutes(45),
            failureRefreshInterval: TimeSpan.FromSeconds(30));
        return builder.Build();
    }
}
