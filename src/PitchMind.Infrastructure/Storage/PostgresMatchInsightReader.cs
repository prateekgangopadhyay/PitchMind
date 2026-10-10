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
                   i.summary, m.source_version, m.provenance, p.source_sha256,
                   COALESCE(jsonb_agg(jsonb_build_object(
                       'kind', e.evidence_kind, 'id', e.evidence_id, 'relation', e.relation)
                       ORDER BY e.evidence_kind, e.evidence_id)
                       FILTER (WHERE e.insight_id IS NOT NULL), '[]'::jsonb)::text AS evidence
            FROM pitchmind.insights i
            JOIN pitchmind.matches m ON m.match_id = i.match_id
            JOIN pitchmind.match_projections p ON p.match_id = i.match_id AND p.revision = i.revision
            LEFT JOIN pitchmind.insight_evidence e ON e.insight_id = i.insight_id
            WHERE i.match_id = $1 AND i.revision <= $2 AND i.as_of_sequence <= $3
              AND ($4::bigint IS NULL OR i.as_of_sequence > $4 OR (i.as_of_sequence = $4 AND i.insight_id > $5))
              AND EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence valid
                  WHERE valid.insight_id = i.insight_id AND (
                      valid.evidence_kind = 'event' AND EXISTS (
                          SELECT 1 FROM pitchmind.match_events me
                          WHERE me.match_id = i.match_id AND me.event_id::text = valid.evidence_id
                            AND me.sequence <= $3 AND me.sequence <= i.as_of_sequence AND me.clock_seconds <= $6)
                      OR valid.evidence_kind = 'frame' AND EXISTS (
                          SELECT 1 FROM pitchmind.tracking_frames tf
                          WHERE tf.match_id = i.match_id AND tf.frame_id = valid.evidence_id
                            AND tf.sequence <= $3 AND tf.sequence <= i.as_of_sequence AND tf.clock_seconds <= $6))
              )
              AND NOT EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence unsupported
                  WHERE unsupported.insight_id = i.insight_id AND (
                      unsupported.evidence_kind NOT IN ('event', 'frame')
                      OR unsupported.evidence_kind = 'event' AND NOT EXISTS (
                          SELECT 1 FROM pitchmind.match_events me
                          WHERE me.match_id = i.match_id AND me.event_id::text = unsupported.evidence_id
                            AND me.sequence <= $3 AND me.sequence <= i.as_of_sequence AND me.clock_seconds <= $6)
                      OR unsupported.evidence_kind = 'frame' AND NOT EXISTS (
                          SELECT 1 FROM pitchmind.tracking_frames tf
                          WHERE tf.match_id = i.match_id AND tf.frame_id = unsupported.evidence_id
                            AND tf.sequence <= $3 AND tf.sequence <= i.as_of_sequence AND tf.clock_seconds <= $6))
              )
            GROUP BY i.insight_id, i.match_id, i.revision, i.as_of_sequence, i.detector_version, i.truth_class,
                     i.summary, m.source_version, m.provenance, p.source_sha256
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
        long throughSequence, int maximumEvidenceItems, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(insightId) || insightId.Length > 256)
            throw new ArgumentException("Insight ID must contain 1 to 256 characters.", nameof(insightId));
        Validate(matchId, maximumRevision, selectedSeconds, throughSequence, null, null, 1);
        if (maximumEvidenceItems is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumEvidenceItems));
        await using var command = dataSource.CreateCommand("""
            SELECT i.insight_id, i.match_id, i.revision, i.as_of_sequence, i.detector_version, i.truth_class,
                   i.summary, m.source_version, m.provenance, p.source_sha256,
                   COALESCE((
                       SELECT jsonb_agg(jsonb_build_object(
                           'kind', bounded.evidence_kind, 'id', bounded.evidence_id, 'relation', bounded.relation)
                           ORDER BY bounded.evidence_kind, bounded.evidence_id)
                       FROM (
                           SELECT evidence_kind, evidence_id, relation
                           FROM pitchmind.insight_evidence
                           WHERE insight_id = i.insight_id
                           ORDER BY evidence_kind, evidence_id
                           LIMIT $6
                       ) bounded
                   ), '[]'::jsonb)::text AS evidence
            FROM pitchmind.insights i
            JOIN pitchmind.matches m ON m.match_id = i.match_id
            JOIN pitchmind.match_projections p ON p.match_id = i.match_id AND p.revision = i.revision
            WHERE i.match_id = $1 AND i.insight_id = $2 AND i.revision <= $3 AND i.as_of_sequence <= $4
              AND (SELECT count(*) FROM pitchmind.insight_evidence all_refs WHERE all_refs.insight_id = i.insight_id) <= $6
              AND EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence valid
                  WHERE valid.insight_id = i.insight_id AND (
                      valid.evidence_kind = 'event' AND EXISTS (
                          SELECT 1 FROM pitchmind.match_events me
                          WHERE me.match_id = i.match_id AND me.event_id::text = valid.evidence_id
                            AND me.sequence <= $4 AND me.sequence <= i.as_of_sequence AND me.clock_seconds <= $5)
                      OR valid.evidence_kind = 'frame' AND EXISTS (
                          SELECT 1 FROM pitchmind.tracking_frames tf
                          WHERE tf.match_id = i.match_id AND tf.frame_id = valid.evidence_id
                            AND tf.sequence <= $4 AND tf.sequence <= i.as_of_sequence AND tf.clock_seconds <= $5))
              )
              AND NOT EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence unsupported
                  WHERE unsupported.insight_id = i.insight_id AND (
                      unsupported.evidence_kind NOT IN ('event', 'frame')
                      OR unsupported.evidence_kind = 'event' AND NOT EXISTS (
                          SELECT 1 FROM pitchmind.match_events me
                          WHERE me.match_id = i.match_id AND me.event_id::text = unsupported.evidence_id
                            AND me.sequence <= $4 AND me.sequence <= i.as_of_sequence AND me.clock_seconds <= $5)
                      OR unsupported.evidence_kind = 'frame' AND NOT EXISTS (
                          SELECT 1 FROM pitchmind.tracking_frames tf
                          WHERE tf.match_id = i.match_id AND tf.frame_id = unsupported.evidence_id
                            AND tf.sequence <= $4 AND tf.sequence <= i.as_of_sequence AND tf.clock_seconds <= $5))
              )

            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(insightId);
        command.Parameters.AddWithValue(maximumRevision);
        command.Parameters.AddWithValue(throughSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(maximumEvidenceItems);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInsight(reader) : null;
    }

    public async Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId, string insightId,
        int maximumRevision, int selectedSeconds, long throughSequence, int maximumItems, int maximumItemBytes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(insightId) || insightId.Length > 256)
            throw new ArgumentException("Insight ID must contain 1 to 256 characters.", nameof(insightId));
        Validate(matchId, maximumRevision, selectedSeconds, throughSequence, null, null, 1);
        if (maximumItems is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        if (maximumItemBytes is < 256 or > 65_536) throw new ArgumentOutOfRangeException(nameof(maximumItemBytes));
        await using var command = dataSource.CreateCommand("""
            WITH refs AS (
                SELECT i.insight_id, i.as_of_sequence, e.evidence_kind, e.evidence_id, e.relation,
                       (SELECT count(*) FROM pitchmind.insight_evidence all_refs
                        WHERE all_refs.insight_id = i.insight_id) AS total_count
                FROM pitchmind.insights i
                JOIN pitchmind.insight_evidence e ON e.insight_id = i.insight_id
                WHERE i.match_id = $1 AND i.insight_id = $2 AND i.revision <= $3 AND i.as_of_sequence <= $4
                ORDER BY e.evidence_kind, e.evidence_id
                LIMIT $6
            )
            SELECT refs.evidence_kind, refs.evidence_id, refs.relation, refs.total_count,
                   COALESCE(me.sequence, tf.sequence) AS evidence_sequence,
                   COALESCE(me.clock_seconds, tf.clock_seconds) AS evidence_clock,
                   COALESCE(me.period, tf.period) AS evidence_period,
                   COALESCE(me.payload::text, tf.payload::text) AS payload
            FROM refs
            LEFT JOIN pitchmind.match_events me ON refs.evidence_kind = 'event'
                AND me.match_id = $1 AND me.event_id::text = refs.evidence_id
            LEFT JOIN pitchmind.tracking_frames tf ON refs.evidence_kind = 'frame'
                AND tf.match_id = $1 AND tf.frame_id = refs.evidence_id
            WHERE (refs.evidence_kind = 'event' AND me.event_id IS NOT NULL
                       AND me.sequence <= $4 AND me.sequence <= refs.as_of_sequence AND me.clock_seconds <= $5
                   OR refs.evidence_kind = 'frame' AND tf.frame_id IS NOT NULL
                       AND tf.sequence <= $4 AND tf.sequence <= refs.as_of_sequence AND tf.clock_seconds <= $5)
              AND octet_length(COALESCE(me.payload::text, tf.payload::text)) <= $7
            ORDER BY refs.evidence_kind, refs.evidence_id
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(insightId);
        command.Parameters.AddWithValue(maximumRevision);
        command.Parameters.AddWithValue(throughSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(maximumItems + 1);
        command.Parameters.AddWithValue(maximumItemBytes);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<PersistedCanonicalEvidenceItem>();
        long? totalCount = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            totalCount = reader.GetInt64(3);
            items.Add(new PersistedCanonicalEvidenceItem(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt64(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7)));
        }
        if (items.Count == 0 || items.Count > maximumItems || totalCount != items.Count) return null;
        return Array.AsReadOnly(items.ToArray());
    }

    private static PersistedMatchInsight ReadInsight(NpgsqlDataReader reader)
    {
        var matchId = new MatchId(reader.GetGuid(1));
        var evidence = JsonSerializer.Deserialize<PersistedEvidenceReference[]>(reader.GetString(10),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Persisted insight evidence references are empty.");
        return new PersistedMatchInsight(reader.GetString(0), matchId, reader.GetInt32(2), reader.GetInt64(3),
            reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8),
            reader.GetString(9).Trim(), Array.AsReadOnly(evidence));
    }

    private static void Validate(MatchId matchId, int maximumRevision, int selectedSeconds, long throughSequence,
        long? afterSequence, string? afterInsightId, int limit)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        if (maximumRevision <= 0) throw new ArgumentOutOfRangeException(nameof(maximumRevision));
        if (selectedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(selectedSeconds));
        if (throughSequence < -1) throw new ArgumentOutOfRangeException(nameof(throughSequence));
        if (afterSequence is < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (afterSequence is not null && afterSequence > throughSequence) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (afterSequence is null && afterInsightId is not null || afterSequence is not null && string.IsNullOrWhiteSpace(afterInsightId))
            throw new ArgumentException("Insight cursors require both afterSequence and afterInsightId.");
        if (afterInsightId is { Length: > 256 }) throw new ArgumentOutOfRangeException(nameof(afterInsightId));
        if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit));
    }
}
