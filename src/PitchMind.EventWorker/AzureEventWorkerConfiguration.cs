using Azure.Identity;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
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
        services.AddSingleton<TokenCredential>(_ => new ManagedIdentityCredential(
            managedIdentityClientId is null
                ? ManagedIdentityId.SystemAssigned
                : ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId)));
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

    public static void AddAzureProjectionConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        var eventHubNamespace = ValidateEventHubNamespace(Required(configuration["EventHubs:Namespace"], "EventHubs:Namespace"));
        var eventHubName = ValidateResourceName(Required(configuration["EventHubs:HubName"], "EventHubs:HubName"), "EventHubs:HubName");
        var consumerGroup = ValidateResourceName(Required(configuration["EventHubs:ConsumerGroup"], "EventHubs:ConsumerGroup"), "EventHubs:ConsumerGroup");
        var consumerId = Required(configuration["EventHubs:ProjectionConsumerId"], "EventHubs:ProjectionConsumerId");
        if (consumerId.Length > 128)
            throw new InvalidOperationException("EventHubs:ProjectionConsumerId must contain at most 128 characters.");

        var blobServiceUri = ValidateBlobServiceUri(Required(configuration["Storage:BlobServiceUri"], "Storage:BlobServiceUri"));
        var checkpointContainer = ValidateContainerName(Required(configuration["Storage:CheckpointContainer"], "Storage:CheckpointContainer"));

        services.AddSingleton(serviceProvider => new BlobServiceClient(
            new Uri(blobServiceUri, UriKind.Absolute), serviceProvider.GetRequiredService<TokenCredential>())
            .GetBlobContainerClient(checkpointContainer));
        services.AddSingleton(serviceProvider => new EventProcessorClient(
            serviceProvider.GetRequiredService<BlobContainerClient>(), consumerGroup,
            eventHubNamespace, eventHubName, serviceProvider.GetRequiredService<TokenCredential>()));
        services.AddSingleton<IConsumerCheckpointStore, PostgresConsumerCheckpointStore>();
        services.AddSingleton<IProjectionPoisonStore, PostgresProjectionPoisonStore>();
        services.AddSingleton<PostgresProjectionPublishedConsumer>();
        services.AddSingleton(serviceProvider => new EventHubProjectionEventHandler(
            serviceProvider.GetRequiredService<PostgresProjectionPublishedConsumer>(),
            serviceProvider.GetRequiredService<IProjectionPoisonStore>(),
            serviceProvider.GetRequiredService<IConsumerCheckpointStore>(), consumerId));
        services.AddHostedService<ProjectionConsumerWorker>();
    }

    public static void AddAzureCandidateFeedConsumer(this IServiceCollection services, IConfiguration configuration)
    {
        _ = ValidateEventHubNamespace(Required(configuration["EventHubs:Namespace"], "EventHubs:Namespace"));
        _ = ValidateResourceName(Required(configuration["EventHubs:CandidateHubName"], "EventHubs:CandidateHubName"), "EventHubs:CandidateHubName");
        _ = ValidateResourceName(Required(configuration["EventHubs:CandidateConsumerGroup"], "EventHubs:CandidateConsumerGroup"), "EventHubs:CandidateConsumerGroup");
        _ = ValidateBlobServiceUri(Required(configuration["Storage:BlobServiceUri"], "Storage:BlobServiceUri"));
        _ = ValidateContainerName(Required(configuration["Storage:CheckpointContainer"], "Storage:CheckpointContainer"));
        services.AddSingleton<PitchMind.Infrastructure.Feeds.CandidateFeedPersistence>();
        services.AddHostedService<AzureCandidateEventConsumerWorker>();
    }

    public static bool IsProjectionConsumerEnabled(IConfiguration configuration)
    {
        var raw = configuration["EventHubs:ProjectionConsumerEnabled"];
        if (string.IsNullOrWhiteSpace(raw)) return false;
        return bool.TryParse(raw, out var enabled)
            ? enabled
            : throw new InvalidOperationException("EventHubs:ProjectionConsumerEnabled must be true or false.");
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

    public static string ValidateBlobServiceUri(string value)
    {
        var candidate = Required(value, "Storage:BlobServiceUri");
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Port != 443 || endpoint.UserInfo.Length != 0 ||
            endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            !endpoint.Host.EndsWith(".blob.core.windows.net", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Host.Length <= ".blob.core.windows.net".Length)
            throw new InvalidOperationException("Storage:BlobServiceUri must be an HTTPS Azure Blob Storage service URI.");
        return endpoint.GetLeftPart(UriPartial.Authority);
    }

    private static string ValidateContainerName(string value)
    {
        if (value.Length is < 3 or > 63 || value[0] == '-' || value[^1] == '-' || value.Contains("--", StringComparison.Ordinal) ||
            value.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new InvalidOperationException("Storage:CheckpointContainer must be a valid lowercase Azure container name.");
        return value;
    }

    private static string ValidateResourceName(string value, string settingName)
    {
        if (value.Length > 128 || value.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' or '$')))
            throw new InvalidOperationException($"{settingName} is invalid.");
        return value;
    }

    public static NpgsqlDataSource CreatePostgresDataSource(string connectionString, TokenCredential credential)
        => AzurePostgresDataSourceFactory.Create(connectionString, credential);

    private static string Required(string? value, string settingName) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 512
            ? value.Trim()
            : throw new InvalidOperationException($"Required setting {settingName} is missing or invalid.");
}
