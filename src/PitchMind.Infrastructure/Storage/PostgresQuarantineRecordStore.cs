using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Append-only PostgreSQL storage for quarantine evidence; migrations are applied separately.</summary>
public sealed class PostgresQuarantineRecordStore(NpgsqlDataSource dataSource) : IQuarantineEvidenceWriter
{
    public Task AppendAsync(QuarantineEvidence evidence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return AppendAsync(new QuarantineRecord(evidence.RecordId, evidence.MatchId, evidence.SourcePointer,
            evidence.PayloadHash, evidence.Reason, evidence.ObservedAt), cancellationToken);
    }

    public async Task AppendAsync(QuarantineRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.RecordId == Guid.Empty) throw new ArgumentException("Record ID is required.", nameof(record));
        if (record.MatchId == Guid.Empty) throw new ArgumentException("Match ID must be null or non-empty.", nameof(record));
        if (string.IsNullOrWhiteSpace(record.SourcePointer) || record.SourcePointer.Length > 2048)
            throw new ArgumentException("Source pointer must contain 1 to 2048 characters.", nameof(record));
        if (record.PayloadHash is not null && !IsSha256Hex(record.PayloadHash))
            throw new ArgumentException("Payload hash must be a 64-character SHA-256 hex digest or null.", nameof(record));
        if (!IsReasonCode(record.Reason))
            throw new ArgumentException("Reason must be a stable uppercase reason code.", nameof(record));

        await using var command = dataSource.CreateCommand("""
            INSERT INTO pitchmind.quarantine(record_id, match_id, source_ptr, payload_hash, reason, observed_at, retry_status)
            VALUES ($1, $2, $3, $4, $5, $6, NULL)
            """);
        command.Parameters.AddWithValue(record.RecordId);
        command.Parameters.AddWithValue((object?)record.MatchId ?? DBNull.Value);
        command.Parameters.AddWithValue(record.SourcePointer);
        command.Parameters.AddWithValue((object?)record.PayloadHash ?? DBNull.Value);
        command.Parameters.AddWithValue(record.Reason);
        command.Parameters.AddWithValue(record.ObservedAt.ToUniversalTime());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<QuarantineRecord?> ReadAsync(Guid recordId, CancellationToken cancellationToken)
    {
        if (recordId == Guid.Empty) throw new ArgumentException("Record ID is required.", nameof(recordId));
        await using var command = dataSource.CreateCommand("""
            SELECT record_id, match_id, source_ptr, payload_hash, reason, observed_at
            FROM pitchmind.quarantine WHERE record_id = $1
            """);
        command.Parameters.AddWithValue(recordId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new QuarantineRecord(
            reader.GetGuid(0),
            reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3).TrimEnd(),
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5));
    }

    private static bool IsSha256Hex(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsReasonCode(string? value) =>
        value is { Length: > 0 and <= 100 } && value[0] is >= 'A' and <= 'Z' &&
        value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
}
