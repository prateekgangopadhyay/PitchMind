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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
        int? maximumRevision, CancellationToken cancellationToken)
    {
        if (maximumRevision is <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRevision));
        return await ReadAtAsync(matchId, selectedSeconds, maximumRevision, null, null, cancellationToken);
    }

    public async Task<SelectedMatchProjection?> GetAtCanonicalRevisionAsync(MatchId matchId, long canonicalRevision,
        int selectedSeconds, string algorithmVersion, CancellationToken cancellationToken)
    {
        if (canonicalRevision <= 0) throw new ArgumentOutOfRangeException(nameof(canonicalRevision));
        if (string.IsNullOrWhiteSpace(algorithmVersion)) throw new ArgumentException("Algorithm version is required.", nameof(algorithmVersion));
        return await ReadAtAsync(matchId, selectedSeconds, null, canonicalRevision, algorithmVersion, cancellationToken);
    }

    private async Task<SelectedMatchProjection?> ReadAtAsync(MatchId matchId, int selectedSeconds,
        int? maximumRevision, long? canonicalRevision, string? algorithmVersion, CancellationToken cancellationToken)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        if (selectedSeconds is < 0 or > MaximumMatchSeconds) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));

        await using var command = dataSource.CreateCommand("""
            SELECT canonical_revision, revision, as_of_sequence, as_of_clock_seconds, algorithm_version, source_sha256,
                   state_sha256, state_json::text
            FROM pitchmind.match_projections
            WHERE match_id = $1 AND as_of_clock_seconds <= $2
              AND ($3::integer IS NULL OR revision <= $3)
              AND ($4::bigint IS NULL OR canonical_revision = $4)
              AND ($5::text IS NULL OR algorithm_version = $5)
            ORDER BY as_of_clock_seconds DESC, as_of_sequence DESC, revision DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue((object?)maximumRevision ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)canonicalRevision ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)algorithmVersion ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        // Legacy projections intentionally retain NULL: migration cannot invent admission provenance.
        long? selectedCanonicalRevision = reader.IsDBNull(0) ? null : reader.GetInt64(0);
        var revision = reader.GetInt32(1);
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

        return new SelectedMatchProjection(state, selectedCanonicalRevision, revision, asOfSequence, asOfClock,
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

    private async Task<long> VerifyPersistedProjectionAsync(MatchId matchId, SelectedMatchProjection projection,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT as_of_sequence, as_of_clock_seconds, algorithm_version, source_sha256, state_sha256,
                   state_json::text
            FROM pitchmind.match_projections
            WHERE match_id = $1 AND canonical_revision IS NOT DISTINCT FROM $2::bigint AND revision = $3
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue((object?)projection.CanonicalRevision ?? DBNull.Value);
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
        if ((projection.CanonicalRevision.HasValue && projection.CanonicalRevision.Value <= 0) ||
            projection.AsOfSequence != sequence || projection.AsOfClockSeconds != clock ||
            projection.AlgorithmVersion != algorithmVersion || projection.SourceSha256.Trim() != sourceHash ||
            projection.StateSha256.Trim() != stateHash || suppliedHash != stateHash)
            throw new InvalidDataException("Supplied projection does not match its persisted revision.");

        return sequence;
    }
}
