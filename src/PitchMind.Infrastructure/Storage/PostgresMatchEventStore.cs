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
        MatchEventValidator.Validate(matchEvent);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        // Transaction-scoped lock serializes writers for a match across processes.
        // Hash collisions only serialize unrelated matches; they do not affect correctness.
        await using (var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended($1, 0))", connection, transaction))
        {
            command.Parameters.AddWithValue(matchEvent.MatchId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = new NpgsqlCommand("SELECT payload::text FROM pitchmind.match_events WHERE event_id = $1", connection, transaction))
        {
            command.Parameters.AddWithValue(matchEvent.EventId);
            var existing = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (existing is not null)
            {
                if (Deserialize(existing) != matchEvent) throw new InvalidOperationException("Conflicting payload for existing event ID.");
                await transaction.CommitAsync(cancellationToken);
                return;
            }
        }
        await using (var command = new NpgsqlCommand("SELECT COALESCE(MAX(sequence), -1) + 1 FROM pitchmind.match_events WHERE match_id = $1", connection, transaction))
        {
            command.Parameters.AddWithValue(matchEvent.MatchId.Value);
            var expected = (long)(await command.ExecuteScalarAsync(cancellationToken))!;
            if (matchEvent.Sequence != expected)
                throw new InvalidOperationException("Sequence conflict, gap or out-of-order event; reconciliation required.");
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
            { throw new InvalidOperationException("Event ID or sequence conflict; reconciliation required.", exception); }
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
}
