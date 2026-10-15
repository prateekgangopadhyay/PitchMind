using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Reads persisted tracking frames at the exact selected second and canonical source revision.</summary>
public sealed class PostgresMatchTrackingReader(NpgsqlDataSource dataSource) : IMatchTrackingReader
{
    private const int MaximumPageSize = 25;
    private const int MaximumPayloadBytes = 32_768;

    public async Task<PersistedTrackingFramePage> ReadAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, long? afterSequence, string? afterFrameId, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > 5400) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (projection.CanonicalRevision <= 0 || projection.ProjectionRevision <= 0 || projection.AsOfSequence < -1 ||
            projection.AsOfClockSeconds > selectedSeconds)
            throw new ArgumentException("Projection must be valid and visible at the selected time.", nameof(projection));
        if (!IsSha256(projection.SourceSha256))
            throw new InvalidDataException("Persisted projection has an invalid source hash.");
        if (!Guid.TryParse(projection.State.MatchId, out var matchId) || matchId == Guid.Empty)
            throw new InvalidDataException("Persisted projection has an invalid match ID.");
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
        if ((afterSequence is null) != (afterFrameId is null) || afterSequence is < 0 ||
            afterFrameId is { Length: 0 or > 128 })
            throw new ArgumentException("Frame cursor key must contain a valid sequence and frame ID.");

        await using var command = dataSource.CreateCommand("""
            SELECT frame_id, sequence, canonical_revision, period, clock_seconds, coordinate_version,
                   CASE WHEN octet_length(quality::text) <= 4096 THEN quality::text ELSE NULL END,
                   source_sha256,
                   CASE WHEN octet_length(payload::text) <= 32768 THEN payload::text ELSE NULL END,
                   octet_length(payload::text), octet_length(quality::text)
            FROM pitchmind.tracking_frames
            WHERE match_id = $1 AND sequence <= $2 AND clock_seconds = $3
              AND canonical_revision IS NOT NULL AND canonical_revision <= $4 AND source_sha256 = $5
              AND ($6 IS NULL OR sequence > $6 OR (sequence = $6 AND frame_id > $7))
            ORDER BY sequence, frame_id
            LIMIT $8
            """);
        command.Parameters.AddWithValue(matchId);
        command.Parameters.AddWithValue(projection.AsOfSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(projection.CanonicalRevision);
        command.Parameters.AddWithValue(projection.SourceSha256);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)afterSequence ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)afterFrameId ?? DBNull.Value });
        command.Parameters.AddWithValue(limit + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var frames = new List<PersistedTrackingFrame>(limit + 1);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetInt32(9) > MaximumPayloadBytes || reader.GetInt32(10) > 4096)
                throw new InvalidDataException("Persisted tracking frame exceeds the response size bound.");
            var frame = new PersistedTrackingFrame(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7).Trim(), reader.GetString(8));
            ValidatePayload(frame, matchId, projection.SourceSha256);
            frames.Add(frame);
        }

        var hasMore = frames.Count > limit;
        if (hasMore) frames.RemoveAt(frames.Count - 1);
        var last = hasMore ? frames.LastOrDefault() : null;
        return new PersistedTrackingFramePage(Array.AsReadOnly(frames.ToArray()), last?.Sequence, last?.FrameId, hasMore);
    }

    public async Task<PersistedTrackingFrame?> ReadByIdAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, string frameId, long? exactCanonicalRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > 5400) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (string.IsNullOrWhiteSpace(frameId) || frameId.Length > 128)
            throw new ArgumentException("Frame ID must contain between 1 and 128 non-whitespace characters.", nameof(frameId));
        if (projection.CanonicalRevision <= 0 || projection.ProjectionRevision <= 0 || projection.AsOfSequence < -1 ||
            projection.AsOfClockSeconds > selectedSeconds)
            throw new ArgumentException("Projection must be valid and visible at the selected time.", nameof(projection));
        if (exactCanonicalRevision is <= 0 || exactCanonicalRevision > projection.CanonicalRevision)
            throw new ArgumentOutOfRangeException(nameof(exactCanonicalRevision));
        if (!IsSha256(projection.SourceSha256))
            throw new InvalidDataException("Persisted projection has an invalid source hash.");
        if (!Guid.TryParse(projection.State.MatchId, out var matchId) || matchId == Guid.Empty)
            throw new InvalidDataException("Persisted projection has an invalid match ID.");

        await using var command = dataSource.CreateCommand("""
            SELECT frame_id, sequence, canonical_revision, period, clock_seconds, coordinate_version,
                   CASE WHEN octet_length(quality::text) <= 4096 THEN quality::text ELSE NULL END,
                   source_sha256,
                   CASE WHEN octet_length(payload::text) <= 32768 THEN payload::text ELSE NULL END,
                   octet_length(payload::text), octet_length(quality::text)
            FROM pitchmind.tracking_frames
            WHERE match_id = $1 AND frame_id = $2 AND sequence <= $3 AND clock_seconds = $4
              AND canonical_revision IS NOT NULL AND canonical_revision <= $5 AND source_sha256 = $6
              AND ($7 IS NULL OR canonical_revision = $7)
            ORDER BY canonical_revision DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue(matchId);
        command.Parameters.AddWithValue(frameId);
        command.Parameters.AddWithValue(projection.AsOfSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(projection.CanonicalRevision);
        command.Parameters.AddWithValue(projection.SourceSha256);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint,
            Value = (object?)exactCanonicalRevision ?? DBNull.Value });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (reader.GetInt32(9) > MaximumPayloadBytes || reader.GetInt32(10) > 4096)
            throw new InvalidDataException("Persisted tracking frame exceeds the response size bound.");
        var frame = new PersistedTrackingFrame(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
            reader.GetString(7).Trim(), reader.GetString(8));
        ValidatePayload(frame, matchId, projection.SourceSha256);
        return frame;
    }

    private static void ValidatePayload(PersistedTrackingFrame indexed, Guid matchId, string sourceSha256)
    {
        try
        {
            using var document = JsonDocument.Parse(indexed.PayloadJson);
            using var qualityDocument = JsonDocument.Parse(indexed.QualityJson);
            var root = document.RootElement;
            var quality = qualityDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object || quality.ValueKind != JsonValueKind.Object ||
                !TryProperty(root, "matchId", out var match) || match.GetString() != matchId.ToString("D") ||
                !TryProperty(root, "frameId", out var frameId) || frameId.GetString() != indexed.FrameId ||
                !TryProperty(root, "sequence", out var sequence) || sequence.GetInt64() != indexed.Sequence ||
                !TryProperty(root, "canonicalRevision", out var canonicalRevision) || canonicalRevision.GetInt64() != indexed.CanonicalRevision ||
                !TryProperty(root, "matchTime", out var matchTime) ||
                !TryProperty(matchTime, "elapsedSeconds", out var elapsed) || elapsed.GetInt64() != indexed.ClockSeconds ||
                !TryProperty(matchTime, "period", out var period) || period.GetInt32() != indexed.Period ||
                !TryProperty(root, "source", out var source) ||
                !TryProperty(source, "sourceSha256", out var sourceHash) || sourceHash.GetString() != sourceSha256)
                throw new InvalidDataException("Persisted tracking frame payload does not match its indexed identity.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("Persisted tracking frame payload or quality metadata is invalid.", exception);
        }
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    private static bool IsSha256(string value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
