using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Reads immutable canonical projections and evidence provenance; migrations are applied separately.</summary>
public sealed class PostgresMatchProjectionStore(NpgsqlDataSource dataSource) : IMatchProjectionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveMatchMetadataAsync(MatchSourceMetadata metadata, CancellationToken cancellationToken)
    {
        Validate(metadata);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand("""
            INSERT INTO pitchmind.matches(match_id, fixture_version, source_version, source_sha256, provenance, pitch_length_metres, pitch_width_metres,
                competition_id, competition_rules_version, max_substitute_reentries_per_player, replay_seed_json, replay_context_json)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12) ON CONFLICT (match_id) DO NOTHING
            """, connection, transaction))
        {
            AddMetadata(command, metadata);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var command = new NpgsqlCommand("""
            SELECT fixture_version = $2 AND source_version = $3 AND source_sha256 = $4 AND provenance = $5
                AND pitch_length_metres = $6 AND pitch_width_metres = $7 AND competition_id IS NOT DISTINCT FROM $8
                AND competition_rules_version IS NOT DISTINCT FROM $9 AND max_substitute_reentries_per_player IS NOT DISTINCT FROM $10
                AND replay_seed_json IS NOT DISTINCT FROM $11::jsonb
                AND replay_context_json IS NOT DISTINCT FROM $12::jsonb
            FROM pitchmind.matches WHERE match_id = $1
            """, connection, transaction))
        {
            AddMetadata(command, metadata);
            if (await command.ExecuteScalarAsync(cancellationToken) is not true)
                throw new InvalidOperationException("Match metadata is immutable; a conflicting source identity was supplied.");
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PersistedMatchProjection?> ReadProjectionAsync(Guid matchId, long canonicalRevision,
        int projectionRevision, CancellationToken cancellationToken)
    {
        if (matchId == Guid.Empty || canonicalRevision < 1 || projectionRevision < 1)
            throw new ArgumentOutOfRangeException(projectionRevision < 1 ? nameof(projectionRevision) : nameof(canonicalRevision));
        await using var command = dataSource.CreateCommand("""
            SELECT p.canonical_revision, p.revision, p.as_of_sequence, p.as_of_clock_seconds,
                p.algorithm_version, p.source_sha256, p.state_json::text, p.state_sha256,
                m.competition_id, m.competition_rules_version, m.max_substitute_reentries_per_player
            FROM pitchmind.match_projections p
            JOIN pitchmind.matches m ON m.match_id = p.match_id AND m.source_sha256 = p.source_sha256
            WHERE p.match_id = $1 AND p.canonical_revision = $2 AND p.revision = $3
            """);
        command.Parameters.AddWithValue(matchId);
        command.Parameters.AddWithValue(canonicalRevision);
        command.Parameters.AddWithValue(projectionRevision);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (reader.IsDBNull(0)) throw new InvalidDataException("Projection has no canonical admission revision.");
        var projection = ReadProjection(matchId, reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3),
            reader.GetString(4), reader.GetString(5).Trim(), reader.GetString(6), reader.GetString(7).Trim());
        ValidateCompetitionContext(projection.State, reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetInt32(10));
        return projection;
    }

    public async Task<PersistedMatchProjection?> ReadProjectionAtTimeAsync(Guid matchId, long canonicalRevision,
        int atClockSeconds, CancellationToken cancellationToken)
    {
        if (matchId == Guid.Empty || canonicalRevision < 1 || atClockSeconds < 0)
            throw new ArgumentOutOfRangeException(atClockSeconds < 0 ? nameof(atClockSeconds) : nameof(canonicalRevision));
        await using var command = dataSource.CreateCommand("""
            SELECT p.canonical_revision, p.revision, p.as_of_sequence, p.as_of_clock_seconds,
                p.algorithm_version, p.source_sha256, p.state_json::text, p.state_sha256,
                m.competition_id, m.competition_rules_version, m.max_substitute_reentries_per_player
            FROM pitchmind.match_projections p
            JOIN pitchmind.matches m ON m.match_id = p.match_id AND m.source_sha256 = p.source_sha256
            WHERE p.match_id = $1 AND p.canonical_revision <= $2 AND p.as_of_clock_seconds <= $3
            ORDER BY p.as_of_clock_seconds DESC, p.as_of_sequence DESC, p.canonical_revision DESC, p.revision DESC
            LIMIT 1
            """);
        command.Parameters.AddWithValue(matchId);
        command.Parameters.AddWithValue(canonicalRevision);
        command.Parameters.AddWithValue(atClockSeconds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (reader.IsDBNull(0)) throw new InvalidDataException("Projection has no canonical admission revision.");
        var projection = ReadProjection(matchId, reader.GetInt64(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3),
            reader.GetString(4), reader.GetString(5).Trim(), reader.GetString(6), reader.GetString(7).Trim());
        ValidateCompetitionContext(projection.State, reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9), reader.IsDBNull(10) ? null : reader.GetInt32(10));
        return projection;
    }

    public async Task<InsightProvenanceRecord?> ReadInsightProvenanceAsync(string insightId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(insightId)) throw new ArgumentException("Insight ID is required.", nameof(insightId));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        Guid matchId;
        int revision;
        long insightSequence;
        string detectorVersion;
        string truthClass;
        string summary;
        string conditionsJson;
        MatchSourceMetadata source;
        PersistedMatchProjection projection;
        string stateSha256;
        await using (var command = new NpgsqlCommand("""
            SELECT i.match_id, i.revision, i.as_of_sequence, i.detector_version, i.truth_class, i.summary, i.conditions_json::text,
                m.fixture_version, m.source_version, m.source_sha256, m.provenance, m.pitch_length_metres, m.pitch_width_metres,
                m.competition_id, m.competition_rules_version, m.max_substitute_reentries_per_player,
                p.as_of_sequence, p.as_of_clock_seconds, p.algorithm_version, p.source_sha256, p.state_json::text, p.state_sha256,
                i.canonical_revision, m.replay_seed_json::text, m.replay_context_json::text
            FROM pitchmind.insights i
            JOIN pitchmind.matches m ON m.match_id = i.match_id
            JOIN pitchmind.match_projections p ON p.match_id = i.match_id AND p.revision = i.revision
                AND p.canonical_revision = i.canonical_revision
            WHERE i.insight_id = $1
            """, connection))
        {
            command.Parameters.AddWithValue(insightId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            matchId = reader.GetGuid(0);
            revision = reader.GetInt32(1);
            insightSequence = reader.GetInt64(2);
            detectorVersion = reader.GetString(3);
            truthClass = reader.GetString(4);
            summary = reader.GetString(5);
            conditionsJson = reader.GetString(6);
            var replaySeed = reader.IsDBNull(23) ? null : JsonSerializer.Deserialize<MatchReplaySeed>(reader.GetString(23), JsonOptions)
                ?? throw new InvalidDataException("Persisted match replay seed is empty.");
            var replayContext = reader.IsDBNull(24) ? null : JsonSerializer.Deserialize<MatchReplayContext>(reader.GetString(24), JsonOptions)
                ?? throw new InvalidDataException("Persisted match replay context is empty.");
            source = new(matchId, reader.GetString(7), reader.GetString(8), reader.GetString(9).Trim(),
                reader.GetString(10), reader.GetDecimal(11), reader.GetDecimal(12),
                reader.IsDBNull(13) ? null : reader.GetString(13), reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetInt32(15), replaySeed, replayContext);
            Validate(source);
            var projectionSourceSha = reader.GetString(19).Trim();
            if (!string.Equals(projectionSourceSha, source.SourceSha256, StringComparison.Ordinal))
                throw new InvalidDataException("Projection source hash does not match the registered match source.");
            if (reader.IsDBNull(22)) throw new InvalidDataException("Insight has no canonical admission revision.");
            var canonicalRevision = reader.GetInt64(22);
            projection = ReadProjection(matchId, canonicalRevision, revision, reader.GetInt64(16), reader.GetInt32(17),
                reader.GetString(18), projectionSourceSha, reader.GetString(20), reader.GetString(21).Trim());
            ValidateCompetitionContext(projection.State, source.CompetitionId, source.CompetitionRulesVersion,
                source.MaximumSubstituteReentriesPerPlayer);
            stateSha256 = Hash(SerializeState(projection.State));
            if (projection.AsOfSequence < insightSequence)
                throw new InvalidDataException("Insight references evidence later than its canonical projection.");
        }

        var evidence = new List<InsightEvidenceReference>();
        await using (var command = new NpgsqlCommand("""
            SELECT e.evidence_kind, e.evidence_id, e.relation, me.sequence, tf.sequence
            FROM pitchmind.insight_evidence e
            LEFT JOIN pitchmind.match_events me ON me.match_id = e.match_id AND me.event_id = e.event_id
            LEFT JOIN pitchmind.tracking_frames tf ON tf.match_id = e.match_id AND tf.frame_id = e.frame_id
            WHERE e.insight_id = $1 ORDER BY e.evidence_kind, e.evidence_id
            """, connection))
        {
            command.Parameters.AddWithValue(insightId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var evidenceKind = reader.GetString(0);
                var evidenceSequence = evidenceKind switch
                {
                    "event" => reader.GetInt64(3),
                    "frame" => reader.GetInt64(4),
                    _ => (long?)null
                };
                if (evidenceSequence > insightSequence)
                    throw new InvalidDataException("Insight references event or tracking evidence later than its selected sequence.");
                evidence.Add(new(evidenceKind, reader.GetString(1), reader.GetString(2)));
            }
        }
        var insight = new CanonicalInsightRecord(insightId, matchId, projection.CanonicalRevision, revision, insightSequence,
            detectorVersion, truthClass, summary, conditionsJson, Array.AsReadOnly(evidence.ToArray()));
        return new(insight, source, projection, stateSha256);
    }

    private static PersistedMatchProjection ReadProjection(Guid matchId, long canonicalRevision, int revision, long sequence, int clock,
        string algorithmVersion, string sourceSha256, string stateJson, string expectedHash)
    {
        var state = JsonSerializer.Deserialize<MatchState>(stateJson, JsonOptions)
            ?? throw new InvalidDataException("Persisted projection state is empty.");
        var projection = new PersistedMatchProjection(matchId, canonicalRevision, revision, sequence, clock, algorithmVersion, sourceSha256, state);
        Validate(projection);
        if (!string.Equals(Hash(SerializeState(state)), expectedHash, StringComparison.Ordinal))
            throw new InvalidDataException("Persisted projection state hash does not match its content.");
        return projection;
    }

    private static void Validate(MatchSourceMetadata value)
    {
        if (value.MatchId == Guid.Empty || string.IsNullOrWhiteSpace(value.FixtureVersion) || string.IsNullOrWhiteSpace(value.SourceVersion) ||
            !IsSha256(value.SourceSha256) || string.IsNullOrWhiteSpace(value.Provenance) ||
            !FootballPitchDimensions.AreValid((double)value.PitchLengthMetres, (double)value.PitchWidthMetres) ||
            value.CompetitionId is not null && string.IsNullOrWhiteSpace(value.CompetitionId) ||
            value.CompetitionRulesVersion is not null && string.IsNullOrWhiteSpace(value.CompetitionRulesVersion) ||
            value.MaximumSubstituteReentriesPerPlayer is < 0 ||
            value.MaximumSubstituteReentriesPerPlayer.HasValue && string.IsNullOrWhiteSpace(value.CompetitionRulesVersion))
            throw new ArgumentException("Match source metadata is incomplete or invalid.", nameof(value));
        if (value.ReplaySeed is not null)
        {
            if (value.ReplaySeed.Provenance != value.Provenance)
                throw new ArgumentException("Replay seed provenance must match the immutable match source.", nameof(value));
            try { _ = MatchAggregate.FromReplaySeed(new(value.MatchId), value.ReplaySeed); }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or NullReferenceException)
            {
                throw new ArgumentException("Replay seed roster is invalid.", nameof(value), exception);
            }
        }
        if (value.ReplayContext is not null)
        {
            var context = value.ReplayContext;
            if (context.SchemaVersion != "match-replay-context-v1" || context.FixtureVersion != value.FixtureVersion ||
                context.Provenance != value.Provenance || string.IsNullOrWhiteSpace(context.HomeTeamName) ||
                string.IsNullOrWhiteSpace(context.AwayTeamName) || context.DurationSeconds is < 1 or > 5400 ||
                context.HalfTimeSeconds <= 0 || context.HalfTimeSeconds >= context.DurationSeconds ||
                context.CoordinateVersion != "metres-v1" ||
                !context.CoordinateConvention.Contains("metre", StringComparison.OrdinalIgnoreCase) ||
                !double.IsFinite(context.PitchLengthMetres) || !double.IsFinite(context.PitchWidthMetres) ||
                context.PitchLengthMetres != (double)value.PitchLengthMetres ||
                context.PitchWidthMetres != (double)value.PitchWidthMetres)
                throw new ArgumentException("Replay context must be immutable, versioned, metric, and match its registered source.", nameof(value));
        }
    }

    private static void Validate(PersistedMatchProjection value)
    {
        if (value.MatchId == Guid.Empty || value.CanonicalRevision < 1 || value.Revision < 1 || value.AsOfSequence < -1 || value.AsOfClockSeconds < 0 ||
            string.IsNullOrWhiteSpace(value.AlgorithmVersion) || !IsSha256(value.SourceSha256) || value.State is null ||
            value.State.MatchId != value.MatchId.ToString("D") || value.State.LastSequence != value.AsOfSequence ||
            value.State.ClockSeconds > value.AsOfClockSeconds)
            throw new InvalidDataException("Persisted projection metadata and state are inconsistent.");
    }

    private static void ValidateCompetitionContext(MatchState state, string? competitionId, string? rulesVersion, int? maximumReentries)
    {
        if (state.CompetitionId != competitionId || state.CompetitionRulesVersion != rulesVersion ||
            state.MaximumSubstituteReentriesPerPlayer != maximumReentries)
            throw new InvalidDataException("Projection competition context does not match the registered match source.");
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string SerializeState(MatchState value) => JsonSerializer.Serialize(value, JsonOptions);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void AddMetadata(NpgsqlCommand command, MatchSourceMetadata value)
    {
        command.Parameters.AddWithValue(value.MatchId);
        command.Parameters.AddWithValue(value.FixtureVersion);
        command.Parameters.AddWithValue(value.SourceVersion);
        command.Parameters.AddWithValue(value.SourceSha256);
        command.Parameters.AddWithValue(value.Provenance);
        command.Parameters.AddWithValue(value.PitchLengthMetres);
        command.Parameters.AddWithValue(value.PitchWidthMetres);
        command.Parameters.AddWithValue((object?)value.CompetitionId ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)value.CompetitionRulesVersion ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)value.MaximumSubstituteReentriesPerPlayer ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
            Value = value.ReplaySeed is null ? DBNull.Value : JsonSerializer.Serialize(value.ReplaySeed, JsonOptions)
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
            Value = value.ReplayContext is null ? DBNull.Value : JsonSerializer.Serialize(value.ReplayContext, JsonOptions)
        });
    }
}
