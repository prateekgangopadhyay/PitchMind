using System.Runtime.CompilerServices;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Application.Matches;

/// <summary>
/// Validates and projects one canonical event, persists its projection/outbox intent,
/// then advances the caller-owned in-memory aggregate only after the store succeeds.
/// </summary>
public sealed class CanonicalMatchEventWriter(ICanonicalUpdateStore store)
{
    private static readonly ConditionalWeakTable<MatchAggregate, SemaphoreSlim> AggregateGates = new();

    public async Task<CanonicalUpdateCommitResult> AppendAsync(
        MatchAggregate aggregate,
        SimulationEvent item,
        int projectionRevision,
        string algorithmVersion,
        string sourceSha256,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(store);
        if (projectionRevision <= 0) throw new ArgumentOutOfRangeException(nameof(projectionRevision));
        if (string.IsNullOrWhiteSpace(algorithmVersion)) throw new ArgumentException("Algorithm version is required.", nameof(algorithmVersion));
        if (string.IsNullOrWhiteSpace(sourceSha256)) throw new ArgumentException("Source hash is required.", nameof(sourceSha256));

        MatchEventValidator.Validate(item.Event);
        var gate = AggregateGates.GetValue(aggregate, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var projectedAggregate = aggregate.Preview(item);
            var update = CanonicalUpdate.FromAppliedAggregate(projectedAggregate, item.Event,
                projectionRevision, algorithmVersion, sourceSha256);

            var result = await store.CommitAsync(update, cancellationToken);
            // A retry after a lost acknowledgement can find the same event already committed.
            // Apply is idempotent for the same event ID/payload and advances a lagging local copy.
            aggregate.Apply(item);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }
}
