using Npgsql;
using PitchMind.Infrastructure.Storage;

if (!string.Equals(Environment.GetEnvironmentVariable("Runtime__Profile"), "local", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("The database migrator requires Runtime:Profile=local.");

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgres must identify the local migration connection.");

var connection = new NpgsqlConnectionStringBuilder(connectionString);
if (connection.Host is not ("127.0.0.1" or "localhost" or "::1"))
    throw new InvalidOperationException("The local database migrator only connects to a loopback PostgreSQL endpoint.");

await using var dataSource = NpgsqlDataSource.Create(connectionString);
await PostgresSchemaMigrator.ApplyAsync(dataSource, CancellationToken.None);
Console.WriteLine($"Applied local PostgreSQL schema migrations through version {PostgresSchemaMigrator.CurrentSchemaVersion}.");
