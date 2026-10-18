using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PitchMind.Infrastructure.Feeds;

namespace PitchMind.EventWorker;

/// <summary>Stages source events in PostgreSQL before advancing the durable Event Hubs checkpoint.</summary>
public sealed class AzureCandidateEventConsumerWorker(
    IConfiguration configuration,
    TokenCredential credential,
    CandidateFeedPersistence persistence,
    ILogger<AzureCandidateEventConsumerWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private EventProcessorClient? _processor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var eventHubNamespace = AzureEventWorkerConfiguration.ValidateEventHubNamespace(
            Required(configuration["EventHubs:Namespace"], "EventHubs:Namespace"));
        var eventHubName = Required(configuration["EventHubs:CandidateHubName"], "EventHubs:CandidateHubName");
        var consumerGroup = Required(configuration["EventHubs:CandidateConsumerGroup"], "EventHubs:CandidateConsumerGroup");
        var blobUri = AzureEventWorkerConfiguration.ValidateBlobServiceUri(
            Required(configuration["Storage:BlobServiceUri"], "Storage:BlobServiceUri"));
        var containerName = Required(configuration["Storage:CheckpointContainer"], "Storage:CheckpointContainer");
        var container = new BlobServiceClient(new Uri(blobUri), credential).GetBlobContainerClient(containerName);
        _processor = new EventProcessorClient(container, consumerGroup, eventHubNamespace, eventHubName, credential);
        _processor.ProcessEventAsync += ProcessEventAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
        logger.LogInformation("Candidate Event Hubs consumer starting for {Hub} / {Group}.", eventHubName, consumerGroup);
        await _processor.StartProcessingAsync(stoppingToken).ConfigureAwait(false);
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await _processor.StopProcessingAsync(CancellationToken.None).ConfigureAwait(false);
            _processor.ProcessEventAsync -= ProcessEventAsync;
            _processor.ProcessErrorAsync -= ProcessErrorAsync;
        }
    }

    private async Task ProcessEventAsync(ProcessEventArgs args)
    {
        var data = args.Data;
        if (data is null) return;
        var raw = data.EventBody.ToArray();
        try
        {
            var message = JsonSerializer.Deserialize<CandidateKafkaMessage>(raw, JsonOptions)
                ?? throw new InvalidDataException("Event Hubs candidate record was empty.");
            if (!PropertyEquals(data, "schemaName", "CandidateRawEventV1") ||
                !PropertyEquals(data, "sourceSha256", message.SourceSha256) ||
                !PropertyEquals(data, "sourceMatchId", message.SourceMatchId) ||
                !PropertyEquals(data, "sourceEventId", message.SourceEventId) ||
                !PropertyEquals(data, "sourceSequence", message.SourceSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                throw new InvalidDataException("Event Hubs application properties do not match the source envelope.");
            await persistence.EnqueueAsync(message, args.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or KeyNotFoundException)
        {
            await persistence.RecordPoisonAsync("event-hubs-azure", args.Partition.PartitionId,
                data.SequenceNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), raw,
                "INVALID_CANDIDATE_EVENT", ex.GetType().Name, args.CancellationToken).ConfigureAwait(false);
        }
        // A source event or its quarantine record is durable before Event Hubs advances the offset.
        await args.UpdateCheckpointAsync(args.CancellationToken).ConfigureAwait(false);
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Candidate Event Hubs callback failed for {Operation} partition {PartitionId}.",
            args.Operation, args.PartitionId);
        return Task.CompletedTask;
    }

    private static bool PropertyEquals(EventData data, string key, string expected) =>
        data.Properties.TryGetValue(key, out var value) && string.Equals(Convert.ToString(value,
            System.Globalization.CultureInfo.InvariantCulture), expected, StringComparison.Ordinal);

    private static string Required(string? value, string key) => !string.IsNullOrWhiteSpace(value)
        ? value.Trim()
        : throw new InvalidOperationException($"Required Azure candidate-consumer setting {key} is missing.");
}
