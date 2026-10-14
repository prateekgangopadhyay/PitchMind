using PitchMind.Domain.Simulation;

namespace PitchMind.TestFixtures;

/// <summary>Test-only event kinds; EP03-S02 owns the future canonical telemetry mapping.</summary>
public enum SyntheticScenarioEventKind
{
    KickOff, HalfTime, Pass, Shot, Goal, Foul, Turnover, Pressure, FullTime,
    YellowCard, RedCard, Substitution
}

/// <summary>
/// Unified scenario timeline. SourceSequence preserves the underlying fixture's original sequence;
/// Sequence orders the expanded, test-only scenario including disciplinary/substitution events.
/// </summary>
public sealed record SyntheticScenarioEvent(
    Guid EventId,
    long Sequence,
    long? SourceSequence,
    int ClockSeconds,
    int Period,
    SyntheticScenarioEventKind Type,
    string TeamId,
    string PlayerId,
    string? RelatedPlayerId = null,
    string? Outcome = null);

/// <summary>Authored possession annotation; it is not a claim derived from the candidate feed.</summary>
public sealed record SyntheticPossessionPhase(
    int StartClockSeconds,
    int EndClockSecondsExclusive,
    int Period,
    string TeamId,
    string Phase);

public enum SyntheticPressCounterexampleKind { MissingTracking, CompletedOpponentPass, WrongTeamRegain, DistantPressArrival }

/// <summary>Named near-miss fixture variants that must not produce the positive press golden.</summary>
public sealed record SyntheticPressCounterexample(
    SyntheticPressCounterexampleKind Kind,
    string Name,
    SyntheticFixture Fixture);

/// <summary>
/// A deterministic test scenario with authored line-up changes and phases. Keep this in the
/// TestFixtures assembly until EP03-S02 defines the versioned canonical event and roster contracts.
/// </summary>
public sealed record GovernedSyntheticScenario(
    SyntheticFixture Fixture,
    IReadOnlyList<SyntheticPlayer> Squad,
    IReadOnlyList<SyntheticScenarioEvent> Events,
    IReadOnlyList<SyntheticPossessionPhase> PossessionPhases,
    IReadOnlyList<TrackingFrame> Frames,
    string ContentSha256);
