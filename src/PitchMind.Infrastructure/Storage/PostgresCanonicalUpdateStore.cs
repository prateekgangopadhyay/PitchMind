using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Simulation;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Commits event, projection revision, and stable outbox message in one PostgreSQL transaction.</summary>
public sealed class PostgresCanonicalUpdateStore(NpgsqlDataSource dataSource) : ICanonicalUpdateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CanonicalUpdateCommitResult> CommitAsync(CanonicalUpdate update, CancellationToken cancellationToken)
    {
        Validate(update);
        var stateJson = JsonSerializer.Serialize(update.State, JsonOptions);
        var stateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
        var priorStateJson = JsonSerializer.Serialize(update.PriorState, JsonOptions);
        var priorStateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(priorStateJson)));
        var eventJson = JsonSerializer.Serialize(update.Event);
        var sourceEventJson = JsonSerializer.Serialize(update.SourceEvent, JsonOptions);
        var messageId = update.Event.EventId;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockMatchAsync(connection, transaction, update.Event.MatchId.ToString(), cancellationToken);
        await EnsureReplaySeedRegisteredAsync(connection, transaction, update, cancellationToken);

        if (await EventExistsAsync(connection, transaction, update.Event.EventId, cancellationToken))
        {
            if (!await IsExactRetryAsync(connection, transaction, update, eventJson, stateHash, cancellationToken))
                throw new InvalidOperationException("Existing event has no matching projection revision and outbox intent.");
            await transaction.CommitAsync(cancellationToken);
            return CanonicalUpdateCommitResult.AlreadyCommitted;
        }

        var expectedSequence = await ScalarLongAsync(connection, transaction,
            "SELECT COALESCE(MAX(sequence), -1) + 1 FROM pitchmind.match_events WHERE match_id = $1",
            update.Event.MatchId.Value, cancellationToken);
        if (expectedSequence != update.Event.Sequence)
            throw new InvalidOperationException("Sequence conflict, gap or out-of-order event; reconciliation required.");

        await ValidateMatchSourceAsync(connection, transaction, update, cancellationToken);
        await ValidatePredecessorAsync(connection, transaction, update, expectedSequence, priorStateHash, cancellationToken);

        var canonicalRevision = await ScalarLongAsync(connection, transaction,
            "SELECT COALESCE(MAX(canonical_revision), 0) + 1 FROM pitchmind.match_events WHERE match_id = $1",
            update.Event.MatchId.Value, cancellationToken);
        var expectedProjectionRevision = await ScalarLongAsync(connection, transaction,
            "SELECT COALESCE(MAX(revision), 0)::bigint + 1 FROM pitchmind.match_projections WHERE match_id = $1",
            update.Event.MatchId.Value, cancellationToken);
        if (update.Revision != expectedProjectionRevision)
            throw new InvalidOperationException("Projection revision conflict; rebuild from the canonical ledger.");
        var outboxJson = JsonSerializer.Serialize(new ProjectionPublishedV1(
            messageId, update.Event.MatchId.ToString(), canonicalRevision, update.Revision, update.Event.Sequence,
            update.Event.ClockSeconds, update.AlgorithmVersion, update.SourceSha256, stateHash, update.State), JsonOptions);

        await using (var command = new NpgsqlCommand("""
            INSERT INTO pitchmind.match_events(event_id, match_id, canonical_revision, sequence, event_type, clock_seconds, period, occurred_at, payload, source_event_json)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(update.Event.EventId);
            command.Parameters.AddWithValue(update.Event.MatchId.Value);
            command.Parameters.AddWithValue(canonicalRevision);
            command.Parameters.AddWithValue(update.Event.Sequence);
            command.Parameters.AddWithValue((int)update.Event.Type);
            command.Parameters.AddWithValue(update.Event.ClockSeconds);
            command.Parameters.AddWithValue(update.Event.Period);
            command.Parameters.AddWithValue(update.Event.OccurredAt.ToUniversalTime());
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, eventJson);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, sourceEventJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand("""
            INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence, as_of_clock_seconds,
                algorithm_version, source_sha256, state_json, state_sha256)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(update.Event.MatchId.Value);
            command.Parameters.AddWithValue(canonicalRevision);
            command.Parameters.AddWithValue(update.Revision);
            command.Parameters.AddWithValue(update.Event.Sequence);
            command.Parameters.AddWithValue(update.Event.ClockSeconds);
            command.Parameters.AddWithValue(update.AlgorithmVersion);
            command.Parameters.AddWithValue(update.SourceSha256);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, stateJson);
            command.Parameters.AddWithValue(stateHash);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var command = new NpgsqlCommand("""
            INSERT INTO pitchmind.outbox(message_id, match_id, canonical_revision, revision, message_kind, payload)
            VALUES ($1, $2, $3, $4, 'projection.published.v1', $5)
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(messageId);
            command.Parameters.AddWithValue(update.Event.MatchId.Value);
            command.Parameters.AddWithValue(canonicalRevision);
            command.Parameters.AddWithValue(update.Revision);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, outboxJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return CanonicalUpdateCommitResult.Committed;
    }

    private static void Validate(CanonicalUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        ArgumentNullException.ThrowIfNull(update.Event);
        ArgumentNullException.ThrowIfNull(update.State);
        MatchEventValidator.Validate(update.Event);
        if (update.Revision <= 0) throw new ArgumentOutOfRangeException(nameof(update.Revision));
        if (string.IsNullOrWhiteSpace(update.AlgorithmVersion)) throw new ArgumentException("Algorithm version is required.", nameof(update));
        if (string.IsNullOrWhiteSpace(update.SourceSha256) || update.SourceSha256.Length != 64 ||
            update.SourceSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Source SHA-256 must contain exactly 64 lowercase hexadecimal characters.", nameof(update));
        if (update.State.MatchId != update.Event.MatchId.ToString() || update.State.LastSequence != update.Event.Sequence ||
            update.State.ClockSeconds != update.Event.ClockSeconds)
            throw new InvalidOperationException("Projection match, sequence, and selected clock must match the canonical event.");
        if (update.PriorState.MatchId != update.Event.MatchId.ToString() ||
            update.PriorState.LastSequence != update.Event.Sequence - 1)
            throw new InvalidOperationException("Projection predecessor must immediately precede the canonical event.");
        if (!string.Equals(update.State.Provenance, update.PriorState.Provenance, StringComparison.Ordinal))
            throw new InvalidOperationException("Projection provenance cannot change while applying a canonical event.");
    }

    private static async Task ValidateMatchSourceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CanonicalUpdate update, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT source_sha256 = $2 AND provenance = $3
            FROM pitchmind.matches
            WHERE match_id = $1
            """, connection, transaction);
        command.Parameters.AddWithValue(update.Event.MatchId.Value);
        command.Parameters.AddWithValue(update.SourceSha256);
        command.Parameters.AddWithValue(update.PriorState.Provenance);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Aggregate source hash and provenance must match the immutable registered match source.");
    }

    private static async Task ValidatePredecessorAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CanonicalUpdate update, long expectedSequence, string priorStateHash, CancellationToken cancellationToken)
    {
        if (expectedSequence == 0)
        {
            var state = update.PriorState;
            if (state.LastSequence != -1 || state.ClockSeconds != 0 || state.Period != 1 ||
                state.Phase != MatchPhase.NotStarted || state.HomeScore != 0 || state.AwayScore != 0 ||
                state.PossessionTeamId is not null || state.HomeActivePlayers != 11 || state.AwayActivePlayers != 11 ||
                state.Players.Count(player => player.Active && player.TeamId == update.ReplaySeed.HomeTeamId) != 11 ||
                state.Players.Count(player => player.Active && player.TeamId == update.ReplaySeed.AwayTeamId) != 11 ||
                state.Players.Select(player => player.PlayerId).Distinct(StringComparer.Ordinal).Count() != state.Players.Count ||
                state.Players.Any(player => player.Dismissed || player.YellowCards != 0))
                throw new InvalidOperationException("The initial aggregate state is not a valid canonical match starting state.");
            return;
        }

        await using var command = new NpgsqlCommand("""
            SELECT p.state_sha256, p.as_of_clock_seconds, p.as_of_sequence
            FROM pitchmind.match_events e
            JOIN pitchmind.match_projections p
              ON p.match_id = e.match_id AND p.canonical_revision = e.canonical_revision
            WHERE e.match_id = $1 AND e.sequence = $2
            ORDER BY e.canonical_revision DESC, p.revision DESC
            LIMIT 1
            """, connection, transaction);
        command.Parameters.AddWithValue(update.Event.MatchId.Value);
        command.Parameters.AddWithValue(expectedSequence - 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0) || reader.IsDBNull(1) || reader.IsDBNull(2))
            throw new InvalidOperationException("The canonical predecessor has no persisted projection; rebuild from the canonical ledger.");
        var storedStateHash = reader.GetString(0).Trim();
        var storedClockSeconds = reader.GetInt32(1);
        var storedSequence = reader.GetInt64(2);
        if (storedSequence != expectedSequence - 1 ||
            !string.Equals(storedStateHash, priorStateHash, StringComparison.Ordinal) ||
            storedClockSeconds != update.PriorState.ClockSeconds)
            throw new InvalidOperationException("Aggregate history diverges from the persisted canonical predecessor; rebuild from the canonical ledger.");
    }

    private static async Task LockMatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string matchId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction);
        command.Parameters.AddWithValue(matchId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureReplaySeedRegisteredAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CanonicalUpdate update, CancellationToken cancellationToken)
    {
        var seedJson = JsonSerializer.Serialize(update.ReplaySeed, JsonOptions);
        await using var command = new NpgsqlCommand("""
            SELECT replay_seed_json = $2::jsonb AND provenance = $3
            FROM pitchmind.matches
            WHERE match_id = $1 AND source_sha256 = $4
            """, connection, transaction);
        command.Parameters.AddWithValue(update.Event.MatchId.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, seedJson);
        command.Parameters.AddWithValue(update.ReplaySeed.Provenance);
        command.Parameters.AddWithValue(update.SourceSha256);
        if (await command.ExecuteScalarAsync(cancellationToken) is not true)
            throw new InvalidOperationException("Canonical update replay seed does not match the immutable registered match source.");
    }

    private static async Task<bool> EventExistsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid eventId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pitchmind.match_events WHERE event_id = $1)", connection, transaction);
        command.Parameters.AddWithValue(eventId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<bool> IsExactRetryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CanonicalUpdate update, string eventJson, string stateHash, CancellationToken cancellationToken)
    {
        var sourceEventJson = JsonSerializer.Serialize(update.SourceEvent, JsonOptions);
        await using var eventCommand = new NpgsqlCommand("SELECT payload::text, canonical_revision, source_event_json::text FROM pitchmind.match_events WHERE event_id = $1", connection, transaction);
        eventCommand.Parameters.AddWithValue(update.Event.EventId);
        await using var eventReader = await eventCommand.ExecuteReaderAsync(cancellationToken);
        if (!await eventReader.ReadAsync(cancellationToken) || eventReader.IsDBNull(1)) return false;
        var storedEvent = eventReader.GetString(0);
        var canonicalRevision = eventReader.GetInt64(1);
        var storedSourceEvent = eventReader.IsDBNull(2) ? null : eventReader.GetString(2);
        if (JsonSerializer.Deserialize<PitchMind.Domain.Events.MatchEvent>(storedEvent) != update.Event) return false;
        var storedEnvelope = storedSourceEvent is null ? null : JsonSerializer.Deserialize<SimulationEvent>(storedSourceEvent, JsonOptions);
        if (storedEnvelope != update.SourceEvent) return false;
        await eventReader.DisposeAsync();

        await using var projectionCommand = new NpgsqlCommand("SELECT state_sha256 FROM pitchmind.match_projections WHERE match_id = $1 AND canonical_revision = $2 AND revision = $3 AND as_of_sequence = $4", connection, transaction);
        projectionCommand.Parameters.AddWithValue(update.Event.MatchId.Value);
        projectionCommand.Parameters.AddWithValue(canonicalRevision);
        projectionCommand.Parameters.AddWithValue(update.Revision);
        projectionCommand.Parameters.AddWithValue(update.Event.Sequence);
        if (await projectionCommand.ExecuteScalarAsync(cancellationToken) is not string storedHash || storedHash.Trim() != stateHash)
            return false;

        var outboxJson = JsonSerializer.Serialize(new ProjectionPublishedV1(
            update.Event.EventId, update.Event.MatchId.ToString(), canonicalRevision, update.Revision, update.Event.Sequence,
            update.Event.ClockSeconds, update.AlgorithmVersion, update.SourceSha256, stateHash, update.State), JsonOptions);
        await using var outboxCommand = new NpgsqlCommand("SELECT canonical_revision = $4 AND payload = $5::jsonb FROM pitchmind.outbox WHERE message_id = $1 AND match_id = $2 AND revision = $3", connection, transaction);
        outboxCommand.Parameters.AddWithValue(update.Event.EventId);
        outboxCommand.Parameters.AddWithValue(update.Event.MatchId.Value);
        outboxCommand.Parameters.AddWithValue(update.Revision);
        outboxCommand.Parameters.AddWithValue(canonicalRevision);
        outboxCommand.Parameters.AddWithValue(NpgsqlDbType.Jsonb, outboxJson);
        return await outboxCommand.ExecuteScalarAsync(cancellationToken) is true;
    }

    private static async Task<long> ScalarLongAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string sql, object value, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(value);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed record ProjectionPublishedV1(Guid MessageId, string MatchId, long CanonicalRevision, int ProjectionRevision, long Sequence,
        int ClockSeconds, string AlgorithmVersion, string SourceSha256, string StateSha256, object State);
}
