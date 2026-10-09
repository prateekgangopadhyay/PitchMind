using System.Runtime.CompilerServices;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Append-only event ledger. Caller owns the shared data source lifecycle; schema migrations are separate.</summary>
public sealed class PostgresMatchEventStore(NpgsqlDataSource dataSource) : IMatchEventStore
{
    public async Task AppendAsync(MatchEvent matchEvent, CancellationToken cancellationToken)
    {
        ValidateForIngestion(matchEvent);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Lock event ID first, then match. This serializes a global ID retry across matches
        // and a match sequence across event IDs without creating a lock-order cycle.
        await AcquireLockAsync(connection, transaction, $"event:{matchEvent.EventId:N}", cancellationToken);
        await AcquireLockAsync(connection, transaction, $"match:{matchEvent.MatchId}", cancellationToken);
        await using (var command = new NpgsqlCommand("SELECT payload::text FROM pitchmind.match_events WHERE event_id = $1", connection, transaction))
        {
            command.Parameters.AddWithValue(matchEvent.EventId);
            var existing = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (existing is not null)
            {
                if (!MatchEventPayloadEquality.AreEqual(Deserialize(existing), matchEvent))
                {
                    var expected = await GetExpectedSequenceAsync(connection, transaction, matchEvent.MatchId, cancellationToken);
                    throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                        MatchEventIngestionFailure.EventIdPayloadConflict, expected, matchEvent.Sequence);
                }
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }
        await using (var command = new NpgsqlCommand("SELECT event_id FROM pitchmind.match_events WHERE match_id = $1 AND sequence = $2", connection, transaction))
        {
            command.Parameters.AddWithValue(matchEvent.MatchId.Value);
            command.Parameters.AddWithValue(matchEvent.Sequence);
            var sequenceOwner = await command.ExecuteScalarAsync(cancellationToken);
            if (sequenceOwner is Guid)
            {
                var expected = await GetExpectedSequenceAsync(connection, transaction, matchEvent.MatchId, cancellationToken);
                throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                    MatchEventIngestionFailure.SequenceAlreadyUsed, expected, matchEvent.Sequence);
            }
        }
        await using (var command = new NpgsqlCommand("SELECT COALESCE(MAX(sequence), -1) + 1 FROM pitchmind.match_events WHERE match_id = $1", connection, transaction))
        {
            command.Parameters.AddWithValue(matchEvent.MatchId.Value);
            var expected = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (matchEvent.Sequence > expected)
                throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                    MatchEventIngestionFailure.SequenceGap, expected, matchEvent.Sequence);
            if (matchEvent.Sequence < expected)
                throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                    MatchEventIngestionFailure.OutOfOrder, expected, matchEvent.Sequence);
        }
        await using (var command = new NpgsqlCommand("""
            INSERT INTO pitchmind.match_events(event_id, match_id, sequence, event_type, clock_seconds, period, occurred_at, payload)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """, connection, transaction))
        {
            command.Parameters.AddWithValue(matchEvent.EventId);
            command.Parameters.AddWithValue(matchEvent.MatchId.Value);
            command.Parameters.AddWithValue(matchEvent.Sequence);
            command.Parameters.AddWithValue((int)matchEvent.Type);
            command.Parameters.AddWithValue(matchEvent.ClockSeconds);
            command.Parameters.AddWithValue(matchEvent.Period);
            command.Parameters.AddWithValue(matchEvent.OccurredAt.ToUniversalTime());
            // Payload retains .NET timestamp precision; timestamp index is microsecond precision.
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(matchEvent));
            try { await command.ExecuteNonQueryAsync(cancellationToken); }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                var failure = exception.ConstraintName switch
                {
                    "match_events_pkey" => MatchEventIngestionFailure.EventIdPayloadConflict,
                    "match_events_match_id_sequence_key" => MatchEventIngestionFailure.SequenceAlreadyUsed,
                    _ => (MatchEventIngestionFailure?)null
                };
                if (failure is null) throw;
                throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId, failure.Value,
                    null, matchEvent.Sequence);
            }
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async IAsyncEnumerable<MatchEvent> ReadAsync(MatchId matchId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        await using var command = dataSource.CreateCommand("SELECT payload::text, sequence FROM pitchmind.match_events WHERE match_id = $1 ORDER BY sequence");
        command.Parameters.AddWithValue(matchId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        long expected = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var matchEvent = Deserialize(reader.GetString(0));
            MatchEventValidator.Validate(matchEvent);
            if (matchEvent.MatchId != matchId || matchEvent.Sequence != expected || reader.GetInt64(1) != expected)
                throw new InvalidDataException("Canonical ledger integrity violation.");
            expected++;
            yield return matchEvent;
        }
    }
    private static MatchEvent Deserialize(string payload) => JsonSerializer.Deserialize<MatchEvent>(payload)
        ?? throw new InvalidDataException("Canonical ledger payload is empty.");

    private static void ValidateForIngestion(MatchEvent matchEvent)
    {
        ArgumentNullException.ThrowIfNull(matchEvent);
        try { MatchEventValidator.Validate(matchEvent); }
        catch (ArgumentException exception)
        {
            throw new MatchEventIngestionException(matchEvent.MatchId, matchEvent.EventId,
                MatchEventIngestionFailure.InvalidEventPayload, null, matchEvent.Sequence, exception);
        }
    }

    private static async Task AcquireLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string lockKey, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction);
        command.Parameters.AddWithValue(lockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> GetExpectedSequenceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        MatchId matchId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT COALESCE(MAX(sequence), -1) + 1 FROM pitchmind.match_events WHERE match_id = $1", connection, transaction);
        command.Parameters.AddWithValue(matchId.Value);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
