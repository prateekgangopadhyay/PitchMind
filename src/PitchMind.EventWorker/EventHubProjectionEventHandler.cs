using System.Globalization;
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

public sealed class EventHubProjectionEventHandler(
    PostgresProjectionPublishedConsumer consumer,
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

        if (!Guid.TryParse(data.MessageId, out var messageId) || messageId == Guid.Empty)
            throw new InvalidDataException("Projection event requires a valid Event Hubs message ID.");

        var payloadBytes = data.Body;
        string payloadText;
        try
        {
            payloadText = new System.Text.UTF8Encoding(false, true).GetString(payloadBytes.Span);
        }
        catch (System.Text.DecoderFallbackException exception)
        {
            throw new InvalidDataException("Projection event body must be valid UTF-8 JSON.", exception);
        }
        var envelope = ProjectionPublishedV1Envelope.Parse(messageId, payloadText);
        ValidateProperties(data, envelope);
        if (string.IsNullOrWhiteSpace(data.Offset))
            throw new InvalidDataException("Projection event is missing its Event Hubs offset.");

        var projection = new ConsumerProjectionSnapshot(envelope.MessageId, envelope.MatchId,
            envelope.CanonicalRevision, envelope.ProjectionRevision, envelope.Sequence, envelope.ClockSeconds,
            envelope.AlgorithmVersion, envelope.SourceSha256, envelope.StateSha256, envelope.State.GetRawText());
        await consumer.ConsumeAsync(consumerId, partitionId, data.Offset,
            data.SequenceNumber, messageId, projection, payloadBytes, cancellationToken);

        // EventProcessorClient processes each partition sequentially in source order.
        // Advancing the Blob checkpoint for a safely deduplicated stale replay lets it
        // catch up when it lagged behind the durable PostgreSQL cursor.
        await updateBrokerCheckpoint(cancellationToken);
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
            throw new InvalidDataException("Projection event properties do not match its versioned body.");
    }

    private static bool ReadLong(IReadOnlyDictionary<string, object> properties, string name, long expected) =>
        properties.TryGetValue(name, out var value) &&
        long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None,
            CultureInfo.InvariantCulture, out var parsed) && parsed == expected;
}
