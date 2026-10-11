using Npgsql;
using NpgsqlTypes;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Reads observed tracking samples at the exact selected second and projection watermark.</summary>
public sealed class PostgresMatchTrackingReader(NpgsqlDataSource dataSource) : IMatchTrackingReader
{
    private const int MaximumPageSize = 100;

    public async Task<PersistedTrackingFramePage> ReadAtAsync(SelectedMatchProjection projection,
        int selectedSeconds, long? afterSequence, string? afterFrameId, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > 5400) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (projection.Revision <= 0 || projection.AsOfSequence < -1 || projection.AsOfClockSeconds > selectedSeconds)
            throw new ArgumentException("Projection must be valid and visible at the selected time.", nameof(projection));
        if (projection.SourceSha256.Length != 64 || projection.SourceSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException("Persisted projection has an invalid source hash.");
        if (!Guid.TryParse(projection.State.MatchId, out var parsedMatchId) || parsedMatchId == Guid.Empty)
            throw new InvalidDataException("Persisted projection has an invalid match ID.");
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
        if ((afterSequence is null) != (afterFrameId is null) || afterSequence is < 0 ||
            afterFrameId is { Length: 0 or > 128 })
            throw new ArgumentException("Frame cursor key must contain a valid sequence and frame ID.");

        await using var command = dataSource.CreateCommand("""
            SELECT frame_id, sequence, canonical_revision, period, clock_seconds, coordinate_version, quality::text,
                   source_sha256, payload::text, octet_length(payload::text)
            FROM pitchmind.tracking_frames
            WHERE match_id = $1 AND sequence <= $2 AND clock_seconds = $3
              AND canonical_revision IS NOT NULL AND canonical_revision <= $4 AND source_sha256 = $5
              AND ($6 IS NULL OR sequence > $6 OR (sequence = $6 AND frame_id > $7))
            ORDER BY sequence, frame_id
            LIMIT $8
            """);
        command.Parameters.AddWithValue(parsedMatchId);
        command.Parameters.AddWithValue(projection.AsOfSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(projection.Revision);
        command.Parameters.AddWithValue(projection.SourceSha256);
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bigint, Value = (object?)afterSequence ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)afterFrameId ?? DBNull.Value });
        command.Parameters.AddWithValue(limit + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var frames = new List<PersistedTrackingFrame>(limit + 1);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetInt32(9) > 32_768)
                throw new InvalidDataException("Persisted tracking frame exceeds the response size bound.");
            frames.Add(new PersistedTrackingFrame(reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6),
                reader.GetString(7).Trim(), reader.GetString(8)));
        }
        var hasMore = frames.Count > limit;
        if (hasMore) frames.RemoveAt(frames.Count - 1);
        var last = hasMore ? frames.LastOrDefault() : null;
        return new PersistedTrackingFramePage(Array.AsReadOnly(frames.ToArray()), last?.Sequence, last?.FrameId, hasMore);
    }
}
