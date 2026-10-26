using Azure.Core;
using Npgsql;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Creates an Azure PostgreSQL data source authenticated with either user/password credentials or a workload identity token.</summary>
public static class AzurePostgresDataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString, TokenCredential? credential = null)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.Host) ||
            !settings.Host.EndsWith(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.Database) || string.IsNullOrWhiteSpace(settings.Username))
            throw new InvalidOperationException("Azure PostgreSQL requires an Azure Flexible Server host, database and database username.");

        settings.SslMode = settings.SslMode == SslMode.Disable ? SslMode.Require : settings.SslMode;

        // If a password is provided (e.g. read from Key Vault), authenticate internally using database user and password.
        if (!string.IsNullOrEmpty(settings.Password))
        {
            var passwordBuilder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
            return passwordBuilder.Build();
        }

        // Otherwise authenticate using Entra workload identity token.
        ArgumentNullException.ThrowIfNull(credential, nameof(credential));
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
