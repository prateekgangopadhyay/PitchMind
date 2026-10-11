using System.Runtime.CompilerServices;

namespace PitchMind.Infrastructure.Simulation.Candidates;

/// <summary>
/// Read-only source data from an explicitly noncanonical candidate fixture.
/// This type is not a MatchEvent and cannot be written to the canonical ledger.
/// </summary>
public sealed class CandidateSourceFeed
{
    internal CandidateSourceFeed(
        string fixtureId,
        string provenance,
        string redistributionPermission,
        string sourceSha256,
        long sourceByteSize,
        IReadOnlyList<CandidateSourceEnvelope> events)
    {
        FixtureId = fixtureId;
        Provenance = provenance;
        RedistributionPermission = redistributionPermission;
        SourceSha256 = sourceSha256;
        SourceByteSize = sourceByteSize;
        Events = events;
    }

    public string Disposition => "candidate_not_canonical";
    public bool IsCanonical => false;
    public string FixtureId { get; }
    public string Provenance { get; }
    public string RedistributionPermission { get; }
    public bool CoordinateSemanticsVerified => false;
    public bool PlayerMetricsValidated => false;
    public bool CanonicalMappingsApproved => false;
    public string SourceSha256 { get; }
    public long SourceByteSize { get; }
    public IReadOnlyList<CandidateSourceEnvelope> Events { get; }

    /// <summary>Replays the unchanged candidate envelopes in source order, beginning inclusively at a source sequence.</summary>
    public async IAsyncEnumerable<CandidateSourceEnvelope> ReplayFromSourceSequenceAsync(
        long sourceSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var envelope in Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (envelope.SourceSequence < sourceSequence)
                continue;

            yield return envelope;
            await Task.Yield();
        }
    }
}

/// <summary>
/// An unmodified JSON object from a candidate source. RawJson retains the event object's source text;
/// raw clock, period, and sequence tokens are exposed without conversion or canonical interpretation.
/// </summary>
public sealed record CandidateSourceEnvelope(
    string EventId,
    string MatchId,
    long SourceSequence,
    string RawSequence,
    string RawClockSeconds,
    string RawPeriod,
    string EventType,
    string RawJson);
