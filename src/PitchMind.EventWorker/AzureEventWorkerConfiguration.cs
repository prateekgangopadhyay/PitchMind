using Azure.Identity;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.EventWorker;

public static class AzureEventWorkerConfiguration
{
    public static void ValidateRuntimeProfile(string? runtimeProfile)
    {
        if (!string.Equals(runtimeProfile, "azure", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("EventWorker requires Runtime:Profile=azure; local outbox dispatch is hosted by the API's local adapter.");
    }

    public static void AddAzureOutboxDispatch(this IServiceCollection services, IConfiguration configuration)
    {
        var eventHubNamespace = Required(configuration["EventHubs:Namespace"], "EventHubs:Namespace");
        var eventHubName = Required(configuration["EventHubs:HubName"], "EventHubs:HubName");
        var postgresConnectionString = Required(configuration.GetConnectionString("PitchMind"), "ConnectionStrings:PitchMind");
        var managedIdentityClientId = configuration["Azure:ManagedIdentityClientId"];
        if (managedIdentityClientId is not null && (managedIdentityClientId.Length > 128 || !Guid.TryParse(managedIdentityClientId, out _)))
            throw new InvalidOperationException("Azure:ManagedIdentityClientId must be a valid GUID when supplied.");

        var eventHubHost = ValidateEventHubNamespace(eventHubNamespace);
        services.AddSingleton<TokenCredential>(_ => managedIdentityClientId is null
            ? new ManagedIdentityCredential()
            : new ManagedIdentityCredential(managedIdentityClientId));
        services.AddSingleton<NpgsqlDataSource>(serviceProvider =>
            CreatePostgresDataSource(postgresConnectionString, serviceProvider.GetRequiredService<TokenCredential>()));
        services.AddSingleton(serviceProvider => new EventHubProducerClient(eventHubHost, eventHubName,
            serviceProvider.GetRequiredService<TokenCredential>(), new EventHubProducerClientOptions
        {
            RetryOptions = new EventHubsRetryOptions
            {
                Mode = EventHubsRetryMode.Exponential,
                MaximumRetries = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                MaximumDelay = TimeSpan.FromSeconds(5)
            }
        }));

        services.AddSingleton<IEventHubEventSender, AzureEventHubEventSender>();
        services.AddSingleton<IOutboxPublisher, EventHubOutboxPublisher>();
        services.AddSingleton<PostgresOutboxDispatcher>();
    }

    public static string ValidateEventHubNamespace(string value)
    {
        var candidate = Required(value, "EventHubs:Namespace");
        if (!Uri.TryCreate($"https://{candidate}", UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Port != 443 || endpoint.UserInfo.Length != 0 ||
            endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            !endpoint.Host.EndsWith(".servicebus.windows.net", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Host.Length <= ".servicebus.windows.net".Length)
            throw new InvalidOperationException("EventHubs:Namespace must be an Azure Event Hubs namespace host.");
        return endpoint.Host.ToLowerInvariant();
    }

    public static NpgsqlDataSource CreatePostgresDataSource(string connectionString, TokenCredential credential)
    {
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(settings.Host) ||
            !settings.Host.EndsWith(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(settings.Database) ||
            string.IsNullOrWhiteSpace(settings.Username) || !string.IsNullOrEmpty(settings.Password))
            throw new InvalidOperationException("Azure PostgreSQL requires host, database and username with managed identity authentication; password credentials are not accepted.");
        settings.SslMode = SslMode.VerifyFull;

        var builder = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        builder.UsePeriodicPasswordProvider(
            async (_, cancellationToken) =>
            {
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]), cancellationToken);
                return token.Token;
            },
            successRefreshInterval: TimeSpan.FromMinutes(45),
            failureRefreshInterval: TimeSpan.FromSeconds(30));
        return builder.Build();
    }

    private static string Required(string? value, string settingName) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 512
            ? value.Trim()
            : throw new InvalidOperationException($"Required setting {settingName} is missing or invalid.");
}
