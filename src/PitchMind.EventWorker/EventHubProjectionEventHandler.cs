using System.Globalization;
using System.Security.Cryptography;
using Azure.Messaging.EventHubs;
using PitchMind.Application.Abstractions;
using PitchMind.Contracts.Streaming;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.EventWorker;

public sealed record ProjectionEvent(
    string? MessageId,
    string? Offset,
    long SequenceNumber,
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, object> Properties);

public sealed class ProjectionEventPoisonException(string reasonCode, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public string ReasonCode { get; } = reasonCode;
}

public sealed class EventHubProjectionEventHandler(
    PostgresProjectionPublishedConsumer consumer,
    IProjectionPoisonStore poisonStore,
    IConsumerCheckpointStore checkpointStore,
    string consumerId)
{
    public async Task HandleAsync(
        EventData data,
        string partitionId,
        Func<CancellationToken, Task> updateBrokerCheckpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        await HandleAsync(new ProjectionEvent(data.MessageId, data.OffsetString, data.SequenceNumber,
            data.EventBody.ToMemory(), new Dictionary<string, object>(data.Properties, StringComparer.Ordinal)),
            partitionId, updateBrokerCheckpoint, cancellationToken);
    }

    public async Task HandleAsync(
        ProjectionEvent data,
        string partitionId,
        Func<CancellationToken, Task> updateBrokerCheckpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(updateBrokerCheckpoint);

        try
        {
            await ConsumeValidEventAsync(data, partitionId, updateBrokerCheckpoint, cancellationToken);
        }
        catch (ProjectionEventPoisonException poison)
        {
            if (string.IsNullOrWhiteSpace(data.Offset) || data.Offset.Length > 128 || data.SequenceNumber < 0)
                throw new InvalidOperationException("Cannot durably isolate a poison event without a valid broker position.", poison);

            var payloadHash = Convert.ToHexString(SHA256.HashData(data.Body.Span)).ToLowerInvariant();
            var poisonId = StablePoisonId(consumerId, partitionId, data.Offset, data.SequenceNumber);
            var durableId = await poisonStore.RecordAsync(new ProjectionPoisonRecord(poisonId, consumerId,
                partitionId, data.Offset, data.SequenceNumber, payloadHash, poison.ReasonCode,
                (poison.InnerException ?? poison).GetType().Name), cancellationToken);
            await checkpointStore.AdvancePoisonAsync(consumerId, partitionId, data.Offset,
                data.SequenceNumber, durableId, cancellationToken);
            await updateBrokerCheckpoint(cancellationToken);
        }
    }

    private async Task ConsumeValidEventAsync(
        ProjectionEvent data,
        string partitionId,
        Func<CancellationToken, Task> updateBrokerCheckpoint,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(data.MessageId, out var messageId) || messageId == Guid.Empty)
            throw new ProjectionEventPoisonException("INVALID_MESSAGE_ID", "Projection event requires a valid Event Hubs message ID.");

        string payloadText;
        try
        {
            payloadText = new System.Text.UTF8Encoding(false, true).GetString(data.Body.Span);
        }
        catch (System.Text.DecoderFallbackException exception)
        {
            throw new ProjectionEventPoisonException("INVALID_UTF8", "Projection event body must be valid UTF-8 JSON.", exception);
        }

        ProjectionPublishedV1Envelope envelope;
        try
        {
            envelope = ProjectionPublishedV1Envelope.Parse(messageId, payloadText);
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or FormatException)
        {
            throw new ProjectionEventPoisonException("INVALID_ENVELOPE", "Projection event does not satisfy ProjectionPublishedV1.", exception);
        }
        ValidateProperties(data, envelope);
        if (string.IsNullOrWhiteSpace(data.Offset))
            throw new ProjectionEventPoisonException("MISSING_BROKER_OFFSET", "Projection event is missing its Event Hubs offset.");
        if (data.SequenceNumber < 0)
            throw new ProjectionEventPoisonException("INVALID_BROKER_SEQUENCE", "Projection event has an invalid broker sequence.");

        var projection = new ConsumerProjectionSnapshot(envelope.MessageId, envelope.MatchId,
            envelope.CanonicalRevision, envelope.ProjectionRevision, envelope.Sequence, envelope.ClockSeconds,
            envelope.AlgorithmVersion, envelope.SourceSha256, envelope.StateSha256, envelope.State.GetRawText());
        try
        {
            await consumer.ConsumeAsync(consumerId, partitionId, data.Offset,
                data.SequenceNumber, messageId, projection, data.Body, cancellationToken);
            var poisonId = StablePoisonId(consumerId, partitionId, data.Offset, data.SequenceNumber);
            await poisonStore.MarkReplayedAsync(poisonId, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new ProjectionEventPoisonException("INVALID_PROJECTION_STATE", "Projection event conflicts with persisted consumer state.", exception);
        }

        // EventProcessorClient processes each partition sequentially in source order.
        // The database effect and durable cursor commit before the SDK checkpoint.
        await updateBrokerCheckpoint(cancellationToken);
    }

    private static Guid StablePoisonId(string consumerId, string partitionId, string offset, long sequence)
    {
        var material = System.Text.Encoding.UTF8.GetBytes(
            $"{consumerId}\0{partitionId}\0{offset}\0{sequence.ToString(CultureInfo.InvariantCulture)}");
        var bytes = SHA256.HashData(material)[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes, bigEndian: true);
    }

    private static void ValidateProperties(ProjectionEvent data, ProjectionPublishedV1Envelope envelope)
    {
        var properties = data.Properties;
        if (!properties.TryGetValue("schemaName", out var schemaName) ||
            !string.Equals(schemaName as string, "ProjectionPublishedV1", StringComparison.Ordinal) ||
            !properties.TryGetValue("matchId", out var matchId) ||
            !string.Equals(matchId as string, envelope.MatchId.ToString("D"), StringComparison.Ordinal) ||
            !ReadLong(properties, "canonicalRevision", envelope.CanonicalRevision) ||
            !ReadLong(properties, "projectionRevision", envelope.ProjectionRevision) ||
            !ReadLong(properties, "sequence", envelope.Sequence) ||
            !properties.TryGetValue("sourceSha256", out var sourceSha256) ||
            !string.Equals(sourceSha256 as string, envelope.SourceSha256, StringComparison.Ordinal))
            throw new ProjectionEventPoisonException("TRANSPORT_METADATA_MISMATCH", "Projection event properties do not match its versioned body.");
    }

    private static bool ReadLong(IReadOnlyDictionary<string, object> properties, string name, long expected) =>
        properties.TryGetValue(name, out var value) &&
        long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsed) && parsed == expected;
}
