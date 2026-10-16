using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Application.Matches;

public sealed record CanonicalMatchStateReplayResult(
    SelectedMatchProjection Projection,
    int SelectedSeconds,
    MatchState ReplayedState,
    string ReplayInputSha256,
    string ReplayedStateSha256);

/// <summary>Replays only canonical state from the immutable event prefix; it does not run or publish detectors.</summary>
public sealed class ReplayCanonicalMatchStateService(IMatchProjectionReader reader)
{
    private const int MaximumMatchSeconds = 5400;
    private const int MaximumReplayEvents = 100_000;
    private const long MaximumReplayInputBytes = 32L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CanonicalMatchStateReplayResult> ExecuteAsync(
        SelectedMatchProjection projection,
        int selectedSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > MaximumMatchSeconds)
            throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (projection.CanonicalRevision <= 0 || projection.ProjectionRevision <= 0 ||
            projection.AsOfSequence < -1 || projection.AsOfClockSeconds < 0 ||
            projection.AsOfClockSeconds > selectedSeconds || string.IsNullOrWhiteSpace(projection.AlgorithmVersion) ||
            !IsSha256(projection.SourceSha256) || !IsSha256(projection.StateSha256))
            throw new InvalidDataException("Selected canonical projection metadata is invalid for replay.");

        if (!Guid.TryParse(projection.State.MatchId, out var parsedMatchId) || parsedMatchId == Guid.Empty ||
            projection.State.LastSequence != projection.AsOfSequence ||
            projection.State.ClockSeconds != projection.AsOfClockSeconds)
            throw new InvalidDataException("Selected canonical projection state does not match its replay metadata.");

        var matchId = new MatchId(parsedMatchId);
        var seed = await reader.ReadReplaySeedAsync(matchId, cancellationToken)
            ?? throw new InvalidDataException("Match has no immutable replay seed; canonical state cannot be replayed.");
        var aggregate = MatchAggregate.FromReplaySeed(matchId, seed);

        using var replayInput = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHashPart(replayInput, "canonical-match-state-replay-v1");
        AppendHashPart(replayInput, JsonSerializer.Serialize(new ReplayInputHeader(
            matchId.ToString(), selectedSeconds, projection.CanonicalRevision, projection.ProjectionRevision,
            projection.AsOfSequence, projection.AlgorithmVersion, projection.SourceSha256), JsonOptions));
        AppendHashPart(replayInput, JsonSerializer.Serialize(seed, JsonOptions));

        long count = 0;
        long totalBytes = 0;
        var previousClock = 0;
        await foreach (var item in reader.ReadReplayEventsAtAsync(projection, selectedSeconds, cancellationToken)
                           .WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (count >= MaximumReplayEvents)
                throw new InvalidDataException("Selected replay exceeds the event count limit.");

            var serialized = JsonSerializer.SerializeToUtf8Bytes(item, JsonOptions);
            if (serialized.Length > MaximumReplayInputBytes || totalBytes > MaximumReplayInputBytes - serialized.Length)
                throw new InvalidDataException("Selected replay exceeds the input byte limit.");

            if (item.Event.MatchId != matchId || item.Event.Sequence != count ||
                item.Event.ClockSeconds < previousClock || item.Event.ClockSeconds > selectedSeconds)
                throw new InvalidDataException("Replay source event is outside the contiguous selected-time prefix.");

            aggregate.Apply(item);
            AppendHashPart(replayInput, serialized);
            totalBytes += serialized.Length;
            previousClock = item.Event.ClockSeconds;
            count++;
        }

        if (count != projection.AsOfSequence + 1)
            throw new InvalidDataException("Replay source prefix does not reach the selected projection sequence.");

        var replayedState = aggregate.Snapshot();
        var replayedStateSha256 = Hash(JsonSerializer.SerializeToUtf8Bytes(replayedState, JsonOptions));
        if (replayedState.MatchId != projection.State.MatchId || replayedState.LastSequence != projection.AsOfSequence ||
            replayedState.ClockSeconds != projection.AsOfClockSeconds || replayedStateSha256 != projection.StateSha256)
            throw new InvalidDataException("Deterministic replay state does not match the selected persisted projection digest.");

        return new(projection, selectedSeconds, replayedState,
            Convert.ToHexStringLower(replayInput.GetHashAndReset()), replayedStateSha256);
    }

    private static void AppendHashPart(IncrementalHash hash, string value) =>
        AppendHashPart(hash, Encoding.UTF8.GetBytes(value));

    private static void AppendHashPart(IncrementalHash hash, byte[] bytes)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool IsSha256(string value)
    {
        if (value.Length != 64) return false;
        return value.All(character => char.IsAsciiHexDigit(character) && !char.IsAsciiLetterUpper(character));
    }

    private sealed record ReplayInputHeader(string MatchId, int SelectedSeconds, long CanonicalRevision,
        int ProjectionRevision, long ThroughSequence, string AlgorithmVersion, string SourceSha256);
}
