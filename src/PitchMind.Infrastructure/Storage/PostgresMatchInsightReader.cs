using System.Text.Json;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Reads persisted insights only when their revision and source sequence are visible.</summary>
public sealed class PostgresMatchInsightReader(NpgsqlDataSource dataSource) : IMatchInsightReader
{
    private const int MaximumPageSize = 100;

    public async Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, int maximumRevision, int selectedSeconds, long throughSequence,
        long? afterSequence, string? afterInsightId, int limit, CancellationToken cancellationToken)
    {
        Validate(matchId, maximumRevision, selectedSeconds, throughSequence, afterSequence, afterInsightId, limit);
        await using var command = dataSource.CreateCommand("""
            SELECT i.insight_id, i.match_id, i.revision, i.as_of_sequence, i.detector_version, i.truth_class,
                   i.summary,
                   COALESCE(jsonb_agg(jsonb_build_object(
                       'kind', e.evidence_kind, 'id', e.evidence_id, 'relation', e.relation)
                       ORDER BY e.evidence_kind, e.evidence_id)
                       FILTER (WHERE e.insight_id IS NOT NULL), '[]'::jsonb)::text AS evidence
            FROM pitchmind.insights i
            LEFT JOIN pitchmind.insight_evidence e ON e.insight_id = i.insight_id
            WHERE i.match_id = $1 AND i.revision <= $2 AND i.as_of_sequence <= $3
              AND ($4::bigint IS NULL OR i.as_of_sequence > $4 OR (i.as_of_sequence = $4 AND i.insight_id > $5))
              AND EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence valid
                  WHERE valid.insight_id = i.insight_id AND (
                      valid.evidence_kind = 'event' AND EXISTS (
                          SELECT 1 FROM pitchmind.match_events me
                          WHERE me.match_id = i.match_id AND me.event_id::text = valid.evidence_id
                            AND me.sequence <= $3 AND me.clock_seconds <= $6)
                      OR valid.evidence_kind = 'frame' AND EXISTS (
                          SELECT 1 FROM pitchmind.tracking_frames tf
                          WHERE tf.match_id = i.match_id AND tf.frame_id = valid.evidence_id
                            AND tf.clock_seconds <= $6))
              )
              AND NOT EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence unsupported
                  WHERE unsupported.insight_id = i.insight_id AND (
                      unsupported.evidence_kind NOT IN ('event', 'frame')
                      OR unsupported.evidence_kind = 'event' AND NOT EXISTS (
                          SELECT 1 FROM pitchmind.match_events me
                          WHERE me.match_id = i.match_id AND me.event_id::text = unsupported.evidence_id
                            AND me.sequence <= $3 AND me.clock_seconds <= $6)
                      OR unsupported.evidence_kind = 'frame' AND NOT EXISTS (
                          SELECT 1 FROM pitchmind.tracking_frames tf
                          WHERE tf.match_id = i.match_id AND tf.frame_id = unsupported.evidence_id
                            AND tf.clock_seconds <= $6))
              )
            GROUP BY i.insight_id, i.match_id, i.revision, i.as_of_sequence, i.detector_version, i.truth_class, i.summary
            ORDER BY i.as_of_sequence, i.insight_id
            LIMIT $7
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(maximumRevision);
        command.Parameters.AddWithValue(throughSequence);
        command.Parameters.AddWithValue((object?)afterSequence ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)afterInsightId ?? DBNull.Value);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(limit + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<PersistedMatchInsight>(limit + 1);
        while (await reader.ReadAsync(cancellationToken)) results.Add(ReadInsight(reader));
        var hasMore = results.Count > limit;
        if (hasMore) results.RemoveAt(results.Count - 1);
        var last = results.LastOrDefault();
        return new PersistedMatchInsightPage(results, hasMore ? last?.AsOfSequence : null,
            hasMore ? last?.InsightId : null, hasMore);
    }

    public async Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId, int maximumRevision, int selectedSeconds,
        long throughSequence, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(insightId) || insightId.Length > 256)
            throw new ArgumentException("Insight ID must contain 1 to 256 characters.", nameof(insightId));
        Validate(matchId, maximumRevision, selectedSeconds, throughSequence, null, null, 1);
        await using var command = dataSource.CreateCommand("""
            SELECT i.insight_id, i.match_id, i.revision, i.as_of_sequence, i.detector_version, i.truth_class,
                   i.summary,
                   COALESCE(jsonb_agg(jsonb_build_object(
                       'kind', e.evidence_kind, 'id', e.evidence_id, 'relation', e.relation)
                       ORDER BY e.evidence_kind, e.evidence_id)
                       FILTER (WHERE e.insight_id IS NOT NULL), '[]'::jsonb)::text AS evidence
            FROM pitchmind.insights i
            LEFT JOIN pitchmind.insight_evidence e ON e.insight_id = i.insight_id
            WHERE i.match_id = $1 AND i.insight_id = $2 AND i.revision <= $3 AND i.as_of_sequence <= $4
              AND EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence valid
                  WHERE valid.insight_id = i.insight_id AND (
                      valid.evidence_kind = 'event' AND EXISTS (
                          SELECT 1 FROM pitchmind.match_events me
                          WHERE me.match_id = i.match_id AND me.event_id::text = valid.evidence_id
                            AND me.sequence <= $4 AND me.clock_seconds <= $5)
                      OR valid.evidence_kind = 'frame' AND EXISTS (
                          SELECT 1 FROM pitchmind.tracking_frames tf
                          WHERE tf.match_id = i.match_id AND tf.frame_id = valid.evidence_id
                            AND tf.clock_seconds <= $5))
              )
              AND NOT EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence unsupported
                  WHERE unsupported.insight_id = i.insight_id AND (
                      unsupported.evidence_kind NOT IN ('event', 'frame')
                      OR unsupported.evidence_kind = 'event' AND NOT EXISTS (
                          SELECT 1 FROM pitchmind.match_events me
                          WHERE me.match_id = i.match_id AND me.event_id::text = unsupported.evidence_id
                            AND me.sequence <= $4 AND me.clock_seconds <= $5)
                      OR unsupported.evidence_kind = 'frame' AND NOT EXISTS (
                          SELECT 1 FROM pitchmind.tracking_frames tf
                          WHERE tf.match_id = i.match_id AND tf.frame_id = unsupported.evidence_id
                            AND tf.clock_seconds <= $5))
              )
            GROUP BY i.insight_id, i.match_id, i.revision, i.as_of_sequence, i.detector_version, i.truth_class, i.summary
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(insightId);
        command.Parameters.AddWithValue(maximumRevision);
        command.Parameters.AddWithValue(throughSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInsight(reader) : null;
    }

    private static PersistedMatchInsight ReadInsight(NpgsqlDataReader reader)
    {
        var matchId = new MatchId(reader.GetGuid(1));
        var evidence = JsonSerializer.Deserialize<PersistedEvidenceReference[]>(reader.GetString(7),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Persisted insight evidence references are empty.");
        return new PersistedMatchInsight(reader.GetString(0), matchId, reader.GetInt32(2), reader.GetInt64(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), Array.AsReadOnly(evidence));
    }

    private static void Validate(MatchId matchId, int maximumRevision, int selectedSeconds, long throughSequence,
        long? afterSequence, string? afterInsightId, int limit)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        if (maximumRevision <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRevision));
        if (selectedSeconds is < 0 or > 5400) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (throughSequence < -1) throw new ArgumentOutOfRangeException(nameof(throughSequence));
        if (afterSequence is < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (afterSequence is not null && afterSequence > throughSequence) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (afterSequence is null && afterInsightId is not null || afterSequence is not null && string.IsNullOrWhiteSpace(afterInsightId))
            throw new ArgumentException("Insight cursors require both afterSequence and afterInsightId.");
        if (afterInsightId is { Length: > 256 }) throw new ArgumentOutOfRangeException(nameof(afterInsightId));
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
