using Npgsql;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Reads observed tracking samples at the exact selected second and projection watermark.</summary>
public sealed class PostgresMatchTrackingReader(NpgsqlDataSource dataSource) : IMatchTrackingReader
{
    private const int MaximumPageSize = 100;

    public async Task<PersistedTrackingFramePage> ReadAtAsync(PersistedMatchProjection projection,
        int selectedSeconds, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projection);
        if (selectedSeconds is < 0 or > 5400) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (projection.Revision <= 0 || projection.AsOfSequence < -1 || projection.AsOfClockSeconds > selectedSeconds)
            throw new ArgumentException("Projection must be valid and visible at the selected time.", nameof(projection));
        if (!Guid.TryParse(projection.State.MatchId, out var parsedMatchId) || parsedMatchId == Guid.Empty)
            throw new InvalidDataException("Persisted projection has an invalid match ID.");
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));

        await using var command = dataSource.CreateCommand("""
            SELECT frame_id, sequence, period, clock_seconds, coordinate_version, quality::text,
                   source_sha256, payload::text, octet_length(payload::text)
            FROM pitchmind.tracking_frames
            WHERE match_id = $1 AND sequence <= $2 AND clock_seconds = $3
            ORDER BY sequence, frame_id
            LIMIT $4
            """);
        command.Parameters.AddWithValue(parsedMatchId);
        command.Parameters.AddWithValue(projection.AsOfSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(limit + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var frames = new List<PersistedTrackingFrame>(limit + 1);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.GetInt32(8) > 32_768)
                throw new InvalidDataException("Persisted tracking frame exceeds the response size bound.");
            frames.Add(new PersistedTrackingFrame(reader.GetString(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7)));
        }
        var hasMore = frames.Count > limit;
        if (hasMore) frames.RemoveAt(frames.Count - 1);
        return new PersistedTrackingFramePage(Array.AsReadOnly(frames.ToArray()), hasMore);
    }
}
