using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using PitchMind.Infrastructure.Feeds;

namespace PitchMind.Api;

/// <summary>Publishes source-bound candidate envelopes to the pre-provisioned DEV Event Hub.</summary>
public sealed class AzureCandidateEventHubPublisher(EventHubProducerClient producer) : ICandidateEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        _ = await producer.GetPartitionIdsAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(CandidateReplayEnvelope envelope, CancellationToken cancellationToken)
    {
        var source = envelope.SourceRecord;
        var message = new CandidateKafkaMessage(envelope.ContractVersion, envelope.SourceSha256, source.SourceMatchId,
            source.SourceEventId, source.SourceSequence, source.RawClockValue, source.RawPeriod, source.RawEventType, source.RawPayload);
        var data = new EventData(JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions))
        {
            MessageId = $"{message.SourceSha256}:{message.SourceMatchId}:{message.SourceSequence}"
        };
        data.Properties["schemaName"] = "CandidateRawEventV1";
        data.Properties["sourceSha256"] = message.SourceSha256;
        data.Properties["sourceMatchId"] = message.SourceMatchId;
        data.Properties["sourceEventId"] = message.SourceEventId;
        data.Properties["sourceSequence"] = message.SourceSequence;
        using var batch = await producer.CreateBatchAsync(new CreateBatchOptions
        {
            PartitionKey = message.SourceMatchId
        }, cancellationToken).ConfigureAwait(false);
        if (!batch.TryAdd(data)) throw new InvalidDataException("Candidate event exceeds the Event Hubs message size limit.");
        await producer.SendAsync(batch, cancellationToken).ConfigureAwait(false);
    }
}
