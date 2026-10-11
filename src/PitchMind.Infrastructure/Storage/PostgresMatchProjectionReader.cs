using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Point-in-time reads from immutable PostgreSQL projection revisions and the canonical event ledger.</summary>
public sealed class PostgresMatchProjectionReader(NpgsqlDataSource dataSource) : IMatchProjectionReader
{
    private const int MaximumMatchSeconds = 5400;
    private const int MaximumPageSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaximumPageSize + 1) throw new ArgumentOutOfRangeException(nameof(limit));
        if (afterMatchId is { Value: var cursor } && cursor == Guid.Empty)
            throw new ArgumentException("A match cursor cannot be empty.", nameof(afterMatchId));

        await using var command = dataSource.CreateCommand("""
            WITH latest AS (
                SELECT DISTINCT ON (match_id) match_id, revision, as_of_sequence, as_of_clock_seconds,
                       algorithm_version, source_sha256, state_sha256, state_json::text AS state_json
                FROM pitchmind.match_projections
                ORDER BY match_id, revision DESC, as_of_clock_seconds DESC, as_of_sequence DESC
            )
            SELECT match_id, revision, as_of_sequence, as_of_clock_seconds, algorithm_version,
                   source_sha256, state_sha256, state_json
            FROM latest
            WHERE ($1::uuid IS NULL OR match_id > $1)
            ORDER BY match_id
            LIMIT $2
            """);
        command.Parameters.AddWithValue((object?)afterMatchId?.Value ?? DBNull.Value);
        command.Parameters.AddWithValue(limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<PersistedMatchProjection>(limit);
        while (await reader.ReadAsync(cancellationToken))
        {
            var matchId = new MatchId(reader.GetGuid(0));
            var projection = ReadProjection(reader, 1);
            if (projection.State.MatchId != matchId.ToString())
                throw new InvalidDataException("Persisted projection metadata does not match its match ID.");
            results.Add(projection);
        }
        return results;
    }

    public async Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
        int? maximumRevision, CancellationToken cancellationToken)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        if (selectedSeconds is < 0 or > MaximumMatchSeconds) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (maximumRevision is <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRevision));

        await using var command = dataSource.CreateCommand("""
            SELECT revision, as_of_sequence, as_of_clock_seconds, algorithm_version, source_sha256,
                   state_sha256, state_json::text
            FROM pitchmind.match_projections
            WHERE match_id = $1 AND as_of_clock_seconds <= $2
              AND ($3::integer IS NULL OR revision <= $3)
            ORDER BY as_of_clock_seconds DESC, as_of_sequence DESC, revision DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue((object?)maximumRevision ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        var revision = reader.GetInt32(0);
        var asOfSequence = reader.GetInt64(1);
        var asOfClock = reader.GetInt32(2);
        var algorithmVersion = reader.GetString(3);
        var sourceHash = reader.GetString(4).Trim();
        var storedStateHash = reader.GetString(5).Trim();
        var stateJson = reader.GetString(6);
        var state = JsonSerializer.Deserialize<MatchState>(stateJson, JsonOptions)
            ?? throw new InvalidDataException("Persisted match projection is empty.");
        var canonicalStateJson = JsonSerializer.Serialize(state, JsonOptions);
        var computedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalStateJson)));
        if (computedHash != storedStateHash)
            throw new InvalidDataException("Persisted match projection hash does not match its state.");
        if (state.MatchId != matchId.ToString() || state.LastSequence != asOfSequence || state.ClockSeconds != asOfClock)
            throw new InvalidDataException("Persisted projection metadata does not match its state.");

        return new SelectedMatchProjection(state, revision, asOfSequence, asOfClock, algorithmVersion, sourceHash, storedStateHash);
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
            WHERE match_id = $1 AND sequence <= $2 AND clock_seconds <= $3
            ORDER BY sequence
            """);
        command.Parameters.AddWithValue(matchId.Value);
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

    public async Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, long afterSequence, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > MaximumMatchSeconds) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (afterSequence < -1 || afterSequence > projection.AsOfSequence) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
        if (projection.AsOfClockSeconds > selectedSeconds)
            throw new ArgumentOutOfRangeException(nameof(projection), "Projection is later than the selected time.");
        if (!Guid.TryParse(projection.State.MatchId, out var parsedMatchId) || parsedMatchId == Guid.Empty)
            throw new InvalidDataException("Persisted projection has an invalid match ID.");

        var matchId = new MatchId(parsedMatchId);
        var persistedSequence = await VerifyPersistedProjectionAsync(matchId, projection, cancellationToken);
        await using var command = dataSource.CreateCommand("""
            SELECT payload::text, sequence, clock_seconds, period
            FROM pitchmind.match_events
            WHERE match_id = $1 AND sequence > $2 AND sequence <= $3 AND clock_seconds <= $4
            ORDER BY sequence
            LIMIT $5
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(afterSequence);
        command.Parameters.AddWithValue(persistedSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(limit + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<MatchEvent>(limit + 1);
        long? expectedSequence = afterSequence < 0 ? 0 : afterSequence + 1;
        while (await reader.ReadAsync(cancellationToken))
        {
            var matchEvent = JsonSerializer.Deserialize<MatchEvent>(reader.GetString(0))
                ?? throw new InvalidDataException("Canonical ledger event is empty.");
            MatchEventValidator.Validate(matchEvent);
            if (matchEvent.MatchId != matchId || matchEvent.Sequence != expectedSequence ||
                reader.GetInt64(1) != matchEvent.Sequence || matchEvent.ClockSeconds != reader.GetInt32(2) ||
                matchEvent.Period != reader.GetInt32(3) || matchEvent.ClockSeconds > selectedSeconds)
                throw new InvalidDataException("Canonical event page is not contiguous at the selected time.");
            expectedSequence = matchEvent.Sequence + 1;
            events.Add(matchEvent);
        }

        var hasMore = events.Count > limit;
        if (hasMore) events.RemoveAt(events.Count - 1);
        if (!hasMore && events.Count > 0 && events[^1].Sequence != persistedSequence)
            throw new InvalidDataException("Canonical event page did not reach the selected projection sequence.");
        if (!hasMore && events.Count == 0 && afterSequence != persistedSequence)
            throw new InvalidDataException("Canonical event page is unexpectedly empty before the selected projection sequence.");

        return new PersistedMatchEventPage(events, hasMore ? events[^1].Sequence : null, hasMore);
    }

    private async Task<long> VerifyPersistedProjectionAsync(MatchId matchId, SelectedMatchProjection projection,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT as_of_sequence, as_of_clock_seconds, algorithm_version, source_sha256, state_sha256,
                   state_json::text
            FROM pitchmind.match_projections
            WHERE match_id = $1 AND revision = $2
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(projection.Revision);
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

    private static PersistedMatchProjection ReadProjection(NpgsqlDataReader reader, int offset)
    {
        var revision = reader.GetInt32(offset);
        var sequence = reader.GetInt64(offset + 1);
        var clock = reader.GetInt32(offset + 2);
        var algorithm = reader.GetString(offset + 3);
        var sourceHash = reader.GetString(offset + 4).Trim();
        var stateHash = reader.GetString(offset + 5).Trim();
        var state = JsonSerializer.Deserialize<MatchState>(reader.GetString(offset + 6), JsonOptions)
            ?? throw new InvalidDataException("Persisted match projection is empty.");
        var canonicalJson = JsonSerializer.Serialize(state, JsonOptions);
        var computedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));
        if (computedHash != stateHash || state.LastSequence != sequence || state.ClockSeconds != clock)
            throw new InvalidDataException("Persisted projection metadata or hash does not match its state.");
        return new PersistedMatchProjection(Guid.Parse(state.MatchId), revision, sequence, clock, algorithm, sourceHash, state);
    }
}
