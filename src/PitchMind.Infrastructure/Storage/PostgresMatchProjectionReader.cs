using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using ReplaySnapshot = PitchMind.Application.Abstractions.MatchReplaySnapshot;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Point-in-time reads from immutable PostgreSQL projection revisions and the canonical event ledger.</summary>
public sealed class PostgresMatchProjectionReader(NpgsqlDataSource dataSource) : IMatchProjectionReader
{
    private const int MaximumMatchSeconds = 5400;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>One SQL statement gives the replay a single PostgreSQL MVCC snapshot across all source inputs.</summary>
    public async Task<ReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        if (selectedSeconds is < 0 or > MaximumMatchSeconds) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (upToCanonicalRevision <= 0) throw new ArgumentOutOfRangeException(nameof(upToCanonicalRevision));
        if (string.IsNullOrWhiteSpace(algorithmVersion)) throw new ArgumentException("Algorithm version is required.", nameof(algorithmVersion));

        await using var command = dataSource.CreateCommand("""
            WITH selected AS (
                SELECT p.match_id, p.canonical_revision, p.revision, p.as_of_sequence, p.as_of_clock_seconds,
                       p.algorithm_version, p.source_sha256, p.state_sha256, p.state_json
                FROM pitchmind.match_projections p
                JOIN pitchmind.matches m ON m.match_id = p.match_id AND m.source_sha256 = p.source_sha256
                WHERE p.match_id = $1 AND p.as_of_clock_seconds <= $2
                  AND p.canonical_revision <= $3 AND p.algorithm_version = $4
                ORDER BY p.as_of_clock_seconds DESC, p.as_of_sequence DESC, p.canonical_revision DESC, p.revision DESC
                LIMIT 1
            )
            SELECT p.canonical_revision, p.revision, p.as_of_sequence, p.as_of_clock_seconds,
                   p.algorithm_version, p.source_sha256, p.state_sha256, p.state_json::text,
                   m.replay_seed_json::text, m.replay_context_json::text, m.fixture_version, m.provenance,
                   m.pitch_length_metres, m.pitch_width_metres, m.competition_id, m.competition_rules_version,
                   m.max_substitute_reentries_per_player,
                   COALESCE((SELECT jsonb_agg(jsonb_build_object('payload', e.payload, 'sequence', e.sequence,
                       'clockSeconds', e.clock_seconds, 'period', e.period) ORDER BY e.sequence)::text
                       FROM pitchmind.match_events e WHERE e.match_id = p.match_id AND e.canonical_revision <= p.canonical_revision
                         AND e.sequence <= p.as_of_sequence AND e.clock_seconds <= $2), '[]'),
                   COALESCE((SELECT jsonb_agg(jsonb_build_object('source', e.source_event_json, 'payload', e.payload,
                       'sequence', e.sequence, 'clockSeconds', e.clock_seconds, 'period', e.period) ORDER BY e.sequence)::text
                       FROM pitchmind.match_events e WHERE e.match_id = p.match_id AND e.canonical_revision <= p.canonical_revision
                         AND e.sequence <= p.as_of_sequence AND e.clock_seconds <= $2), '[]'),
                   COALESCE((SELECT jsonb_agg(jsonb_build_object('frame', f.payload, 'frameId', f.frame_id, 'sequence', f.sequence, 'period', f.period,
                       'clockSeconds', f.clock_seconds, 'coordinateVersion', f.coordinate_version, 'sourceSha256', f.source_sha256)
                       ORDER BY f.clock_seconds, f.sequence, f.frame_id)::text
                       FROM pitchmind.tracking_frames f WHERE f.match_id = p.match_id
                         AND f.canonical_revision <= p.canonical_revision AND f.clock_seconds <= $2), '[]')
            FROM selected p JOIN pitchmind.matches m ON m.match_id = p.match_id
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(upToCanonicalRevision);
        command.Parameters.AddWithValue(algorithmVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var state = JsonSerializer.Deserialize<MatchState>(reader.GetString(7), JsonOptions)
            ?? throw new InvalidDataException("Persisted match projection is empty.");
        var sourceHash = reader.GetString(5).Trim();
        var stateHash = reader.GetString(6).Trim();
        var projection = new SelectedMatchProjection(state, reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2),
            reader.GetInt32(3), reader.GetString(4), sourceHash, stateHash);
        ValidateProjection(matchId, projection);

        var seed = reader.IsDBNull(8) ? null : JsonSerializer.Deserialize<MatchReplaySeed>(reader.GetString(8), JsonOptions)
            ?? throw new InvalidDataException("Persisted replay seed is empty.");
        var context = reader.IsDBNull(9) ? null : JsonSerializer.Deserialize<MatchReplayContext>(reader.GetString(9), JsonOptions)
            ?? throw new InvalidDataException("Persisted replay context is empty.");
        var sourceIdentity = new MatchReplaySourceIdentity(reader.GetString(10), reader.GetString(11),
            (double)reader.GetDecimal(12), (double)reader.GetDecimal(13),
            new MatchCompetitionRules(reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15), reader.IsDBNull(16) ? null : reader.GetInt32(16)));
        var indexedEvents = JsonSerializer.Deserialize<List<IndexedEvent>>(reader.GetString(17), JsonOptions) ?? [];
        var indexedReplayEvents = JsonSerializer.Deserialize<List<IndexedReplayEvent>>(reader.GetString(18), JsonOptions) ?? [];
        var indexedFrames = JsonSerializer.Deserialize<List<IndexedFrame>>(reader.GetString(19), JsonOptions) ?? [];

        var events = indexedEvents.Select(item =>
        {
            var matchEvent = JsonSerializer.Deserialize<MatchEvent>(item.Payload.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("Canonical ledger event is empty.");
            MatchEventValidator.Validate(matchEvent);
            if (matchEvent.MatchId != matchId || matchEvent.Sequence != item.Sequence ||
                matchEvent.ClockSeconds != item.ClockSeconds || matchEvent.Period != item.Period)
                throw new InvalidDataException("Canonical event payload does not match its indexed metadata.");
            return matchEvent;
        }).ToArray();
        var replayEvents = indexedReplayEvents.Select(item =>
        {
            if (item.Source.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                throw new InvalidDataException("Canonical event is missing its immutable replay source envelope.");
            var replayEvent = JsonSerializer.Deserialize<SimulationEvent>(item.Source.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("Canonical replay source event is empty.");
            var matchEvent = JsonSerializer.Deserialize<MatchEvent>(item.Payload.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("Canonical event payload is empty.");
            MatchEventValidator.Validate(replayEvent.Event);
            if (replayEvent.Event != matchEvent || replayEvent.Event.MatchId != matchId ||
                replayEvent.Event.Sequence != item.Sequence || replayEvent.Event.ClockSeconds != item.ClockSeconds ||
                replayEvent.Event.Period != item.Period)
                throw new InvalidDataException("Canonical replay envelope does not match its indexed event.");
            return replayEvent;
        }).ToArray();
        var frames = indexedFrames.Select(item =>
        {
            var frame = JsonSerializer.Deserialize<TrackingFrame>(item.Frame.GetRawText(), JsonOptions)
                ?? throw new InvalidDataException("Persisted tracking frame is empty.");
            if (frame.ClockSeconds != item.ClockSeconds || frame.Id != item.FrameId || item.SourceSha256.Trim() != sourceHash)
                throw new InvalidDataException("Persisted tracking frame metadata is inconsistent with the selected source.");
            return new MatchReplayFrame(frame, item.Sequence, item.Period, item.CoordinateVersion, item.SourceSha256.Trim());
        }).ToArray();
        return new(projection, Array.AsReadOnly(events), seed, context, sourceIdentity, Array.AsReadOnly(replayEvents), Array.AsReadOnly(frames));
    }

    private static void ValidateProjection(MatchId matchId, SelectedMatchProjection projection)
    {
        var canonicalJson = JsonSerializer.Serialize(projection.State, JsonOptions);
        var computedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
        if (computedHash != projection.StateSha256 || projection.State.MatchId != matchId.ToString() ||
            projection.State.LastSequence != projection.AsOfSequence || projection.State.ClockSeconds != projection.AsOfClockSeconds)
            throw new InvalidDataException("Persisted projection metadata does not match its state.");
    }

    private sealed record IndexedEvent(JsonElement Payload, long Sequence, int ClockSeconds, int Period);
    private sealed record IndexedReplayEvent(JsonElement Source, JsonElement Payload, long Sequence, int ClockSeconds, int Period);
    private sealed record IndexedFrame(JsonElement Frame, string FrameId, long Sequence, int Period, int ClockSeconds,
        string CoordinateVersion, string SourceSha256);

    public async Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
        long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        if (selectedSeconds is < 0 or > MaximumMatchSeconds) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (upToCanonicalRevision <= 0) throw new ArgumentOutOfRangeException(nameof(upToCanonicalRevision));
        if (string.IsNullOrWhiteSpace(algorithmVersion)) throw new ArgumentException("Algorithm version is required.", nameof(algorithmVersion));

        await using var command = dataSource.CreateCommand("""
            SELECT canonical_revision, revision, as_of_sequence, as_of_clock_seconds, algorithm_version, source_sha256,
                   state_sha256, state_json::text
            FROM pitchmind.match_projections
            WHERE match_id = $1 AND as_of_clock_seconds <= $2
              AND canonical_revision <= $3 AND algorithm_version = $4
            ORDER BY as_of_clock_seconds DESC, as_of_sequence DESC, canonical_revision DESC, revision DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(upToCanonicalRevision);
        command.Parameters.AddWithValue(algorithmVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var canonicalRevision = reader.GetInt64(0);
        var projectionRevision = reader.GetInt32(1);
        var asOfSequence = reader.GetInt64(2);
        var asOfClock = reader.GetInt32(3);
        var selectedAlgorithmVersion = reader.GetString(4);
        var sourceHash = reader.GetString(5).Trim();
        var storedStateHash = reader.GetString(6).Trim();
        var stateJson = reader.GetString(7);
        var state = JsonSerializer.Deserialize<MatchState>(stateJson, JsonOptions)
            ?? throw new InvalidDataException("Persisted match projection is empty.");
        var canonicalStateJson = JsonSerializer.Serialize(state, JsonOptions);
        var computedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalStateJson)));
        if (computedHash != storedStateHash)
            throw new InvalidDataException("Persisted match projection hash does not match its state.");
        if (state.MatchId != matchId.ToString() || state.LastSequence != asOfSequence || state.ClockSeconds != asOfClock)
            throw new InvalidDataException("Persisted projection metadata does not match its state.");

        return new SelectedMatchProjection(state, canonicalRevision, projectionRevision, asOfSequence, asOfClock,
            selectedAlgorithmVersion, sourceHash, storedStateHash);
    }

    public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > MaximumMatchSeconds) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (projection.AsOfClockSeconds > selectedSeconds)
            throw new ArgumentOutOfRangeException(nameof(projection), "Projection is later than the selected time.");

        if (!Guid.TryParse(projection.State.MatchId, out var parsedMatchId) || parsedMatchId == Guid.Empty)
            throw new InvalidDataException("Persisted projection has an invalid match ID.");
        var matchId = new MatchId(parsedMatchId);
        var persistedSequence = await VerifyPersistedProjectionAsync(matchId, projection, cancellationToken);
        await using var command = dataSource.CreateCommand("""
            SELECT payload::text, sequence, clock_seconds, period
            FROM pitchmind.match_events
            WHERE match_id = $1 AND canonical_revision <= $2 AND sequence <= $3 AND clock_seconds <= $4
            ORDER BY sequence
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(projection.CanonicalRevision);
        command.Parameters.AddWithValue(persistedSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long expectedSequence = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var matchEvent = JsonSerializer.Deserialize<MatchEvent>(reader.GetString(0))
                ?? throw new InvalidDataException("Canonical ledger event is empty.");
            MatchEventValidator.Validate(matchEvent);
            if (matchEvent.MatchId != matchId || matchEvent.Sequence != expectedSequence || reader.GetInt64(1) != expectedSequence ||
                matchEvent.ClockSeconds != reader.GetInt32(2) || matchEvent.Period != reader.GetInt32(3) || matchEvent.ClockSeconds > selectedSeconds)
                throw new InvalidDataException("Canonical event sequence is not contiguous at the selected time.");
            expectedSequence++;
            yield return matchEvent;
        }
        if (expectedSequence != persistedSequence + 1)
            throw new InvalidDataException("Selected-time event window does not reach the projection sequence.");
    }

    public async Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT replay_seed_json::text FROM pitchmind.matches WHERE match_id = $1");
        command.Parameters.AddWithValue(matchId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull) return null;
        var seed = JsonSerializer.Deserialize<MatchReplaySeed>((string)value, JsonOptions)
            ?? throw new InvalidDataException("Persisted replay seed is empty.");
        if (seed.SchemaVersion is not ("match-replay-seed-v1" or "match-replay-seed-v2") || string.IsNullOrWhiteSpace(seed.Provenance))
            throw new InvalidDataException("Persisted replay seed version or provenance is invalid.");
        return seed;
    }

    public async Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT replay_context_json::text FROM pitchmind.matches WHERE match_id = $1");
        command.Parameters.AddWithValue(matchId.Value);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull) return null;
        var context = JsonSerializer.Deserialize<MatchReplayContext>((string)value, JsonOptions)
            ?? throw new InvalidDataException("Persisted replay context is empty.");
        if (context.SchemaVersion != "match-replay-context-v1")
            throw new InvalidDataException("Persisted replay context version is unsupported.");
        return context;
    }

    public async IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > MaximumMatchSeconds || projection.AsOfClockSeconds > selectedSeconds)
            throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (!Guid.TryParse(projection.State.MatchId, out var parsedMatchId) || parsedMatchId == Guid.Empty)
            throw new InvalidDataException("Persisted projection has an invalid match ID.");
        var matchId = new MatchId(parsedMatchId);
        var persistedSequence = await VerifyPersistedProjectionAsync(matchId, projection, cancellationToken);
        await using var command = dataSource.CreateCommand("""
            SELECT source_event_json::text, payload::text, sequence, clock_seconds, period
            FROM pitchmind.match_events
            WHERE match_id = $1 AND canonical_revision <= $2 AND sequence <= $3 AND clock_seconds <= $4
            ORDER BY sequence
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(projection.CanonicalRevision);
        command.Parameters.AddWithValue(persistedSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long expectedSequence = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(0)) throw new InvalidDataException("Canonical event is missing its immutable replay source envelope.");
            var item = JsonSerializer.Deserialize<SimulationEvent>(reader.GetString(0), JsonOptions)
                ?? throw new InvalidDataException("Canonical replay source event is empty.");
            var indexedEvent = JsonSerializer.Deserialize<MatchEvent>(reader.GetString(1))
                ?? throw new InvalidDataException("Canonical event payload is empty.");
            MatchEventValidator.Validate(item.Event);
            if (item.Event != indexedEvent || item.Event.MatchId != matchId || item.Event.Sequence != expectedSequence ||
                reader.GetInt64(2) != expectedSequence || item.Event.ClockSeconds != reader.GetInt32(3) ||
                item.Event.Period != reader.GetInt32(4) || item.Event.ClockSeconds > selectedSeconds)
                throw new InvalidDataException("Canonical replay envelope does not match its immutable indexed event.");
            expectedSequence++;
            yield return item;
        }
        if (expectedSequence != persistedSequence + 1)
            throw new InvalidDataException("Replay event prefix does not reach the selected projection sequence.");
    }

    public async IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > MaximumMatchSeconds || projection.AsOfClockSeconds > selectedSeconds)
            throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (!Guid.TryParse(projection.State.MatchId, out var parsedMatchId) || parsedMatchId == Guid.Empty)
            throw new InvalidDataException("Persisted projection has an invalid match ID.");
        var matchId = new MatchId(parsedMatchId);
        _ = await VerifyPersistedProjectionAsync(matchId, projection, cancellationToken);
        await using var command = dataSource.CreateCommand("""
            SELECT frame_id, sequence, period, clock_seconds, coordinate_version, source_sha256, payload::text
            FROM pitchmind.tracking_frames
            WHERE match_id = $1 AND canonical_revision <= $2 AND clock_seconds <= $3
            ORDER BY clock_seconds, sequence, frame_id
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(projection.CanonicalRevision);
        command.Parameters.AddWithValue(selectedSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var frameIds = new HashSet<string>(StringComparer.Ordinal);
        long previousSequence = -1;
        int previousClock = -1;
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var sequence = reader.GetInt64(1);
            var period = reader.GetInt32(2);
            var clock = reader.GetInt32(3);
            var coordinateVersion = reader.GetString(4);
            var sourceSha256 = reader.GetString(5).Trim();
            var frame = JsonSerializer.Deserialize<TrackingFrame>(reader.GetString(6), JsonOptions)
                ?? throw new InvalidDataException("Persisted tracking frame is empty.");
            if (!frameIds.Add(id) || frame.Id != id || sequence <= previousSequence || clock < previousClock ||
                frame.ClockSeconds != clock || clock > selectedSeconds || period is < 1 or > 2 ||
                string.IsNullOrWhiteSpace(coordinateVersion) || sourceSha256 != projection.SourceSha256)
                throw new InvalidDataException("Persisted tracking frame metadata is inconsistent with the selected source prefix.");
            previousSequence = sequence;
            previousClock = clock;
            yield return new MatchReplayFrame(frame, sequence, period, coordinateVersion, sourceSha256);
        }
    }

    private async Task<long> VerifyPersistedProjectionAsync(MatchId matchId, SelectedMatchProjection projection,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT as_of_sequence, as_of_clock_seconds, algorithm_version, source_sha256, state_sha256,
                   state_json::text
            FROM pitchmind.match_projections
            WHERE match_id = $1 AND canonical_revision = $2 AND revision = $3
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(projection.CanonicalRevision);
        command.Parameters.AddWithValue(projection.ProjectionRevision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidDataException("Projection revision is not present in the canonical store.");

        var sequence = reader.GetInt64(0);
        var clock = reader.GetInt32(1);
        var algorithmVersion = reader.GetString(2);
        var sourceHash = reader.GetString(3).Trim();
        var stateHash = reader.GetString(4).Trim();
        var stateJson = reader.GetString(5);
        var state = JsonSerializer.Deserialize<MatchState>(stateJson, JsonOptions)
            ?? throw new InvalidDataException("Persisted match projection is empty.");
        var canonicalStateJson = JsonSerializer.Serialize(state, JsonOptions);
        var computedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalStateJson)));

        if (computedHash != stateHash || state.MatchId != matchId.ToString() || state.LastSequence != sequence ||
            state.ClockSeconds != clock)
            throw new InvalidDataException("Persisted projection metadata does not match its state.");

        var suppliedStateJson = JsonSerializer.Serialize(projection.State, JsonOptions);
        var suppliedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(suppliedStateJson)));
        if (projection.AsOfSequence != sequence || projection.AsOfClockSeconds != clock ||
            projection.AlgorithmVersion != algorithmVersion || projection.SourceSha256.Trim() != sourceHash ||
            projection.StateSha256.Trim() != stateHash || suppliedHash != stateHash)
            throw new InvalidDataException("Supplied projection does not match its persisted revision.");

        return sequence;
    }
}
