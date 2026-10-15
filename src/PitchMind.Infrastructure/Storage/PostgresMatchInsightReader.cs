using System.Text.Json;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Reads only persisted insights whose revision-specific evidence is visible at the selected time.</summary>
public sealed class PostgresMatchInsightReader(NpgsqlDataSource dataSource) : IMatchInsightReader
{
    private const int MaximumPageSize = 100;
    private const int MaximumSummaryBytes = 6_144;

    public async Task<PersistedMatchInsightPage> ReadAtAsync(MatchId matchId, long maximumCanonicalRevision,
        int maximumProjectionRevision, int selectedSeconds, long throughSequence, long? afterSequence,
        string? afterInsightId, int limit, CancellationToken cancellationToken)
    {
        Validate(matchId, maximumCanonicalRevision, maximumProjectionRevision, selectedSeconds, throughSequence,
            afterSequence, afterInsightId, limit);
        await using var command = dataSource.CreateCommand("""
            SELECT i.canonical_revision, i.insight_id, i.match_id, i.revision, i.as_of_sequence,
                   i.detector_version, i.truth_class, i.summary, i.conditions_json::text, m.source_version, m.provenance, p.source_sha256,
                   COALESCE(jsonb_agg(jsonb_build_object(
                       'kind', e.evidence_kind, 'id', e.evidence_id, 'relation', e.relation)
                       ORDER BY e.evidence_kind, e.evidence_id)
                       FILTER (WHERE e.insight_id IS NOT NULL), '[]'::jsonb)::text AS evidence
            FROM pitchmind.insights i
            JOIN pitchmind.matches m ON m.match_id = i.match_id
            JOIN pitchmind.match_projections p ON p.match_id = i.match_id
                AND p.canonical_revision = i.canonical_revision AND p.revision = i.revision
                AND p.source_sha256 = m.source_sha256
            LEFT JOIN pitchmind.insight_evidence e ON e.insight_id = i.insight_id
                AND e.canonical_revision = i.canonical_revision
            WHERE i.match_id = $1 AND i.canonical_revision <= $2 AND i.revision <= $3
              AND i.as_of_sequence <= $4
              AND ($5::bigint IS NULL OR i.as_of_sequence > $5 OR (i.as_of_sequence = $5 AND i.insight_id > $6))
              AND octet_length(i.summary) <= $8
              AND EXISTS (SELECT 1 FROM pitchmind.insight_evidence any_ref
                          WHERE any_ref.insight_id = i.insight_id AND any_ref.canonical_revision = i.canonical_revision)
              AND NOT EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence e_invalid
                  WHERE e_invalid.insight_id = i.insight_id AND e_invalid.canonical_revision = i.canonical_revision
                    AND (e_invalid.evidence_kind NOT IN ('event', 'frame') OR
                         (e_invalid.evidence_kind = 'event' AND NOT EXISTS (
                             SELECT 1 FROM pitchmind.match_events me WHERE me.match_id = i.match_id
                               AND me.event_id = e_invalid.event_id
                               AND me.canonical_revision = e_invalid.evidence_canonical_revision
                               AND e_invalid.evidence_canonical_revision <= i.canonical_revision
                               AND e_invalid.evidence_canonical_revision <= $2
                               AND me.sequence <= $4 AND me.sequence <= i.as_of_sequence AND me.clock_seconds <= $7
                               AND me.payload #>> '{source,rightsReviewStatus}' = 'approved_for_use')) OR
                         (e_invalid.evidence_kind = 'frame' AND NOT EXISTS (
                             SELECT 1 FROM pitchmind.tracking_frames tf WHERE tf.match_id = i.match_id
                               AND tf.frame_id = e_invalid.frame_id
                               AND tf.canonical_revision = e_invalid.evidence_canonical_revision
                               AND e_invalid.evidence_canonical_revision <= i.canonical_revision
                               AND e_invalid.evidence_canonical_revision <= $2
                               AND tf.source_sha256 = p.source_sha256
                               AND tf.sequence <= $4 AND tf.sequence <= i.as_of_sequence AND tf.clock_seconds <= $7
                               AND tf.payload #>> '{source,rightsReviewStatus}' = 'approved_for_use')))
              )
            GROUP BY i.canonical_revision, i.insight_id, i.match_id, i.revision, i.as_of_sequence,
                     i.detector_version, i.truth_class, i.summary, i.conditions_json, m.source_version, m.provenance, p.source_sha256
            ORDER BY i.as_of_sequence, i.insight_id
            LIMIT $9
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(maximumCanonicalRevision);
        command.Parameters.AddWithValue(maximumProjectionRevision);
        command.Parameters.AddWithValue(throughSequence);
        command.Parameters.AddWithValue((object?)afterSequence ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)afterInsightId ?? DBNull.Value);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(MaximumSummaryBytes);
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

    public async Task<PersistedMatchInsight?> GetAtAsync(MatchId matchId, string insightId,
        long maximumCanonicalRevision, int maximumProjectionRevision, int selectedSeconds, long throughSequence,
        CancellationToken cancellationToken, int maximumEvidenceItems = 100)
    {
        if (string.IsNullOrWhiteSpace(insightId) || insightId.Length > 256)
            throw new ArgumentException("Insight ID must contain 1 to 256 characters.", nameof(insightId));
        Validate(matchId, maximumCanonicalRevision, maximumProjectionRevision, selectedSeconds, throughSequence,
            null, null, 1);
        if (maximumEvidenceItems is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumEvidenceItems));
        await using var command = dataSource.CreateCommand("""
            SELECT i.canonical_revision, i.insight_id, i.match_id, i.revision, i.as_of_sequence,
                   i.detector_version, i.truth_class, i.summary, i.conditions_json::text, m.source_version, m.provenance, p.source_sha256,
                   COALESCE(jsonb_agg(jsonb_build_object(
                       'kind', e.evidence_kind, 'id', e.evidence_id, 'relation', e.relation)
                       ORDER BY e.evidence_kind, e.evidence_id)
                       FILTER (WHERE e.insight_id IS NOT NULL), '[]'::jsonb)::text AS evidence
            FROM pitchmind.insights i
            JOIN pitchmind.matches m ON m.match_id = i.match_id
            JOIN pitchmind.match_projections p ON p.match_id = i.match_id
                AND p.canonical_revision = i.canonical_revision AND p.revision = i.revision
                AND p.source_sha256 = m.source_sha256
            LEFT JOIN pitchmind.insight_evidence e ON e.insight_id = i.insight_id
                AND e.canonical_revision = i.canonical_revision
            WHERE i.match_id = $1 AND i.insight_id = $2
              AND i.canonical_revision <= $3 AND i.revision <= $4 AND i.as_of_sequence <= $5
              AND octet_length(i.summary) <= $8
              AND (SELECT count(*) FROM pitchmind.insight_evidence any_ref
                   WHERE any_ref.insight_id = i.insight_id AND any_ref.canonical_revision = i.canonical_revision)
                   BETWEEN 1 AND $7
              AND NOT EXISTS (
                  SELECT 1 FROM pitchmind.insight_evidence e_invalid
                  WHERE e_invalid.insight_id = i.insight_id AND e_invalid.canonical_revision = i.canonical_revision
                    AND (e_invalid.evidence_kind NOT IN ('event', 'frame') OR
                         (e_invalid.evidence_kind = 'event' AND NOT EXISTS (
                             SELECT 1 FROM pitchmind.match_events me WHERE me.match_id = i.match_id
                               AND me.event_id = e_invalid.event_id
                               AND me.canonical_revision = e_invalid.evidence_canonical_revision
                               AND e_invalid.evidence_canonical_revision <= i.canonical_revision
                               AND e_invalid.evidence_canonical_revision <= $3
                               AND me.sequence <= $5 AND me.sequence <= i.as_of_sequence AND me.clock_seconds <= $6
                               AND me.payload #>> '{source,rightsReviewStatus}' = 'approved_for_use')) OR
                         (e_invalid.evidence_kind = 'frame' AND NOT EXISTS (
                             SELECT 1 FROM pitchmind.tracking_frames tf WHERE tf.match_id = i.match_id
                               AND tf.frame_id = e_invalid.frame_id
                               AND tf.canonical_revision = e_invalid.evidence_canonical_revision
                               AND e_invalid.evidence_canonical_revision <= i.canonical_revision
                               AND e_invalid.evidence_canonical_revision <= $3
                               AND tf.source_sha256 = p.source_sha256
                               AND tf.sequence <= $5 AND tf.sequence <= i.as_of_sequence AND tf.clock_seconds <= $6
                               AND tf.payload #>> '{source,rightsReviewStatus}' = 'approved_for_use')))
              )
            GROUP BY i.canonical_revision, i.insight_id, i.match_id, i.revision, i.as_of_sequence,
                     i.detector_version, i.truth_class, i.summary, i.conditions_json, m.source_version, m.provenance, p.source_sha256
            LIMIT 1
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(insightId);
        command.Parameters.AddWithValue(maximumCanonicalRevision);
        command.Parameters.AddWithValue(maximumProjectionRevision);
        command.Parameters.AddWithValue(throughSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(maximumEvidenceItems);
        command.Parameters.AddWithValue(MaximumSummaryBytes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInsight(reader) : null;
    }

    public async Task<IReadOnlyList<PersistedCanonicalEvidenceItem>?> ReadEvidenceAtAsync(MatchId matchId,
        string insightId, long maximumCanonicalRevision, int maximumProjectionRevision, int selectedSeconds,
        long throughSequence, CancellationToken cancellationToken, int maximumItems = 100, int maximumItemBytes = 65_536)
    {
        if (string.IsNullOrWhiteSpace(insightId) || insightId.Length > 256)
            throw new ArgumentException("Insight ID must contain 1 to 256 characters.", nameof(insightId));
        Validate(matchId, maximumCanonicalRevision, maximumProjectionRevision, selectedSeconds, throughSequence,
            null, null, 1);
        if (maximumItems is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        if (maximumItemBytes is < 256 or > 65_536) throw new ArgumentOutOfRangeException(nameof(maximumItemBytes));
        await using var command = dataSource.CreateCommand("""
            WITH target AS (
                SELECT i.insight_id, i.canonical_revision, i.as_of_sequence, p.source_sha256
                FROM pitchmind.insights i
                JOIN pitchmind.matches m ON m.match_id = i.match_id
                JOIN pitchmind.match_projections p ON p.match_id = i.match_id
                    AND p.canonical_revision = i.canonical_revision AND p.revision = i.revision
                    AND p.source_sha256 = m.source_sha256
                WHERE i.match_id = $1 AND i.insight_id = $2
                  AND i.canonical_revision <= $3 AND i.revision <= $4 AND i.as_of_sequence <= $5
            ), refs AS (
                SELECT target.insight_id, target.canonical_revision, target.as_of_sequence, target.source_sha256,
                       e.evidence_canonical_revision,
                       e.match_id, e.evidence_kind, e.evidence_id, e.relation,
                       (SELECT count(*) FROM pitchmind.insight_evidence all_refs
                        WHERE all_refs.insight_id = target.insight_id
                          AND all_refs.canonical_revision = target.canonical_revision) AS total_count
                FROM target
                JOIN pitchmind.insight_evidence e ON e.insight_id = target.insight_id
                    AND e.canonical_revision = target.canonical_revision
            )
            SELECT refs.evidence_kind, refs.evidence_id, refs.relation, refs.evidence_canonical_revision, refs.total_count,
                   COALESCE(me.sequence, tf.sequence) AS evidence_sequence,
                   COALESCE(me.clock_seconds, tf.clock_seconds) AS evidence_clock,
                   COALESCE(me.period, tf.period) AS evidence_period,
                   COALESCE(me.payload::text, tf.payload::text) AS payload
            FROM refs
            LEFT JOIN pitchmind.match_events me ON refs.evidence_kind = 'event'
                AND me.match_id = refs.match_id AND me.event_id::text = refs.evidence_id
                AND me.canonical_revision = refs.evidence_canonical_revision
                AND me.payload #>> '{source,rightsReviewStatus}' = 'approved_for_use'
            LEFT JOIN pitchmind.tracking_frames tf ON refs.evidence_kind = 'frame'
                AND tf.match_id = refs.match_id AND tf.frame_id = refs.evidence_id
                AND tf.canonical_revision = refs.evidence_canonical_revision AND tf.source_sha256 = refs.source_sha256
                AND tf.payload #>> '{source,rightsReviewStatus}' = 'approved_for_use'
            WHERE (refs.evidence_kind = 'event' AND me.event_id IS NOT NULL
                       AND refs.evidence_canonical_revision <= refs.canonical_revision
                       AND refs.evidence_canonical_revision <= $3
                       AND me.sequence <= $5 AND me.sequence <= refs.as_of_sequence AND me.clock_seconds <= $6
                   OR refs.evidence_kind = 'frame' AND tf.frame_id IS NOT NULL
                       AND refs.evidence_canonical_revision <= refs.canonical_revision
                       AND refs.evidence_canonical_revision <= $3
                       AND tf.sequence <= $5 AND tf.sequence <= refs.as_of_sequence AND tf.clock_seconds <= $6)
            AND octet_length(COALESCE(me.payload::text, tf.payload::text)) <= $8
            ORDER BY refs.evidence_kind, refs.evidence_id
            LIMIT $7
            """);
        command.Parameters.AddWithValue(matchId.Value);
        command.Parameters.AddWithValue(insightId);
        command.Parameters.AddWithValue(maximumCanonicalRevision);
        command.Parameters.AddWithValue(maximumProjectionRevision);
        command.Parameters.AddWithValue(throughSequence);
        command.Parameters.AddWithValue(selectedSeconds);
        command.Parameters.AddWithValue(maximumItems + 1);
        command.Parameters.AddWithValue(maximumItemBytes);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<PersistedCanonicalEvidenceItem>();
        long? totalCount = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            totalCount = reader.GetInt64(4);
            items.Add(new PersistedCanonicalEvidenceItem(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetInt64(3), reader.GetInt64(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8)));
        }
        if (items.Count == 0 || items.Count > maximumItems || totalCount != items.Count) return null;
        return Array.AsReadOnly(items.ToArray());
    }

    private static PersistedMatchInsight ReadInsight(NpgsqlDataReader reader)
    {
        var conditionsJson = reader.GetString(8);
        using (var conditions = JsonDocument.Parse(conditionsJson))
        {
            if (conditions.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Persisted insight conditions must be a JSON object.");
        }
        var evidence = JsonSerializer.Deserialize<PersistedEvidenceReference[]>(reader.GetString(12),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Persisted insight evidence references are empty.");
        return new PersistedMatchInsight(reader.GetString(1), new MatchId(reader.GetGuid(2)), reader.GetInt64(0),
            reader.GetInt32(3), reader.GetInt64(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
            conditionsJson, reader.GetString(9), reader.GetString(10), reader.GetString(11).Trim(), Array.AsReadOnly(evidence));
    }

    private static void Validate(MatchId matchId, long maximumCanonicalRevision, int maximumProjectionRevision,
        int selectedSeconds, long throughSequence, long? afterSequence, string? afterInsightId, int limit)
    {
        if (matchId.Value == Guid.Empty) throw new ArgumentException("Match ID is required.", nameof(matchId));
        if (maximumCanonicalRevision <= 0) throw new ArgumentOutOfRangeException(nameof(maximumCanonicalRevision));
        if (maximumProjectionRevision <= 0) throw new ArgumentOutOfRangeException(nameof(maximumProjectionRevision));
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
