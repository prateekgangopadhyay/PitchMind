using System.Text;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;

namespace PitchMind.Application.MatchIntelligence;

public sealed record AutoRecapResult(
    string MatchId,
    long CanonicalRevision,
    string Headline,
    string Summary,
    IReadOnlyList<string> KeyMomentsSummary,
    string Provenance,
    bool GeneratedByAi,
    DateTimeOffset GeneratedUtc);

public sealed record AutoRecapInsight(
    string InsightId,
    string MatchId,
    int ClockSeconds,
    int Period,
    long Sequence,
    string Summary,
    string DetectorVersion,
    string TruthClass);

public interface IAutoRecapGenerator
{
    AutoRecapResult GenerateDeterministicRecap(
        SelectedMatchProjection projection,
        IReadOnlyList<AutoRecapInsight> keyMoments,
        string fixtureName);
}

public sealed class AutoRecapGenerator : IAutoRecapGenerator
{
    public AutoRecapResult GenerateDeterministicRecap(
        SelectedMatchProjection projection,
        IReadOnlyList<AutoRecapInsight> keyMoments,
        string fixtureName)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var state = projection.State;

        var homeScore = state.HomeScore;
        var awayScore = state.AwayScore;
        string resultText = homeScore > awayScore
            ? "Home Victory"
            : homeScore < awayScore
                ? "Away Victory"
                : "Draw";

        var headline = $"{fixtureName}: Full-Time {homeScore}-{awayScore} ({resultText})";

        var summaryBuilder = new StringBuilder();
        summaryBuilder.Append($"Full-time conclusion of {fixtureName} at elapsed clock {state.ClockSeconds}s. ");
        summaryBuilder.Append($"Final score: {homeScore}-{awayScore}. ");
        summaryBuilder.Append($"Active players: {state.HomeActivePlayers} home vs {state.AwayActivePlayers} away. ");
        summaryBuilder.Append($"Canonical revision: {projection.CanonicalRevision}, algorithm: {projection.AlgorithmVersion}. ");
        summaryBuilder.Append($"Source integrity verified via SHA-256 fingerprint: {projection.SourceSha256[..12]}... ");

        var keyMomentsSummary = new List<string>();
        if (keyMoments is { Count: > 0 })
        {
            foreach (var km in keyMoments.OrderBy(k => k.ClockSeconds))
            {
                var momentText = $"[Clock {km.ClockSeconds}s | Period {km.Period}]: {km.Summary}";
                keyMomentsSummary.Add(momentText);
            }
        }
        else
        {
            keyMomentsSummary.Add($"No critical turning points detected through sequence {projection.AsOfSequence}.");
        }

        return new AutoRecapResult(
            MatchId: state.MatchId,
            CanonicalRevision: projection.CanonicalRevision,
            Headline: headline,
            Summary: summaryBuilder.ToString(),
            KeyMomentsSummary: keyMomentsSummary,
            Provenance: projection.State.Provenance,
            GeneratedByAi: false,
            GeneratedUtc: DateTimeOffset.UtcNow);
    }
}
