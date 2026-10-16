using System.Security.Cryptography;
using System.Text;
using Npgsql;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

public sealed record ProjectionConsumeResult(
    bool AppliedProjection,
    ConsumerCheckpointAdvanceResult CheckpointResult);

/// <summary>
/// Applies committed projection notifications to a rebuildable, noncanonical PostgreSQL
/// snapshot. The inbox effect commits before the separate partition cursor advances.
/// </summary>
public sealed class PostgresProjectionPublishedConsumer(
    NpgsqlDataSource dataSource,
    IConsumerCheckpointStore checkpointStore)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<ProjectionConsumeResult> ConsumeAsync(
        string consumerId,
        string partitionId,
        string offset,
        long brokerSequenceNumber,
        Guid brokerMessageId,
        ConsumerProjectionSnapshot projection,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ValidatePosition(consumerId, partitionId, offset, brokerSequenceNumber, brokerMessageId, payload);
        ValidateProjection(brokerMessageId, projection);

        var payloadSha256 = Convert.ToHexString(SHA256.HashData(payload.Span)).ToLowerInvariant();
        _ = StrictUtf8.GetString(payload.Span); // Reject malformed UTF-8 before opening a transaction.
        var recomputedStateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(projection.StateJson))).ToLowerInvariant();
        if (!string.Equals(recomputedStateHash, projection.StateSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Projection state hash does not match the state payload.");
        var applied = await ApplyInboxAndSnapshotAsync(consumerId, projection, payloadSha256, cancellationToken);

        var checkpointResult = await checkpointStore.AdvanceAsync(
            new ConsumerCheckpoint(consumerId, partitionId, offset, brokerSequenceNumber,
                projection.MatchId, projection.MessageId, projection.Sequence),
            cancellationToken,
            alreadyProcessed: !applied);

        return new ProjectionConsumeResult(applied, checkpointResult);
    }

    private async Task<bool> ApplyInboxAndSnapshotAsync(
        string consumerId,
        ConsumerProjectionSnapshot projection,
        string payloadSha256,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var insertReceipt = new NpgsqlCommand("""
            INSERT INTO pitchmind.consumer_projection_inbox (consumer_id, message_id, payload_sha256)
            VALUES ($1, $2, $3)
            ON CONFLICT (consumer_id, message_id) DO NOTHING
            RETURNING message_id
            """, connection, transaction))
        {
            insertReceipt.Parameters.AddWithValue(consumerId);
            insertReceipt.Parameters.AddWithValue(projection.MessageId);
            insertReceipt.Parameters.AddWithValue(payloadSha256);
            var inserted = await insertReceipt.ExecuteScalarAsync(cancellationToken);
            if (inserted is null)
            {
                await using var existingReceipt = new NpgsqlCommand("""
                    SELECT payload_sha256
                    FROM pitchmind.consumer_projection_inbox
                    WHERE consumer_id = $1 AND message_id = $2
                    FOR UPDATE
                    """, connection, transaction);
                existingReceipt.Parameters.AddWithValue(consumerId);
                existingReceipt.Parameters.AddWithValue(projection.MessageId);
                var existingHash = (string?)await existingReceipt.ExecuteScalarAsync(cancellationToken);
                if (!string.Equals(existingHash, payloadSha256, StringComparison.Ordinal))
                    throw new InvalidDataException("A consumer message ID was redelivered with a different payload.");

                await transaction.CommitAsync(cancellationToken);
                return false;
            }
        }

        var storedRevision = await ReadStoredRevisionAsync(connection, transaction, consumerId, projection.MatchId, cancellationToken);
        if (storedRevision is null)
        {
            await InsertSnapshotAsync(connection, transaction, consumerId, projection, cancellationToken);
        }
        else if (projection.CanonicalRevision > storedRevision.CanonicalRevision)
        {
            if (projection.CanonicalRevision != storedRevision.CanonicalRevision + 1)
                throw new InvalidDataException("Projection consumer received a canonical revision gap.");
            await UpdateSnapshotAsync(connection, transaction, consumerId, projection, cancellationToken);
        }
        else if (projection.CanonicalRevision == storedRevision.CanonicalRevision &&
                 !SnapshotMatches(storedRevision, projection))
        {
            throw new InvalidDataException("Conflicting projection payloads share one canonical revision.");
        }
        // Older revisions are recorded in the inbox but cannot replace the latest snapshot.

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task<StoredRevision?> ReadStoredRevisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string consumerId,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        await using var query = new NpgsqlCommand("""
            SELECT canonical_revision, projection_revision, sequence, clock_seconds,
                   algorithm_version, source_sha256, state_sha256
            FROM pitchmind.consumer_projection_snapshots
            WHERE consumer_id = $1 AND match_id = $2
            FOR UPDATE
            """, connection, transaction);
        query.Parameters.AddWithValue(consumerId);
        query.Parameters.AddWithValue(matchId);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new StoredRevision(reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3),
            reader.GetString(4), reader.GetString(5).TrimEnd(), reader.GetString(6).TrimEnd());
    }

    private static async Task InsertSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string consumerId,
        ConsumerProjectionSnapshot projection,
        CancellationToken cancellationToken)
    {
        await using var insert = new NpgsqlCommand("""
            INSERT INTO pitchmind.consumer_projection_snapshots
                (consumer_id, match_id, canonical_revision, projection_revision, sequence, clock_seconds,
                 algorithm_version, source_sha256, state_sha256, state_json)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10::jsonb)
            """, connection, transaction);
        AddSnapshotParameters(insert, consumerId, projection);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpdateSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string consumerId,
        ConsumerProjectionSnapshot projection,
        CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand("""
            UPDATE pitchmind.consumer_projection_snapshots
            SET canonical_revision = $3, projection_revision = $4, sequence = $5, clock_seconds = $6,
                algorithm_version = $7, source_sha256 = $8, state_sha256 = $9, state_json = $10::jsonb,
                updated_at = now()
            WHERE consumer_id = $1 AND match_id = $2
            """, connection, transaction);
        AddSnapshotParameters(update, consumerId, projection);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Consumer projection snapshot disappeared while locked.");
    }

    private static void AddSnapshotParameters(
        NpgsqlCommand command,
        string consumerId,
        ConsumerProjectionSnapshot projection)
    {
        command.Parameters.AddWithValue(consumerId);
        command.Parameters.AddWithValue(projection.MatchId);
        command.Parameters.AddWithValue(projection.CanonicalRevision);
        command.Parameters.AddWithValue(projection.ProjectionRevision);
        command.Parameters.AddWithValue(projection.Sequence);
        command.Parameters.AddWithValue(projection.ClockSeconds);
        command.Parameters.AddWithValue(projection.AlgorithmVersion);
        command.Parameters.AddWithValue(projection.SourceSha256);
        command.Parameters.AddWithValue(projection.StateSha256);
        command.Parameters.AddWithValue(projection.StateJson);
    }

    private static bool SnapshotMatches(StoredRevision stored, ConsumerProjectionSnapshot projection) =>
        stored.ProjectionRevision == projection.ProjectionRevision &&
        stored.Sequence == projection.Sequence &&
        stored.ClockSeconds == projection.ClockSeconds &&
        string.Equals(stored.AlgorithmVersion, projection.AlgorithmVersion, StringComparison.Ordinal) &&
        string.Equals(stored.SourceSha256, projection.SourceSha256, StringComparison.Ordinal) &&
        string.Equals(stored.StateSha256, projection.StateSha256, StringComparison.Ordinal);

    private static void ValidateProjection(Guid brokerMessageId, ConsumerProjectionSnapshot projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.MessageId != brokerMessageId || projection.MatchId == Guid.Empty ||
            projection.CanonicalRevision < 1 || projection.ProjectionRevision < 1 || projection.Sequence < 0 ||
            projection.ClockSeconds < 0 || string.IsNullOrWhiteSpace(projection.AlgorithmVersion) ||
            !IsSha256(projection.SourceSha256) || !IsSha256(projection.StateSha256) ||
            string.IsNullOrWhiteSpace(projection.StateJson) || projection.StateJson.Length > 65_536)
            throw new InvalidDataException("Projection snapshot metadata is invalid.");
        using var state = System.Text.Json.JsonDocument.Parse(projection.StateJson,
            new System.Text.Json.JsonDocumentOptions { MaxDepth = 16 });
        if (state.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
            throw new InvalidDataException("Projection snapshot state must be an object.");
    }

    private static bool IsSha256(string value) => value.Length == 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void ValidatePosition(
        string consumerId,
        string partitionId,
        string offset,
        long brokerSequenceNumber,
        Guid brokerMessageId,
        ReadOnlyMemory<byte> payload)
    {
        if (string.IsNullOrWhiteSpace(consumerId) || consumerId.Length > 128)
            throw new ArgumentException("Consumer ID must contain 1 to 128 characters.", nameof(consumerId));
        if (string.IsNullOrWhiteSpace(partitionId) || partitionId.Length > 128)
            throw new ArgumentException("Partition ID must contain 1 to 128 characters.", nameof(partitionId));
        if (string.IsNullOrWhiteSpace(offset) || offset.Length > 128)
            throw new ArgumentException("Event Hubs offset must contain 1 to 128 characters.", nameof(offset));
        if (brokerSequenceNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(brokerSequenceNumber));
        if (brokerMessageId == Guid.Empty)
            throw new ArgumentException("Event Hubs message ID is required.", nameof(brokerMessageId));
        if (payload.IsEmpty || payload.Length > 65_536)
            throw new InvalidDataException("Projection event payload is outside the supported size.");
    }

    private sealed record StoredRevision(
        long CanonicalRevision,
        int ProjectionRevision,
        long Sequence,
        int ClockSeconds,
        string AlgorithmVersion,
        string SourceSha256,
        string StateSha256);
}
