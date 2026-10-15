using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;

namespace PitchMind.Application.Matches;

/// <summary>
/// Runs canonical admission with caller-supplied source lineage and persists every classified
/// ordering/dedupe rejection as sanitized quarantine evidence before returning the rejection.
/// This is an application use case; it does not invent source metadata or retry held events.
/// </summary>
public sealed class SourcedMatchEventIngestionService(
    IMatchEventStore eventStore,
    IQuarantineEvidenceWriter quarantineWriter,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task AppendAsync(
        MatchEvent matchEvent,
        IngestionSourceReference source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(matchEvent);
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(source);

        try
        {
            await eventStore.AppendAsync(matchEvent, cancellationToken);
        }
        catch (MatchEventIngestionException failure)
        {
            var evidence = new QuarantineEvidence(
                Guid.NewGuid(),
                failure.MatchId.Value == Guid.Empty ? null : failure.MatchId.Value,
                source.SourcePointer,
                source.PayloadHash,
                failure.Code,
                _timeProvider.GetUtcNow());

            // Do not acknowledge a rejected event if durable evidence could not be recorded.
            // The persistence exception remains visible and the original classified failure is
            // retained as its inner exception.
            try
            {
                await quarantineWriter.AppendAsync(evidence, cancellationToken);
            }
            catch (Exception captureFailure) when (captureFailure is not OperationCanceledException)
            {
                throw new QuarantineEvidenceWriteException(failure, captureFailure);
            }

            throw;
        }
    }

    private static void ValidateSource(IngestionSourceReference source)
    {
        if (string.IsNullOrWhiteSpace(source.SourcePointer) || source.SourcePointer.Length > 2048)
            throw new ArgumentException("A stable source pointer of 1 to 2048 characters is required.", nameof(source));
        if (source.PayloadHash is not null &&
            (source.PayloadHash.Length != 64 || source.PayloadHash.Any(character => !Uri.IsHexDigit(character))))
            throw new ArgumentException("Source payload hash must be a 64-character SHA-256 digest or null.", nameof(source));
    }
}

/// <summary>Indicates that rejection evidence could not be made durable.</summary>
public sealed class QuarantineEvidenceWriteException : InvalidOperationException
{
    public QuarantineEvidenceWriteException(MatchEventIngestionException ingestionFailure, Exception persistenceFailure)
        : base($"Ingestion failed with {ingestionFailure.Code}, and quarantine evidence could not be persisted.", persistenceFailure)
    {
        IngestionFailure = ingestionFailure;
    }

    public MatchEventIngestionException IngestionFailure { get; }
}
