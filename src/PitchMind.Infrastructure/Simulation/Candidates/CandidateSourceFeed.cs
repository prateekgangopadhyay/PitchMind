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

    /// <summary>
    /// Replays unchanged candidate envelopes in source order, beginning inclusively at a source sequence,
    /// with a caller-selected upper bound on the event rate.
    /// </summary>
    public async IAsyncEnumerable<CandidateSourceEnvelope> ReplayFromSourceSequenceAsync(
        long sourceSequence,
        CandidateReplayOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var delayBetweenEvents = TimeSpan.FromSeconds(1d / options.EventsPerSecond);
        var emittedAny = false;

        foreach (var envelope in Events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (envelope.SourceSequence < sourceSequence)
                continue;

            if (emittedAny)
                await Task.Delay(delayBetweenEvents, options.Clock, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            yield return envelope;
            emittedAny = true;
        }
    }
}

/// <summary>Validated rate ceiling for local replay of candidate source records.</summary>
public sealed record CandidateReplayOptions
{
    public const double MinimumEventsPerSecond = 0.01;
    public const double MaximumEventsPerSecond = 1_000;

    public CandidateReplayOptions(double eventsPerSecond) : this(eventsPerSecond, TimeProvider.System) { }

    internal CandidateReplayOptions(double eventsPerSecond, TimeProvider clock)
    {
        if (!double.IsFinite(eventsPerSecond) || eventsPerSecond < MinimumEventsPerSecond || eventsPerSecond > MaximumEventsPerSecond)
        {
            throw new ArgumentOutOfRangeException(
                nameof(eventsPerSecond),
                $"Replay rate must be between {MinimumEventsPerSecond} and {MaximumEventsPerSecond} events per second.");
        }

        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        EventsPerSecond = eventsPerSecond;
    }

    public double EventsPerSecond { get; }
    internal TimeProvider Clock { get; }
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
