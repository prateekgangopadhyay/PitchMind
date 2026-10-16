using Npgsql;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Stores a partition cursor after a consumer's inbox transaction has committed.
/// This PostgreSQL cursor is not itself an Event Hubs checkpoint call.
/// </summary>
public sealed class PostgresConsumerCheckpointStore(NpgsqlDataSource dataSource) : IConsumerCheckpointStore
{
    public async Task<ConsumerCheckpointAdvanceResult> AdvancePoisonAsync(
        string consumerId,
        string partitionId,
        string offset,
        long brokerSequenceNumber,
        Guid poisonId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(consumerId) || consumerId.Length > 128 ||
            string.IsNullOrWhiteSpace(partitionId) || partitionId.Length > 128 ||
            string.IsNullOrWhiteSpace(offset) || offset.Length > 128)
            throw new ArgumentException("Consumer, partition, and offset must be bounded non-empty values.");
        if (brokerSequenceNumber < 0 || poisonId == Guid.Empty)
            throw new ArgumentException("Poison checkpoint requires a non-negative broker sequence and stable poison ID.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var ensure = new NpgsqlCommand("""
            INSERT INTO pitchmind.consumer_checkpoints (consumer_id, partition_id)
            VALUES ($1, $2) ON CONFLICT (consumer_id, partition_id) DO NOTHING
            """, connection, transaction))
        {
            ensure.Parameters.AddWithValue(consumerId);
            ensure.Parameters.AddWithValue(partitionId);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var read = new NpgsqlCommand("""
            SELECT last_offset, last_broker_sequence_number, last_poison_id
            FROM pitchmind.consumer_checkpoints
            WHERE consumer_id = $1 AND partition_id = $2 FOR UPDATE
            """, connection, transaction);
        read.Parameters.AddWithValue(consumerId);
        read.Parameters.AddWithValue(partitionId);
        await using var reader = await read.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Consumer checkpoint row could not be locked.");
        var lastOffset = reader.IsDBNull(0) ? null : reader.GetString(0);
        var lastSequence = reader.GetInt64(1);
        var lastPoisonId = reader.IsDBNull(2) ? (Guid?)null : reader.GetGuid(2);
        await reader.DisposeAsync();

        if (lastSequence >= 0)
        {
            if (brokerSequenceNumber == lastSequence)
            {
                if (offset != lastOffset)
                    throw new InvalidOperationException("A different event already owns this consumer partition sequence.");
                await transaction.CommitAsync(cancellationToken);
                return lastPoisonId == poisonId
                    ? ConsumerCheckpointAdvanceResult.AlreadyCurrent
                    : ConsumerCheckpointAdvanceResult.StaleReplay;
            }
            if (brokerSequenceNumber < lastSequence)
            {
                await transaction.CommitAsync(cancellationToken);
                return ConsumerCheckpointAdvanceResult.StaleReplay;
            }
            if (brokerSequenceNumber != lastSequence + 1)
                throw new InvalidOperationException("Consumer checkpoint cannot skip an unprocessed partition sequence.");
        }

        await using var update = new NpgsqlCommand("""
            UPDATE pitchmind.consumer_checkpoints
            SET last_offset = $3, last_broker_sequence_number = $4,
                last_match_id = NULL, last_message_id = NULL,
                last_canonical_sequence = NULL, last_poison_id = $5, updated_at = now()
            WHERE consumer_id = $1 AND partition_id = $2
            """, connection, transaction);
        update.Parameters.AddWithValue(consumerId);
        update.Parameters.AddWithValue(partitionId);
        update.Parameters.AddWithValue(offset);
        update.Parameters.AddWithValue(brokerSequenceNumber);
        update.Parameters.AddWithValue(poisonId);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Consumer checkpoint row disappeared while locked.");
        await transaction.CommitAsync(cancellationToken);
        return ConsumerCheckpointAdvanceResult.Advanced;
    }

    public async Task<ConsumerCheckpointAdvanceResult> AdvanceAsync(
        ConsumerCheckpoint checkpoint,
        CancellationToken cancellationToken,
        bool alreadyProcessed = false)
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
            SELECT last_offset, last_broker_sequence_number, last_match_id, last_message_id, last_canonical_sequence,
                   last_poison_id
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
        Guid? lastPoisonId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
        await reader.DisposeAsync();

        if (lastBrokerSequence >= 0)
        {
            if (checkpoint.BrokerSequenceNumber == lastBrokerSequence)
            {
                if (lastPoisonId is not null && checkpoint.Offset == lastOffset)
                {
                    // A corrected/replayed canonical event may replace a poison marker
                    // at the same broker position after manual partition replay.
                }
                else
                {
                    var identical = checkpoint.Offset == lastOffset && checkpoint.MatchId == lastMatchId &&
                        checkpoint.MessageId == lastMessageId && checkpoint.CanonicalSequence == lastCanonicalSequence &&
                        lastPoisonId is null;
                    if (!identical)
                        throw new InvalidOperationException("A different event already owns this consumer partition sequence.");

                    await transaction.CommitAsync(cancellationToken);
                    return ConsumerCheckpointAdvanceResult.AlreadyCurrent;
                }
            }
            else if (checkpoint.BrokerSequenceNumber < lastBrokerSequence && alreadyProcessed)
            {
                // Event Hubs may replay older records when its Blob checkpoint lags the
                // durable inbox/cursor. They are safe to consume again, but must never
                // move either cursor backwards.
                await transaction.CommitAsync(cancellationToken);
                return ConsumerCheckpointAdvanceResult.StaleReplay;
            }

            else if (checkpoint.BrokerSequenceNumber < lastBrokerSequence)
            {
                throw new InvalidOperationException("Consumer checkpoint cannot move backwards.");
            }
            else if (checkpoint.BrokerSequenceNumber != lastBrokerSequence + 1 &&
                     checkpoint.BrokerSequenceNumber != lastBrokerSequence)
            {
                throw new InvalidOperationException("Consumer checkpoint cannot skip an unprocessed partition sequence.");
            }
        }

        await using var update = new NpgsqlCommand("""
            UPDATE pitchmind.consumer_checkpoints
            SET last_offset = $3, last_broker_sequence_number = $4, last_match_id = $5,
                last_message_id = $6, last_canonical_sequence = $7, last_poison_id = NULL, updated_at = now()
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
