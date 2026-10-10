using System.Diagnostics.Metrics;
using Npgsql;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

public enum OutboxDispatchResult { NoMessage, Published, RetryScheduled, DeadLettered }

/// <summary>Leases due messages, publishes with a stable ID, and records bounded retry state.</summary>
public sealed class PostgresOutboxDispatcher(
    NpgsqlDataSource dataSource,
    IOutboxPublisher publisher,
    int maxAttempts = 5,
    TimeSpan? leaseDuration = null)
{
    private static readonly Meter OutboxMeter = new("PitchMind.Infrastructure.Outbox", "1.0.0");
    private static readonly Counter<long> PublishedMessages = OutboxMeter.CreateCounter<long>("pitchmind.outbox.published");
    private static readonly Counter<long> RetriedMessages = OutboxMeter.CreateCounter<long>("pitchmind.outbox.retry_scheduled");
    private static readonly Counter<long> DeadLetteredMessages = OutboxMeter.CreateCounter<long>("pitchmind.outbox.dead_lettered");
    private static readonly Histogram<double> MessageAgeSeconds = OutboxMeter.CreateHistogram<double>("pitchmind.outbox.message_age", "s");
    private readonly TimeSpan _leaseDuration = leaseDuration ?? TimeSpan.FromSeconds(30);

    public async Task<OutboxDispatchResult> DispatchOneAsync(CancellationToken cancellationToken)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        var claimToken = Guid.NewGuid();
        var claim = await ClaimAsync(claimToken, cancellationToken);
        if (claim is null) return OutboxDispatchResult.NoMessage;
        MessageAgeSeconds.Record(Math.Max(0, (DateTimeOffset.UtcNow - claim.CreatedAt).TotalSeconds));

        try
        {
            await publisher.PublishAsync(claim.MessageId, claim.Payload, cancellationToken);
            await CompleteAsync(claim.MessageId, claimToken, cancellationToken);
            PublishedMessages.Add(1);
            return OutboxDispatchResult.Published;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Leave the lease in place; another worker can recover after it expires.
            throw;
        }
        catch
        {
            var result = await RecordFailureAsync(claim.MessageId, claimToken, claim.Attempt, cancellationToken);
            if (result == OutboxDispatchResult.DeadLettered) DeadLetteredMessages.Add(1);
            else RetriedMessages.Add(1);
            return result;
        }
    }

    private async Task<ClaimedMessage?> ClaimAsync(Guid claimToken, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH candidate AS (
                SELECT o.message_id FROM pitchmind.outbox AS o
                WHERE o.published_at IS NULL AND o.failed_at IS NULL
                  AND (o.next_attempt_at IS NULL OR o.next_attempt_at <= now())
                  AND (o.lease_expires_at IS NULL OR o.lease_expires_at <= now())
                  AND NOT EXISTS (
                      SELECT 1 FROM pitchmind.outbox AS earlier
                      WHERE earlier.match_id = o.match_id
                        AND earlier.canonical_revision < o.canonical_revision
                        AND earlier.published_at IS NULL
                        AND earlier.failed_at IS NULL
                  )
                ORDER BY o.created_at, o.message_id
                FOR UPDATE OF o SKIP LOCKED LIMIT 1
            )
            UPDATE pitchmind.outbox AS o
            SET claim_token = $1, lease_expires_at = now() + $2, attempt_count = attempt_count + 1
            FROM candidate
            WHERE o.message_id = candidate.message_id
            RETURNING o.message_id, o.payload::text, o.attempt_count, o.created_at
            """, connection, transaction);
        command.Parameters.AddWithValue(claimToken);
        command.Parameters.AddWithValue(_leaseDuration);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await reader.DisposeAsync();
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var claim = new ClaimedMessage(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetFieldValue<DateTimeOffset>(3));
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return claim;
    }

    private async Task CompleteAsync(Guid messageId, Guid claimToken, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("""
            UPDATE pitchmind.outbox SET published_at = now(), claim_token = NULL, lease_expires_at = NULL,
                next_attempt_at = NULL, last_error_code = NULL
            WHERE message_id = $1 AND claim_token = $2 AND published_at IS NULL
            """);
        command.Parameters.AddWithValue(messageId);
        command.Parameters.AddWithValue(claimToken);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Outbox lease was lost before acknowledging publication.");
    }

    private async Task<OutboxDispatchResult> RecordFailureAsync(Guid messageId, Guid claimToken, int attempt,
        CancellationToken cancellationToken)
    {
        var deadLetter = attempt >= maxAttempts;
        var delaySeconds = Math.Min(300, Math.Pow(2, Math.Min(attempt - 1, 8)));
        await using var command = dataSource.CreateCommand("""
            UPDATE pitchmind.outbox
            SET claim_token = NULL, lease_expires_at = NULL,
                next_attempt_at = CASE WHEN $3 THEN NULL ELSE now() + ($4 * interval '1 second') END,
                failed_at = CASE WHEN $3 THEN now() ELSE NULL END,
                last_error_code = 'publisher-failure'
            WHERE message_id = $1 AND claim_token = $2 AND published_at IS NULL
            """);
        command.Parameters.AddWithValue(messageId);
        command.Parameters.AddWithValue(claimToken);
        command.Parameters.AddWithValue(deadLetter);
        command.Parameters.AddWithValue(delaySeconds);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Outbox lease was lost before recording the failed attempt.");
        return deadLetter ? OutboxDispatchResult.DeadLettered : OutboxDispatchResult.RetryScheduled;
    }

    private sealed record ClaimedMessage(Guid MessageId, string Payload, int Attempt, DateTimeOffset CreatedAt);
}
