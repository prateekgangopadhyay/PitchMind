using Npgsql;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Append-only poison identity with idempotent retry accounting; payload bytes are never persisted.</summary>
public sealed class PostgresProjectionPoisonStore(NpgsqlDataSource dataSource) : IProjectionPoisonStore
{
    public async Task MarkReplayedAsync(Guid poisonId, CancellationToken cancellationToken)
    {
        if (poisonId == Guid.Empty) throw new ArgumentException("Poison ID is required.", nameof(poisonId));
        await using var command = dataSource.CreateCommand("""
            UPDATE pitchmind.consumer_projection_poison
            SET disposition = 'replayed', last_observed_at = now()
            WHERE poison_id = $1 AND disposition = 'pending'
            """);
        command.Parameters.AddWithValue(poisonId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Guid> RecordAsync(ProjectionPoisonRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.PoisonId == Guid.Empty) throw new ArgumentException("Poison ID is required.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.ConsumerId) || record.ConsumerId.Length > 128 ||
            string.IsNullOrWhiteSpace(record.PartitionId) || record.PartitionId.Length > 128 ||
            string.IsNullOrWhiteSpace(record.Offset) || record.Offset.Length > 128)
            throw new ArgumentException("Consumer, partition, and offset must be bounded non-empty values.", nameof(record));
        if (record.BrokerSequenceNumber < 0) throw new ArgumentOutOfRangeException(nameof(record));
        if (record.PayloadSha256.Length != 64 || !record.PayloadSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Payload digest must be a SHA-256 hex value.", nameof(record));
        if (record.ReasonCode.Length is < 1 or > 100 || record.ReasonCode[0] is < 'A' or > 'Z' ||
            record.ReasonCode.Any(character => character is not (>= 'A' and <= 'Z' or >= '0' and <= '9' or '_')))
            throw new ArgumentException("Reason code must be a stable uppercase identifier.", nameof(record));
        if (record.ExceptionType.Length is < 1 or > 128 || record.ExceptionType.Any(char.IsControl))
            throw new ArgumentException("Exception type must be a bounded type name.", nameof(record));

        await using var command = dataSource.CreateCommand("""
            INSERT INTO pitchmind.consumer_projection_poison
                (poison_id, consumer_id, partition_id, broker_offset, broker_sequence_number,
                 payload_sha256, reason_code, exception_type)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            ON CONFLICT (consumer_id, partition_id, broker_sequence_number) DO UPDATE
                SET attempt_count = pitchmind.consumer_projection_poison.attempt_count + 1,
                    last_observed_at = now()
                WHERE pitchmind.consumer_projection_poison.poison_id = EXCLUDED.poison_id
                  AND pitchmind.consumer_projection_poison.broker_offset = EXCLUDED.broker_offset
                  AND pitchmind.consumer_projection_poison.payload_sha256 = EXCLUDED.payload_sha256
                  AND pitchmind.consumer_projection_poison.reason_code = EXCLUDED.reason_code
                  AND pitchmind.consumer_projection_poison.exception_type = EXCLUDED.exception_type
            RETURNING poison_id
            """);
        command.Parameters.AddWithValue(record.PoisonId);
        command.Parameters.AddWithValue(record.ConsumerId);
        command.Parameters.AddWithValue(record.PartitionId);
        command.Parameters.AddWithValue(record.Offset);
        command.Parameters.AddWithValue(record.BrokerSequenceNumber);
        command.Parameters.AddWithValue(record.PayloadSha256.ToLowerInvariant());
        command.Parameters.AddWithValue(record.ReasonCode);
        command.Parameters.AddWithValue(record.ExceptionType);
        var storedId = await command.ExecuteScalarAsync(cancellationToken);
        return storedId is Guid id
            ? id
            : throw new InvalidDataException("A poison position was redelivered with conflicting identity or payload.");
    }
}
