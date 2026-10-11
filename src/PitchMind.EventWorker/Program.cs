using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PitchMind.Application.Abstractions;
using PitchMind.Infrastructure.Storage;

var builder = Host.CreateApplicationBuilder(args);
var profile = builder.Configuration["Runtime:Profile"];
var publisher = builder.Configuration["Outbox:Provider"];
if (!string.Equals(profile, "local", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("EventWorker requires Runtime:Profile=local until an Azure worker adapter is implemented.");
if (!string.Equals(publisher, "postgres-local", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("EventWorker requires Outbox:Provider=postgres-local for its LOCAL delivery sink.");

var connectionString = builder.Configuration.GetConnectionString("Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:Postgres must target the LOCAL Docker PostgreSQL service.");

builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<IOutboxPublisher, LocalPostgresOutboxPublisher>();
builder.Services.AddSingleton(serviceProvider => new PostgresOutboxDispatcher(
    serviceProvider.GetRequiredService<NpgsqlDataSource>(),
    serviceProvider.GetRequiredService<IOutboxPublisher>()));
builder.Services.AddHostedService<PostgresOutboxDispatchWorker>();
await builder.Build().RunAsync();
