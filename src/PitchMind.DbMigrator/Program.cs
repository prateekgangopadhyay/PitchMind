using Npgsql;
using Azure.Core;
using Azure.Identity;
using PitchMind.Infrastructure.Storage;

var runtimeProfile = Environment.GetEnvironmentVariable("Runtime__Profile");

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__PitchMind");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("A PostgreSQL migration connection string is required.");

var connection = new NpgsqlConnectionStringBuilder(connectionString);
if (string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase))
{
    if (connection.Host is not ("127.0.0.1" or "localhost" or "::1"))
        throw new InvalidOperationException("The LOCAL database migrator only connects to a loopback PostgreSQL endpoint.");

    var apiPassword = Environment.GetEnvironmentVariable("PITCHMIND_API_PASSWORD");
    if (string.IsNullOrWhiteSpace(apiPassword))
        throw new InvalidOperationException("PITCHMIND_API_PASSWORD is required to provision the restricted LOCAL API role.");

    await using var dataSource = NpgsqlDataSource.Create(connectionString);
    await PostgresSchemaMigrator.ApplyAsync(dataSource, CancellationToken.None);
    await LocalApiRoleProvisioner.ProvisionAsync(dataSource, apiPassword, CancellationToken.None);
    Console.WriteLine($"Applied LOCAL PostgreSQL schema migrations through version {PostgresSchemaMigrator.CurrentSchemaVersion} and refreshed the restricted API role.");
}
else if (string.Equals(runtimeProfile, "azure", StringComparison.OrdinalIgnoreCase))
{
    var managedIdentityClientId = Environment.GetEnvironmentVariable("Azure__ManagedIdentityClientId");
    var apiIdentityObjectId = Environment.GetEnvironmentVariable("PitchMind__ApiIdentityObjectId");
    if (!Guid.TryParse(managedIdentityClientId, out _) || !Guid.TryParse(apiIdentityObjectId, out _))
        throw new InvalidOperationException("Azure migration requires Azure__ManagedIdentityClientId and PitchMind__ApiIdentityObjectId GUIDs.");

    TokenCredential credential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId));
    await using var dataSource = AzurePostgresDataSourceFactory.Create(connectionString, credential);
    await PostgresSchemaMigrator.ApplyAsync(dataSource, CancellationToken.None);
    await AzureApiRoleProvisioner.ProvisionAsync(dataSource, apiIdentityObjectId, CancellationToken.None);
    Console.WriteLine($"Applied Azure PostgreSQL schema migrations through version {PostgresSchemaMigrator.CurrentSchemaVersion} and provisioned the restricted API Entra role.");
}
else
{
    throw new InvalidOperationException("The database migrator requires Runtime:Profile=local or azure.");
}
