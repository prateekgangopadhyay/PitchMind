using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using PitchMind.Application.Abstractions;
using PitchMind.Contracts.Streaming;

namespace PitchMind.EventWorker;

public sealed class EventHubOutboxPublisher(IEventHubEventSender sender) : IOutboxPublisher
{
    public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
    {
        var envelope = ProjectionPublishedV1Envelope.Parse(messageId, messageJson);
        var eventData = new EventData(BinaryData.FromString(messageJson))
        {
            MessageId = envelope.MessageId.ToString("D"),
            ContentType = "application/json"
        };
        eventData.Properties["schemaName"] = "ProjectionPublishedV1";
        eventData.Properties["matchId"] = envelope.MatchId.ToString("D");
        eventData.Properties["canonicalRevision"] = envelope.CanonicalRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        eventData.Properties["projectionRevision"] = envelope.ProjectionRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        eventData.Properties["sequence"] = envelope.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        eventData.Properties["sourceSha256"] = envelope.SourceSha256;

        await sender.SendAsync(eventData, envelope.MatchId.ToString("D"), cancellationToken);
    }
}

public interface IEventHubEventSender
{
    Task SendAsync(EventData eventData, string partitionKey, CancellationToken cancellationToken);
}

public sealed class AzureEventHubEventSender(EventHubProducerClient producer) : IEventHubEventSender, IAsyncDisposable
{
    public Task SendAsync(EventData eventData, string partitionKey, CancellationToken cancellationToken) =>
        producer.SendAsync([eventData], new SendEventOptions { PartitionKey = partitionKey }, cancellationToken);

    public ValueTask DisposeAsync() => producer.DisposeAsync();
}
