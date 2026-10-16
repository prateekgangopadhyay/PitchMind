namespace PitchMind.Application.Abstractions;

/// <summary>Sanitized durable locator for an invalid committed projection notification.</summary>
public sealed record ProjectionPoisonRecord(
    Guid PoisonId,
    string ConsumerId,
    string PartitionId,
    string Offset,
    long BrokerSequenceNumber,
    string PayloadSha256,
    string ReasonCode,
    string ExceptionType);

public interface IProjectionPoisonStore
{
    /// <summary>Idempotently records the poison position and returns its stable durable ID.</summary>
    Task<Guid> RecordAsync(ProjectionPoisonRecord record, CancellationToken cancellationToken);

    Task MarkReplayedAsync(Guid poisonId, CancellationToken cancellationToken);
}
