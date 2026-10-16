using Npgsql;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Stores a partition cursor after a consumer's inbox transaction has committed.
/// This PostgreSQL cursor is not itself an Event Hubs checkpoint call.
/// </summary>
public sealed class PostgresConsumerCheckpointStore(NpgsqlDataSource dataSource) : IConsumerCheckpointStore
{
    public async Task<ConsumerCheckpointAdvanceResult> AdvanceAsync(
        ConsumerCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        Validate(checkpoint);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var ensure = new NpgsqlCommand("""
            INSERT INTO pitchmind.consumer_checkpoints (consumer_id, partition_id)
            VALUES ($1, $2)
            ON CONFLICT (consumer_id, partition_id) DO NOTHING
            """, connection, transaction))
        {
            ensure.Parameters.AddWithValue(checkpoint.ConsumerId);
            ensure.Parameters.AddWithValue(checkpoint.PartitionId);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var read = new NpgsqlCommand("""
            SELECT last_offset, last_broker_sequence_number, last_match_id, last_message_id, last_canonical_sequence
            FROM pitchmind.consumer_checkpoints
            WHERE consumer_id = $1 AND partition_id = $2
            FOR UPDATE
            """, connection, transaction);
        read.Parameters.AddWithValue(checkpoint.ConsumerId);
        read.Parameters.AddWithValue(checkpoint.PartitionId);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Consumer checkpoint row could not be locked.");

        var lastOffset = reader.IsDBNull(0) ? null : reader.GetString(0);
        var lastBrokerSequence = reader.GetInt64(1);
        Guid? lastMatchId = reader.IsDBNull(2) ? null : reader.GetGuid(2);
        Guid? lastMessageId = reader.IsDBNull(3) ? null : reader.GetGuid(3);
        long? lastCanonicalSequence = reader.IsDBNull(4) ? null : reader.GetInt64(4);
        await reader.DisposeAsync();

        if (lastBrokerSequence >= 0)
        {
            if (checkpoint.BrokerSequenceNumber == lastBrokerSequence)
            {
                var identical = checkpoint.Offset == lastOffset && checkpoint.MatchId == lastMatchId &&
                    checkpoint.MessageId == lastMessageId && checkpoint.CanonicalSequence == lastCanonicalSequence;
                if (!identical)
                    throw new InvalidOperationException("A different event already owns this consumer partition sequence.");

                await transaction.CommitAsync(cancellationToken);
                return ConsumerCheckpointAdvanceResult.AlreadyCurrent;
            }

            if (checkpoint.BrokerSequenceNumber < lastBrokerSequence)
                throw new InvalidOperationException("Consumer checkpoint cannot move backwards.");
            if (checkpoint.BrokerSequenceNumber != lastBrokerSequence + 1)
                throw new InvalidOperationException("Consumer checkpoint cannot skip an unprocessed partition sequence.");
        }

        await using var update = new NpgsqlCommand("""
            UPDATE pitchmind.consumer_checkpoints
            SET last_offset = $3, last_broker_sequence_number = $4, last_match_id = $5,
                last_message_id = $6, last_canonical_sequence = $7, updated_at = now()
            WHERE consumer_id = $1 AND partition_id = $2
            """, connection, transaction);
        update.Parameters.AddWithValue(checkpoint.ConsumerId);
        update.Parameters.AddWithValue(checkpoint.PartitionId);
        update.Parameters.AddWithValue(checkpoint.Offset);
        update.Parameters.AddWithValue(checkpoint.BrokerSequenceNumber);
        update.Parameters.AddWithValue(checkpoint.MatchId);
        update.Parameters.AddWithValue(checkpoint.MessageId);
        update.Parameters.AddWithValue(checkpoint.CanonicalSequence);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Consumer checkpoint row disappeared while locked.");

        await transaction.CommitAsync(cancellationToken);
        return ConsumerCheckpointAdvanceResult.Advanced;
    }

    private static void Validate(ConsumerCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        if (string.IsNullOrWhiteSpace(checkpoint.ConsumerId) || checkpoint.ConsumerId.Length > 128)
            throw new ArgumentException("Consumer ID must contain 1 to 128 characters.", nameof(checkpoint));
        if (string.IsNullOrWhiteSpace(checkpoint.PartitionId) || checkpoint.PartitionId.Length > 128)
            throw new ArgumentException("Partition ID must contain 1 to 128 characters.", nameof(checkpoint));
        if (string.IsNullOrWhiteSpace(checkpoint.Offset) || checkpoint.Offset.Length > 128)
            throw new ArgumentException("Offset must contain 1 to 128 characters.", nameof(checkpoint));
        if (checkpoint.BrokerSequenceNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpoint), "Broker sequence number cannot be negative.");
        if (checkpoint.MatchId == Guid.Empty || checkpoint.MessageId == Guid.Empty)
            throw new ArgumentException("Match and message IDs are required.", nameof(checkpoint));
        if (checkpoint.CanonicalSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(checkpoint), "Canonical sequence cannot be negative.");
    }
}
