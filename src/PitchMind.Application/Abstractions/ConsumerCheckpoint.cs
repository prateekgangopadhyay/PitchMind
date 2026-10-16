namespace PitchMind.Application.Abstractions;

/// <summary>
/// Durable Event Hubs partition position for a consumer. Advance this only after the
/// matching event has been committed through the consumer inbox.
/// </summary>
public sealed record ConsumerCheckpoint(
    string ConsumerId,
    string PartitionId,
    string Offset,
    long BrokerSequenceNumber,
    Guid MatchId,
    Guid MessageId,
    long CanonicalSequence);

public enum ConsumerCheckpointAdvanceResult
{
    Advanced,
    AlreadyCurrent,
    StaleReplay
}

public interface IConsumerCheckpointStore
{
    Task<ConsumerCheckpointAdvanceResult> AdvanceAsync(
        ConsumerCheckpoint checkpoint,
        CancellationToken cancellationToken,
        bool alreadyProcessed = false);
}
