using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using PitchMind.Infrastructure.Storage;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES")))
            Skip = "Set PITCHMIND_TEST_POSTGRES to an isolated local PostgreSQL test database; not evidence of a passing integration test.";
    }
}

public sealed class PostgresLedgerTests
{
    private static async Task<List<MatchEvent>> Read(PostgresMatchEventStore store, MatchId matchId)
    {
        var results = new List<MatchEvent>();
        await foreach (var entry in store.ReadAsync(matchId, CancellationToken.None)) results.Add(entry);
        return results;
    }
    private static MatchEvent Event(MatchId matchId, long sequence = 0) => MatchEvent.Create(Guid.NewGuid(), matchId, sequence,
        MatchEventType.Pass, (int)sequence + 1, 1, DateTimeOffset.UtcNow);
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create(Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES")!);

    private static async Task IsolateOutboxAsync(NpgsqlDataSource source)
    {
        await using var command = source.CreateCommand("UPDATE pitchmind.outbox SET published_at = now(), claim_token = NULL, lease_expires_at = NULL WHERE published_at IS NULL");
        await command.ExecuteNonQueryAsync();
    }

    [PostgresIntegrationFact]
    public async Task SchemaMigrationsAreVersionedRepeatableAndAdmitRedCards()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);

        await using (var versions = source.CreateCommand("SELECT array_agg(version ORDER BY version) FROM pitchmind.schema_versions"))
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, (int[])(await versions.ExecuteScalarAsync())!);
        foreach (var table in new[] { "matches", "tracking_frames", "match_projections", "insights", "insight_evidence", "quarantine", "consumer_checkpoints", "outbox", "consumer_inbox" })
        {
            await using var command = source.CreateCommand("SELECT to_regclass($1) IS NOT NULL");
            command.Parameters.AddWithValue($"pitchmind.{table}");
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
        }
        foreach (var table in new[] { "match_events", "tracking_frames", "match_projections", "insights", "insight_evidence", "outbox" })
        {
            await using var column = source.CreateCommand("SELECT data_type FROM information_schema.columns WHERE table_schema = 'pitchmind' AND table_name = $1 AND column_name = 'canonical_revision'");
            column.Parameters.AddWithValue(table);
            Assert.Equal("bigint", await column.ExecuteScalarAsync());
        }

        var insightId = $"insight-{Guid.NewGuid():N}";
        var insightMatchId = MatchId.New().Value;
        var sourceHash = new string('a', 64);
        await using (var metadata = source.CreateCommand("INSERT INTO pitchmind.matches(match_id, fixture_version, source_version, source_sha256, provenance, pitch_length_metres, pitch_width_metres) VALUES ($1, 'fixture-v1', 'source-v1', $2, 'synthetic', 105, 68)"))
        {
            metadata.Parameters.AddWithValue(insightMatchId);
            metadata.Parameters.AddWithValue(sourceHash);
            await metadata.ExecuteNonQueryAsync();
        }
        await using (var projection = source.CreateCommand("INSERT INTO pitchmind.match_projections (match_id, canonical_revision, revision, as_of_sequence, as_of_clock_seconds, algorithm_version, source_sha256, state_json, state_sha256) VALUES ($1, 1, 1, 3, 90, 'test-v1', $2, '{}'::jsonb, $3)"))
        {
            projection.Parameters.AddWithValue(insightMatchId);
            projection.Parameters.AddWithValue(sourceHash);
            projection.Parameters.AddWithValue(new string('b', 64));
            await projection.ExecuteNonQueryAsync();
        }
        await using (var insight = source.CreateCommand("INSERT INTO pitchmind.insights (insight_id, match_id, canonical_revision, revision, as_of_sequence, detector_version, truth_class, summary, conditions_json) VALUES ($1, $2, 1, 1, 3, 'test-v1', 'derived', 'test insight', '{}'::jsonb)"))
        {
            insight.Parameters.AddWithValue(insightId);
            insight.Parameters.AddWithValue(insightMatchId);
            await insight.ExecuteNonQueryAsync();
        }
        await using (var mismatchedEvidence = source.CreateCommand("INSERT INTO pitchmind.insight_evidence (insight_id, evidence_kind, evidence_id, match_id, canonical_revision, revision, relation, event_id) VALUES ($1, 'event', $2, $3, 1, 1, 'supports', $4)"))
        {
            mismatchedEvidence.Parameters.AddWithValue(insightId);
            var eventId = Guid.NewGuid();
            mismatchedEvidence.Parameters.AddWithValue(eventId.ToString("D"));
            mismatchedEvidence.Parameters.AddWithValue(MatchId.New().Value);
            mismatchedEvidence.Parameters.AddWithValue(eventId);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => mismatchedEvidence.ExecuteNonQueryAsync());
            Assert.Equal("23503", exception.SqlState);
        }

        var matchId = MatchId.New();
        var redCard = MatchEvent.Create(Guid.NewGuid(), matchId, 0, MatchEventType.RedCard, 1, 1, DateTimeOffset.UtcNow);
        var store = new PostgresMatchEventStore(source);
        await store.AppendAsync(redCard, CancellationToken.None);
        Assert.Equal(redCard, Assert.Single(await Read(store, matchId)));
        await using (var revision = source.CreateCommand("SELECT canonical_revision IS NULL FROM pitchmind.match_events WHERE match_id = $1 AND event_id = $2"))
        {
            revision.Parameters.AddWithValue(matchId.Value);
            revision.Parameters.AddWithValue(redCard.EventId);
            Assert.True((bool)(await revision.ExecuteScalarAsync())!);
        }
        await using (var invalidRevision = source.CreateCommand("UPDATE pitchmind.match_events SET canonical_revision = 0 WHERE match_id = $1 AND event_id = $2"))
        {
            invalidRevision.Parameters.AddWithValue(matchId.Value);
            invalidRevision.Parameters.AddWithValue(redCard.EventId);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => invalidRevision.ExecuteNonQueryAsync());
            Assert.Equal("23514", exception.SqlState);
        }
    }

    [PostgresIntegrationFact]
    public async Task Version4MigrationPreservesLegacyProjectionsWithoutSourceMetadataAndFailsClosedOnRead()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);

        var matchId = Guid.NewGuid();
        var sourceHash = new string('a', 64);
        var stateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("{}"))).ToLowerInvariant();
        try
        {
            await using (var dropConstraint = source.CreateCommand(
                "ALTER TABLE pitchmind.match_projections DROP CONSTRAINT IF EXISTS match_projections_match_source_fk"))
                await dropConstraint.ExecuteNonQueryAsync();
            await using (var downgradeVersion = source.CreateCommand(
                "DELETE FROM pitchmind.schema_versions WHERE version >= 4"))
                await downgradeVersion.ExecuteNonQueryAsync();
            await using (var dropOutboxConstraint = source.CreateCommand(
                "ALTER TABLE pitchmind.outbox DROP CONSTRAINT IF EXISTS outbox_event_admission_revision_fk"))
                await dropOutboxConstraint.ExecuteNonQueryAsync();
            await using (var legacy = source.CreateCommand("""
                INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence,
                    as_of_clock_seconds, algorithm_version, source_sha256, state_json, state_sha256)
                VALUES ($1, NULL, 1, 0, 0, 'legacy-v3', $2, '{}'::jsonb, $3)
                """))
            {
                legacy.Parameters.AddWithValue(matchId);
                legacy.Parameters.AddWithValue(sourceHash);
                legacy.Parameters.AddWithValue(stateHash);
                await legacy.ExecuteNonQueryAsync();
            }
            await using (var orphan = source.CreateCommand("""
                INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence,
                    as_of_clock_seconds, algorithm_version, source_sha256, state_json, state_sha256)
                VALUES ($1, 1, 2, 0, 0, 'orphan-v3', $2, '{}'::jsonb, $3)
                """))
            {
                orphan.Parameters.AddWithValue(matchId);
                orphan.Parameters.AddWithValue(sourceHash);
                orphan.Parameters.AddWithValue(stateHash);
                await orphan.ExecuteNonQueryAsync();
            }

            await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);

            await using (var version = source.CreateCommand("SELECT MAX(version) FROM pitchmind.schema_versions"))
                Assert.Equal(5, (int)(await version.ExecuteScalarAsync())!);
            await using (var validation = source.CreateCommand("SELECT NOT convalidated FROM pg_constraint WHERE conname = 'match_projections_match_source_fk'"))
                Assert.True((bool)(await validation.ExecuteScalarAsync())!);
            await using (var legacyRow = source.CreateCommand("SELECT COUNT(*) FROM pitchmind.match_projections WHERE match_id = $1"))
            {
                legacyRow.Parameters.AddWithValue(matchId);
                Assert.Equal(2L, (long)(await legacyRow.ExecuteScalarAsync())!);
            }

            var readStore = new PostgresMatchProjectionStore(source);
            Assert.Null(await readStore.ReadProjectionAsync(matchId, 1, 2, CancellationToken.None));

            await using var rejected = source.CreateCommand("""
                INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence,
                    as_of_clock_seconds, algorithm_version, source_sha256, state_json, state_sha256)
                VALUES ($1, 1, 1, 0, 0, 'new-v4', $2, '{}'::jsonb, $3)
                """);
            rejected.Parameters.AddWithValue(Guid.NewGuid());
            rejected.Parameters.AddWithValue(sourceHash);
            rejected.Parameters.AddWithValue(stateHash);
            var exception = await Assert.ThrowsAsync<PostgresException>(() => rejected.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        }
        finally
        {
            await using (var cleanup = source.CreateCommand("DELETE FROM pitchmind.match_projections WHERE match_id = $1"))
            {
                cleanup.Parameters.AddWithValue(matchId);
                await cleanup.ExecuteNonQueryAsync();
            }
            await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        }
    }

    [PostgresIntegrationFact]
    public async Task ProjectionAndInsightProvenanceSurviveDataSourceReopen()
    {
        var matchId = Guid.NewGuid();
        var sourceHash = new string('a', 64);
        var metadata = new MatchSourceMetadata(matchId, "fixture-v1", "source-v1", sourceHash,
            "synthetic", 105m, 68m);
        var state = new MatchState(matchId.ToString("D"), 0, 1, MatchPhase.FirstHalf, 0, 0,
            "home", 11, 11, Array.Empty<MatchPlayerState>(), 0, "synthetic");
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var stateJson = JsonSerializer.Serialize(state, jsonOptions);
        var stateHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson))).ToLowerInvariant();
        var eventId = Guid.NewGuid();
        var insightId = $"insight-{Guid.NewGuid():N}";
        var futureInsightId = $"insight-{Guid.NewGuid():N}";
        var futureFrameInsightId = $"insight-{Guid.NewGuid():N}";

        await using (var source = Source())
        {
            await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
            var store = new PostgresMatchProjectionStore(source);
            await store.SaveMatchMetadataAsync(metadata, CancellationToken.None);
            await store.SaveMatchMetadataAsync(metadata, CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveMatchMetadataAsync(
                metadata with { SourceVersion = "conflicting-source" }, CancellationToken.None));

            var sourceEvent = MatchEvent.Create(eventId, new MatchId(matchId), 0,
                MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch);
            await new PostgresMatchEventStore(source).AppendAsync(sourceEvent, CancellationToken.None);
            await using (var admit = source.CreateCommand("UPDATE pitchmind.match_events SET canonical_revision = 1 WHERE match_id = $1 AND event_id = $2"))
            {
                admit.Parameters.AddWithValue(matchId);
                admit.Parameters.AddWithValue(eventId);
                Assert.Equal(1, await admit.ExecuteNonQueryAsync());
            }
            await using (var frame = source.CreateCommand("""
                INSERT INTO pitchmind.tracking_frames(match_id, frame_id, canonical_revision, sequence, period, clock_seconds,
                    coordinate_version, quality, source_sha256, payload)
                VALUES ($1, 'frame-kickoff', 1, 0, 1, 0, 'metres-v1', '{}'::jsonb, $2, '{}'::jsonb)
                """))
            {
                frame.Parameters.AddWithValue(matchId);
                frame.Parameters.AddWithValue(sourceHash);
                await frame.ExecuteNonQueryAsync();
            }
            await using (var projection = source.CreateCommand("""
                INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence, as_of_clock_seconds,
                    algorithm_version, source_sha256, state_json, state_sha256)
                VALUES ($1, 1, 7, 0, 0, 'aggregate-v1', $2, $3, $4)
                """))
            {
                projection.Parameters.AddWithValue(matchId);
                projection.Parameters.AddWithValue(sourceHash);
                projection.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, stateJson);
                projection.Parameters.AddWithValue(stateHash);
                await projection.ExecuteNonQueryAsync();
            }
            await using (var mismatchedProjection = source.CreateCommand("""
                INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence, as_of_clock_seconds,
                    algorithm_version, source_sha256, state_json, state_sha256)
                VALUES ($1, 1, 8, 0, 0, 'aggregate-v1', $2, $3, $4)
                """))
            {
                mismatchedProjection.Parameters.AddWithValue(matchId);
                mismatchedProjection.Parameters.AddWithValue(new string('f', 64));
                mismatchedProjection.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, stateJson);
                mismatchedProjection.Parameters.AddWithValue(stateHash);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => mismatchedProjection.ExecuteNonQueryAsync());
                Assert.Equal("23503", exception.SqlState);
            }
            await using (var insight = source.CreateCommand("""
                INSERT INTO pitchmind.insights(insight_id, match_id, canonical_revision, revision, as_of_sequence,
                    detector_version, truth_class, summary, conditions_json)
                VALUES ($1, $2, 1, 7, 0, 'detector-v1', 'derived', 'Persisted evidence', '{"selected":true}'::jsonb)
                """))
            {
                insight.Parameters.AddWithValue(insightId);
                insight.Parameters.AddWithValue(matchId);
                await insight.ExecuteNonQueryAsync();
            }
            await using (var evidence = source.CreateCommand("""
                INSERT INTO pitchmind.insight_evidence(insight_id, evidence_kind, evidence_id, match_id,
                    canonical_revision, revision, relation, event_id)
                VALUES ($1, 'event', $2, $3, 1, 7, 'supports', $4)
                """))
            {
                evidence.Parameters.AddWithValue(insightId);
                evidence.Parameters.AddWithValue(eventId.ToString("D"));
                evidence.Parameters.AddWithValue(matchId);
                evidence.Parameters.AddWithValue(eventId);
                await evidence.ExecuteNonQueryAsync();
            }
            await using (var evidence = source.CreateCommand("""
                INSERT INTO pitchmind.insight_evidence(insight_id, evidence_kind, evidence_id, match_id,
                    canonical_revision, revision, relation, frame_id)
                VALUES ($1, 'frame', 'frame-kickoff', $2, 1, 7, 'supports', 'frame-kickoff')
                """))
            {
                evidence.Parameters.AddWithValue(insightId);
                evidence.Parameters.AddWithValue(matchId);
                await evidence.ExecuteNonQueryAsync();
            }
            var futureEventId = Guid.NewGuid();
            await new PostgresMatchEventStore(source).AppendAsync(
                MatchEvent.Create(futureEventId, new MatchId(matchId), 1, MatchEventType.KickOff, 1, 1, DateTimeOffset.UnixEpoch.AddSeconds(1)),
                CancellationToken.None);
            await using (var admit = source.CreateCommand("UPDATE pitchmind.match_events SET canonical_revision = 2 WHERE match_id = $1 AND event_id = $2"))
            {
                admit.Parameters.AddWithValue(matchId);
                admit.Parameters.AddWithValue(futureEventId);
                Assert.Equal(1, await admit.ExecuteNonQueryAsync());
            }
            await using (var projection = source.CreateCommand("""
                INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence,
                    as_of_clock_seconds, algorithm_version, source_sha256, state_json, state_sha256)
                VALUES ($1, 2, 8, 0, 0, 'aggregate-v1', $2, $3, $4)
                """))
            {
                projection.Parameters.AddWithValue(matchId);
                projection.Parameters.AddWithValue(sourceHash);
                projection.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Jsonb, stateJson);
                projection.Parameters.AddWithValue(stateHash);
                await projection.ExecuteNonQueryAsync();
            }
            await using (var insight = source.CreateCommand("""
                INSERT INTO pitchmind.insights(insight_id, match_id, canonical_revision, revision, as_of_sequence,
                    detector_version, truth_class, summary, conditions_json)
                VALUES ($1, $2, 2, 8, 0, 'detector-v1', 'derived', 'Future evidence must be rejected', '{}'::jsonb)
                """))
            {
                insight.Parameters.AddWithValue(futureInsightId);
                insight.Parameters.AddWithValue(matchId);
                await insight.ExecuteNonQueryAsync();
            }
            await using (var mismatchedRevision = source.CreateCommand("""
                INSERT INTO pitchmind.insight_evidence(insight_id, evidence_kind, evidence_id, match_id,
                    canonical_revision, revision, relation, event_id)
                VALUES ($1, 'event', $2, $3, 2, 8, 'supports', $4)
                """))
            {
                mismatchedRevision.Parameters.AddWithValue(futureInsightId);
                mismatchedRevision.Parameters.AddWithValue(eventId.ToString("D"));
                mismatchedRevision.Parameters.AddWithValue(matchId);
                mismatchedRevision.Parameters.AddWithValue(eventId);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => mismatchedRevision.ExecuteNonQueryAsync());
                Assert.Equal("23503", exception.SqlState);
            }
            await using (var evidence = source.CreateCommand("""
                INSERT INTO pitchmind.insight_evidence(insight_id, evidence_kind, evidence_id, match_id,
                    canonical_revision, revision, relation, event_id)
                VALUES ($1, 'event', $2, $3, 2, 8, 'supports', $4)
                """))
            {
                evidence.Parameters.AddWithValue(futureInsightId);
                evidence.Parameters.AddWithValue(futureEventId.ToString("D"));
                evidence.Parameters.AddWithValue(matchId);
                evidence.Parameters.AddWithValue(futureEventId);
                await evidence.ExecuteNonQueryAsync();
            }
            await using (var frame = source.CreateCommand("""
                INSERT INTO pitchmind.tracking_frames(match_id, frame_id, canonical_revision, sequence, period, clock_seconds,
                    coordinate_version, quality, source_sha256, payload)
                VALUES ($1, 'frame-future', 1, 1, 1, 1, 'metres-v1', '{}'::jsonb, $2, '{}'::jsonb)
                """))
            {
                frame.Parameters.AddWithValue(matchId);
                frame.Parameters.AddWithValue(sourceHash);
                await frame.ExecuteNonQueryAsync();
            }
            await using (var insight = source.CreateCommand("""
                INSERT INTO pitchmind.insights(insight_id, match_id, canonical_revision, revision, as_of_sequence,
                    detector_version, truth_class, summary, conditions_json)
                VALUES ($1, $2, 1, 7, 0, 'detector-v1', 'derived', 'Future frame must be rejected', '{}'::jsonb)
                """))
            {
                insight.Parameters.AddWithValue(futureFrameInsightId);
                insight.Parameters.AddWithValue(matchId);
                await insight.ExecuteNonQueryAsync();
            }
            await using (var evidence = source.CreateCommand("""
                INSERT INTO pitchmind.insight_evidence(insight_id, evidence_kind, evidence_id, match_id,
                    canonical_revision, revision, relation, frame_id)
                VALUES ($1, 'frame', 'frame-future', $2, 1, 7, 'supports', 'frame-future')
                """))
            {
                evidence.Parameters.AddWithValue(futureFrameInsightId);
                evidence.Parameters.AddWithValue(matchId);
                await evidence.ExecuteNonQueryAsync();
            }
        }

        await using var reopened = Source();
        var readStore = new PostgresMatchProjectionStore(reopened);
        var restoredProjection = await readStore.ReadProjectionAsync(matchId, 1L, 7, CancellationToken.None);
        Assert.NotNull(restoredProjection);
        Assert.Equal(1, restoredProjection.CanonicalRevision);
        Assert.Equal(7, restoredProjection.Revision);
        Assert.Null(await readStore.ReadProjectionAsync(matchId, 1L, 1, CancellationToken.None));
        Assert.Equal(stateJson, JsonSerializer.Serialize(restoredProjection.State, jsonOptions));
        Assert.Equal(sourceHash, restoredProjection.SourceSha256);

        var provenance = await readStore.ReadInsightProvenanceAsync(insightId, CancellationToken.None);
        Assert.NotNull(provenance);
        Assert.Equal(matchId, provenance.Source.MatchId);
        Assert.Equal("source-v1", provenance.Source.SourceVersion);
        Assert.Equal(sourceHash, provenance.Source.SourceSha256);
        Assert.Equal(1, provenance.Insight.CanonicalRevision);
        Assert.Equal(7, provenance.Insight.Revision);
        Assert.Equal(0, provenance.Insight.AsOfSequence);
        Assert.Contains(provenance.Insight.Evidence, item => item.Kind == "event" && item.Id == eventId.ToString("D"));
        Assert.Contains(provenance.Insight.Evidence, item => item.Kind == "frame" && item.Id == "frame-kickoff");
        Assert.Equal(stateHash, provenance.StateSha256);
        await Assert.ThrowsAsync<InvalidDataException>(() => readStore.ReadInsightProvenanceAsync(futureInsightId, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => readStore.ReadInsightProvenanceAsync(futureFrameInsightId, CancellationToken.None));
    }

    [PostgresIntegrationFact]
    public async Task ExistingVersionTwoDatabaseUpgradesToCurrentSchemaRepeatably()
    {
        await using var source = Source();
        await using (var connection = await source.OpenConnectionAsync())
        await using (var transaction = await connection.BeginTransactionAsync())
        await using (var legacy = new NpgsqlCommand(PostgresSchemaMigrator.SchemaSql + PostgresSchemaMigrator.Version2Sql, connection, transaction))
        {
            await legacy.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }

        var existingEvent = Event(MatchId.New());
        await new PostgresMatchEventStore(source).AppendAsync(existingEvent, CancellationToken.None);

        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await using var versions = source.CreateCommand("SELECT array_agg(version ORDER BY version) FROM pitchmind.schema_versions");
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, (int[])(await versions.ExecuteScalarAsync())!);
        await using var inbox = source.CreateCommand("SELECT to_regclass('pitchmind.consumer_inbox') IS NOT NULL");
        Assert.True((bool)(await inbox.ExecuteScalarAsync())!);
        Assert.Equal(existingEvent, Assert.Single(await Read(new(source), existingEvent.MatchId)));
    }

    private static (CanonicalUpdate Update, MatchAggregate Aggregate) BuildUpdate(MatchId matchId, int revision = 1)
    {
        var evt = MatchEvent.Create(Guid.NewGuid(), matchId, 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UtcNow);
        var players = Enumerable.Range(1, 11).SelectMany(i => new[] { ($"home-{i}", "home"), ($"away-{i}", "away") }).ToArray();
        var aggregate = new MatchAggregate(matchId, "home", "away", players,
            players.Select(p => p.Item1), "approved-synthetic-test");
        aggregate.Apply(new SimulationEvent(evt, "home", "home-1", "kickoff", "started"));
        var update = CanonicalUpdate.FromAppliedAggregate(aggregate, evt, revision, "aggregate-v1", new string('a', 64));
        return (update, aggregate);
    }

    private static CanonicalUpdate Update(MatchId matchId) => BuildUpdate(matchId).Update;

    private static Task RegisterSourceAsync(NpgsqlDataSource source, CanonicalUpdate update) =>
        new PostgresMatchProjectionStore(source).SaveMatchMetadataAsync(
            new MatchSourceMetadata(update.Event.MatchId.Value, "fixture-v1", "source-v1", update.SourceSha256,
                "approved-synthetic-test", 105m, 68m), CancellationToken.None);

    [PostgresIntegrationFact]
    public async Task CanonicalEventProjectionAndOutboxCommitTogetherAndExactRetryIsIdempotent()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await IsolateOutboxAsync(source);
        var (update, aggregate) = BuildUpdate(MatchId.New());
        var store = new PostgresCanonicalUpdateStore(source);

        await RegisterSourceAsync(source, update);
        Assert.Equal(CanonicalUpdateCommitResult.Committed, await store.CommitAsync(update, CancellationToken.None));
        Assert.Equal(CanonicalUpdateCommitResult.AlreadyCommitted, await store.CommitAsync(update, CancellationToken.None));
        await using var counts = source.CreateCommand("""
            SELECT (SELECT count(*) FROM pitchmind.match_events WHERE match_id = $1),
                   (SELECT count(*) FROM pitchmind.match_projections WHERE match_id = $1),
                   (SELECT count(*) FROM pitchmind.outbox WHERE match_id = $1)
            """);
        counts.Parameters.AddWithValue(update.Event.MatchId.Value);
        await using var reader = await counts.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
        await reader.DisposeAsync();

        var fabricatedGoal = update.Event with { Type = MatchEventType.Goal };
        Assert.Throws<InvalidOperationException>(() => CanonicalUpdate.FromAppliedAggregate(
            aggregate, fabricatedGoal, 1, "aggregate-v1", new string('a', 64)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(
            CanonicalUpdate.FromAppliedAggregate(aggregate, update.Event, 1, "aggregate-v1", new string('b', 64)),
            CancellationToken.None));
        Assert.Equal(OutboxDispatchResult.Published, await new PostgresOutboxDispatcher(source, new CountingPublisher())
            .DispatchOneAsync(CancellationToken.None));
    }

    [PostgresIntegrationFact]
    public async Task GoalRetryDoesNotDoubleApplyScoreAndCanonicalRevisionAdvancesPerMatch()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var matchId = MatchId.New();
        var players = Enumerable.Range(1, 11).SelectMany(i => new[] { ($"home-{i}", "home"), ($"away-{i}", "away") }).ToArray();
        var aggregate = new MatchAggregate(matchId, "home", "away", players, players.Select(p => p.Item1), "approved-synthetic-test");
        var kickoff = MatchEvent.Create(Guid.NewGuid(), matchId, 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UtcNow);
        aggregate.Apply(new SimulationEvent(kickoff, "home", "home-1", "kickoff", "started"));
        var kickoffUpdate = CanonicalUpdate.FromAppliedAggregate(aggregate, kickoff, 1, "aggregate-v1", new string('a', 64));
        var store = new PostgresCanonicalUpdateStore(source);
        await RegisterSourceAsync(source, kickoffUpdate);
        Assert.Equal(CanonicalUpdateCommitResult.Committed, await store.CommitAsync(kickoffUpdate, CancellationToken.None));
        var goal = MatchEvent.Create(Guid.NewGuid(), matchId, 1, MatchEventType.Goal, 120, 1, DateTimeOffset.UtcNow);
        aggregate.Apply(new SimulationEvent(goal, "home", "home-2", "shot", "goal"));
        var update = CanonicalUpdate.FromAppliedAggregate(aggregate, goal, 2, "aggregate-v1", new string('a', 64));

        Assert.Equal(CanonicalUpdateCommitResult.Committed, await store.CommitAsync(update, CancellationToken.None));
        Assert.Equal(CanonicalUpdateCommitResult.AlreadyCommitted, await store.CommitAsync(update, CancellationToken.None));

        await using var query = source.CreateCommand("""
            SELECT e.canonical_revision, (p.state_json->>'homeScore')::integer
            FROM pitchmind.match_events e
            JOIN pitchmind.match_projections p
              ON p.match_id = e.match_id AND p.canonical_revision = e.canonical_revision
             AND p.as_of_sequence = e.sequence
            WHERE e.match_id = $1 ORDER BY e.canonical_revision
            """);
        query.Parameters.AddWithValue(matchId.Value);
        await using var rows = await query.ExecuteReaderAsync();
        Assert.True(await rows.ReadAsync());
        Assert.Equal(1L, rows.GetInt64(0));
        Assert.Equal(0, rows.GetInt32(1));
        Assert.True(await rows.ReadAsync());
        Assert.Equal(2L, rows.GetInt64(0));
        Assert.Equal(1, rows.GetInt32(1));
        Assert.False(await rows.ReadAsync());
        await rows.DisposeAsync();

        await using var counts = source.CreateCommand("""
            SELECT (SELECT count(*) FROM pitchmind.match_events WHERE match_id = $1),
                   (SELECT count(*) FROM pitchmind.match_projections WHERE match_id = $1),
                   (SELECT count(*) FROM pitchmind.outbox WHERE match_id = $1)
            """);
        counts.Parameters.AddWithValue(matchId.Value);
        await using var countReader = await counts.ExecuteReaderAsync();
        Assert.True(await countReader.ReadAsync());
        Assert.Equal(2L, countReader.GetInt64(0));
        Assert.Equal(2L, countReader.GetInt64(1));
        Assert.Equal(2L, countReader.GetInt64(2));
    }

    [PostgresIntegrationFact]
    public async Task CanonicalUpdateRejectsAggregateBuiltFromDivergentPriorHistory()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var matchId = MatchId.New();
        var (persistedKickoff, _) = BuildUpdate(matchId);
        await RegisterSourceAsync(source, persistedKickoff);
        var store = new PostgresCanonicalUpdateStore(source);
        Assert.Equal(CanonicalUpdateCommitResult.Committed,
            await store.CommitAsync(persistedKickoff, CancellationToken.None));

        var players = Enumerable.Range(1, 11).SelectMany(i => new[] { ($"home-{i}", "home"), ($"away-{i}", "away") }).ToArray();
        var divergentAggregate = new MatchAggregate(matchId, "home", "away", players,
            players.Select(p => p.Item1), "approved-synthetic-test");
        var divergentKickoff = MatchEvent.Create(Guid.NewGuid(), matchId, 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UtcNow);
        divergentAggregate.Apply(new SimulationEvent(divergentKickoff, "away", "away-1", "kickoff", "started"));
        var pass = MatchEvent.Create(Guid.NewGuid(), matchId, 1, MatchEventType.Pass, 1, 1, DateTimeOffset.UtcNow);
        divergentAggregate.Apply(new SimulationEvent(pass, "away", "away-1", "pass", "complete"));
        var divergentUpdate = CanonicalUpdate.FromAppliedAggregate(divergentAggregate, pass, 2,
            "aggregate-v1", persistedKickoff.SourceSha256);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(divergentUpdate, CancellationToken.None));
        Assert.Contains("Aggregate history diverges", error.Message, StringComparison.Ordinal);
        await using var counts = source.CreateCommand("""
            SELECT (SELECT count(*) FROM pitchmind.match_events WHERE match_id = $1),
                   (SELECT count(*) FROM pitchmind.match_projections WHERE match_id = $1),
                   (SELECT count(*) FROM pitchmind.outbox WHERE match_id = $1)
            """);
        counts.Parameters.AddWithValue(matchId.Value);
        await using var reader = await counts.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(1L, reader.GetInt64(2));
    }

    [PostgresIntegrationFact]
    public async Task OutboxRecoversAfterPublishFailureAndInboxAppliesEffectOnce()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await IsolateOutboxAsync(source);
        var update = Update(MatchId.New());
        await RegisterSourceAsync(source, update);
        await new PostgresCanonicalUpdateStore(source).CommitAsync(update, CancellationToken.None);

        await using (var prepare = source.CreateCommand("CREATE TABLE pitchmind.consumer_effect_test (message_id uuid PRIMARY KEY, count integer NOT NULL)"))
            await prepare.ExecuteNonQueryAsync();
        var inbox = new PostgresConsumerInbox(source);
        var publisher = new DeliverThenLoseAckPublisher(inbox, update.Event.EventId);
        var dispatcher = new PostgresOutboxDispatcher(source, publisher);
        Assert.Equal(OutboxDispatchResult.RetryScheduled, await dispatcher.DispatchOneAsync(CancellationToken.None));
        await using (var makeDue = source.CreateCommand("UPDATE pitchmind.outbox SET next_attempt_at = now() - interval '1 second' WHERE message_id = $1"))
        {
            makeDue.Parameters.AddWithValue(update.Event.EventId);
            await makeDue.ExecuteNonQueryAsync();
        }
        // A new dispatcher instance simulates process restart. The first publish reached the consumer,
        // but its acknowledgement was lost, so the stable message is delivered again after retry.
        Assert.Equal(OutboxDispatchResult.Published, await new PostgresOutboxDispatcher(source, publisher).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(2, publisher.DeliveryAttempts);
        Assert.Equal(OutboxDispatchResult.NoMessage, await dispatcher.DispatchOneAsync(CancellationToken.None));
        await using var effect = source.CreateCommand("SELECT count FROM pitchmind.consumer_effect_test WHERE message_id = $1");
        effect.Parameters.AddWithValue(update.Event.EventId);
        Assert.Equal(1, (int)(await effect.ExecuteScalarAsync())!);
    }

    [PostgresIntegrationFact]
    public async Task FailedOutboxInsertRollsBackEventAndProjection()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var update = Update(MatchId.New());
        await RegisterSourceAsync(source, update);
        var triggerSuffix = Guid.NewGuid().ToString("N");
        var functionName = $"reject_outbox_{triggerSuffix}";
        var triggerName = $"reject_outbox_{triggerSuffix}";
        await using (var trigger = source.CreateCommand($"""
            CREATE FUNCTION pitchmind.{functionName}() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.message_id = '{update.Event.EventId:D}'::uuid THEN
                    RAISE EXCEPTION 'simulated outbox insert failure' USING ERRCODE = 'P0001';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER {triggerName} BEFORE INSERT ON pitchmind.outbox
            FOR EACH ROW EXECUTE FUNCTION pitchmind.{functionName}();
            """))
        {
            await trigger.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => new PostgresCanonicalUpdateStore(source).CommitAsync(update, CancellationToken.None));
        Assert.Equal("P0001", error.SqlState);
        await using var counts = source.CreateCommand("""
            SELECT (SELECT count(*) FROM pitchmind.match_events WHERE match_id = $1),
                   (SELECT count(*) FROM pitchmind.match_projections WHERE match_id = $1)
            """);
        counts.Parameters.AddWithValue(update.Event.MatchId.Value);
        await using var reader = await counts.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
        await reader.DisposeAsync();
        await using var cleanup = source.CreateCommand($"DROP TRIGGER {triggerName} ON pitchmind.outbox; DROP FUNCTION pitchmind.{functionName}();");
        await cleanup.ExecuteNonQueryAsync();
    }

    [PostgresIntegrationFact]
    public async Task OutboxRetriesAreBoundedAndDeadLetterAfterConfiguredLimit()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await IsolateOutboxAsync(source);
        var update = Update(MatchId.New());
        await RegisterSourceAsync(source, update);
        await new PostgresCanonicalUpdateStore(source).CommitAsync(update, CancellationToken.None);
        var dispatcher = new PostgresOutboxDispatcher(source, new AlwaysFailPublisher(), maxAttempts: 2);

        Assert.Equal(OutboxDispatchResult.RetryScheduled, await dispatcher.DispatchOneAsync(CancellationToken.None));
        await using (var due = source.CreateCommand("UPDATE pitchmind.outbox SET next_attempt_at = now() - interval '1 second' WHERE message_id = $1"))
        {
            due.Parameters.AddWithValue(update.Event.EventId);
            await due.ExecuteNonQueryAsync();
        }
        Assert.Equal(OutboxDispatchResult.DeadLettered, await dispatcher.DispatchOneAsync(CancellationToken.None));
        Assert.Equal(OutboxDispatchResult.NoMessage, await dispatcher.DispatchOneAsync(CancellationToken.None));
        await using var state = source.CreateCommand("SELECT attempt_count, failed_at IS NOT NULL, last_error_code FROM pitchmind.outbox WHERE message_id = $1");
        state.Parameters.AddWithValue(update.Event.EventId);
        await using var reader = await state.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.True(reader.GetBoolean(1));
        Assert.Equal("publisher-failure", reader.GetString(2));
    }

    [PostgresIntegrationFact]
    public async Task ConcurrentDispatchersDoNotClaimTheSameLiveLease()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await IsolateOutboxAsync(source);
        var update = Update(MatchId.New());
        await RegisterSourceAsync(source, update);
        await new PostgresCanonicalUpdateStore(source).CommitAsync(update, CancellationToken.None);
        var publisher = new BlockingPublisher();
        var firstDispatcher = new PostgresOutboxDispatcher(source, publisher, leaseDuration: TimeSpan.FromMinutes(1));
        var firstDispatch = firstDispatcher.DispatchOneAsync(CancellationToken.None);
        await publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var secondResult = await new PostgresOutboxDispatcher(source, new AlwaysFailPublisher())
            .DispatchOneAsync(CancellationToken.None);
        Assert.Equal(OutboxDispatchResult.NoMessage, secondResult);

        publisher.Release.TrySetResult();
        Assert.Equal(OutboxDispatchResult.Published, await firstDispatch);
    }

    [PostgresIntegrationFact]
    public async Task LongPublicationRenewsLeaseAndRemainsExclusive()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await IsolateOutboxAsync(source);
        var update = Update(MatchId.New());
        await RegisterSourceAsync(source, update);
        await new PostgresCanonicalUpdateStore(source).CommitAsync(update, CancellationToken.None);

        var publisher = new DelayedPublisher(TimeSpan.FromMilliseconds(300));
        var firstDispatch = new PostgresOutboxDispatcher(source, publisher,
            leaseDuration: TimeSpan.FromMilliseconds(60)).DispatchOneAsync(CancellationToken.None);
        await publisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(180);

        var competingPublisher = new CountingPublisher();
        Assert.Equal(OutboxDispatchResult.NoMessage,
            await new PostgresOutboxDispatcher(source, competingPublisher).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(0, competingPublisher.Publications);
        Assert.Equal(OutboxDispatchResult.Published, await firstDispatch);
    }

    [PostgresIntegrationFact]
    public async Task LaterRevisionWaitsUntilEarlierSameMatchOutboxMessageIsTerminal()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await IsolateOutboxAsync(source);
        var matchId = MatchId.New();
        var players = Enumerable.Range(1, 11).SelectMany(i => new[] { ($"home-{i}", "home"), ($"away-{i}", "away") }).ToArray();
        var aggregate = new MatchAggregate(matchId, "home", "away", players,
            players.Select(p => p.Item1), "approved-synthetic-test");
        var store = new PostgresCanonicalUpdateStore(source);
        var kickoff = MatchEvent.Create(Guid.NewGuid(), matchId, 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UtcNow);
        aggregate.Apply(new SimulationEvent(kickoff, "home", "home-1", "kickoff", "started"));
        var kickoffUpdate = CanonicalUpdate.FromAppliedAggregate(aggregate, kickoff, 1, "aggregate-v1", new string('a', 64));
        await RegisterSourceAsync(source, kickoffUpdate);
        Assert.Equal(CanonicalUpdateCommitResult.Committed, await store.CommitAsync(kickoffUpdate, CancellationToken.None));

        var goal = MatchEvent.Create(Guid.NewGuid(), matchId, 1, MatchEventType.Goal, 120, 1, DateTimeOffset.UtcNow);
        aggregate.Apply(new SimulationEvent(goal, "home", "home-2", "shot", "goal"));
        var goalUpdate = CanonicalUpdate.FromAppliedAggregate(aggregate, goal, 2, "aggregate-v1", new string('a', 64));
        Assert.Equal(CanonicalUpdateCommitResult.Committed, await store.CommitAsync(goalUpdate, CancellationToken.None));

        var firstPublisher = new BlockingPublisher();
        var firstDispatch = new PostgresOutboxDispatcher(source, firstPublisher, leaseDuration: TimeSpan.FromMinutes(1))
            .DispatchOneAsync(CancellationToken.None);
        await firstPublisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(kickoff.EventId, firstPublisher.MessageId);

        // The second worker must not skip the leased first revision to publish the goal update.
        Assert.Equal(OutboxDispatchResult.NoMessage,
            await new PostgresOutboxDispatcher(source, new CapturingPublisher()).DispatchOneAsync(CancellationToken.None));

        firstPublisher.Release.TrySetResult();
        Assert.Equal(OutboxDispatchResult.Published, await firstDispatch);
        var secondPublisher = new CapturingPublisher();
        Assert.Equal(OutboxDispatchResult.Published,
            await new PostgresOutboxDispatcher(source, secondPublisher).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(goal.EventId, Assert.Single(secondPublisher.MessageIds));
    }

    [PostgresIntegrationFact]
    public async Task ExpiredLeaseCanBeClaimedAfterWorkerCancellation()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await IsolateOutboxAsync(source);
        var update = Update(MatchId.New());
        await RegisterSourceAsync(source, update);
        await new PostgresCanonicalUpdateStore(source).CommitAsync(update, CancellationToken.None);
        var blockedPublisher = new BlockingPublisher();
        using var cancellation = new CancellationTokenSource();
        var abandonedDispatch = new PostgresOutboxDispatcher(source, blockedPublisher, leaseDuration: TimeSpan.FromMilliseconds(50))
            .DispatchOneAsync(cancellation.Token);
        await blockedPublisher.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandonedDispatch);
        await Task.Delay(75);

        var recoveredPublisher = new CountingPublisher();
        Assert.Equal(OutboxDispatchResult.Published,
            await new PostgresOutboxDispatcher(source, recoveredPublisher).DispatchOneAsync(CancellationToken.None));
        Assert.Equal(1, recoveredPublisher.Publications);
    }

    private sealed class DeliverThenLoseAckPublisher(PostgresConsumerInbox inbox, Guid eventId) : IOutboxPublisher
    {
        public int DeliveryAttempts { get; private set; }
        public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
        {
            DeliveryAttempts++;
            async Task Apply(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
            {
                await using var command = new NpgsqlCommand("INSERT INTO pitchmind.consumer_effect_test(message_id, count) VALUES ($1, 1) ON CONFLICT (message_id) DO UPDATE SET count = pitchmind.consumer_effect_test.count + 1", connection, transaction);
                command.Parameters.AddWithValue(eventId);
                await command.ExecuteNonQueryAsync(token);
            }
            Assert.Equal(eventId, messageId);
            await inbox.ExecuteOnceAsync("projection-consumer-v1", messageId, Apply, cancellationToken);
            if (DeliveryAttempts == 1) throw new InvalidOperationException("Simulated lost acknowledgement after consumer commit.");
        }
    }

    private sealed class AlwaysFailPublisher : IOutboxPublisher
    {
        public Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated persistent transport outage.");
    }

    private sealed class BlockingPublisher : IOutboxPublisher
    {
        public Guid MessageId { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
        {
            MessageId = messageId;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class DelayedPublisher(TimeSpan duration) : IOutboxPublisher
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(duration, cancellationToken);
        }
    }

    private sealed class CapturingPublisher : IOutboxPublisher
    {
        public List<Guid> MessageIds { get; } = [];
        public Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
        {
            MessageIds.Add(messageId);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingPublisher : IOutboxPublisher
    {
        public int Publications { get; private set; }
        public Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
        {
            Publications++;
            return Task.CompletedTask;
        }
    }

    [PostgresIntegrationFact]
    public async Task CommittedEventsSurviveNewDataSourceAndKeepTimestampPrecision()
    {
        var matchId = MatchId.New(); var evt = Event(matchId);
        await using (var source = Source())
        {
            await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
            await new PostgresMatchEventStore(source).AppendAsync(evt, CancellationToken.None);
        }
        await using var reopened = Source();
        Assert.Equal(evt, Assert.Single(await Read(new(reopened), matchId)));
    }
    [PostgresIntegrationFact]
    public async Task DuplicateConcurrentAppendCommitsOnceAndConflictingPayloadIsRejected()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source); var matchId = MatchId.New(); var evt = Event(matchId);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => store.AppendAsync(evt, CancellationToken.None)));
        Assert.Single(await Read(store, matchId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(evt with { ClockSeconds = 42 }, CancellationToken.None));
        Assert.Equal(evt, Assert.Single(await Read(store, matchId)));
    }
    [PostgresIntegrationFact]
    public async Task SequenceConflictsAndGapsRollbackWithoutChangingLedger()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source); var matchId = MatchId.New(); var first = Event(matchId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(Event(matchId, 2), CancellationToken.None));
        Assert.Empty(await Read(store, matchId));
        await store.AppendAsync(first, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(Event(matchId), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(Event(matchId, 2), CancellationToken.None));
        var next = Event(matchId, 1); await store.AppendAsync(next, CancellationToken.None);
        Assert.Equal(new[] { first, next }, await Read(store, matchId));
    }
    [PostgresIntegrationFact]
    public async Task CancellationCannotCommitAnEvent()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source); var matchId = MatchId.New();
        using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.AppendAsync(Event(matchId), token.Token));
        Assert.Empty(await Read(store, matchId));
    }
    [PostgresIntegrationFact]
    public async Task RuntimeRoleCanAppendReadButCannotChangeHistoryOrCreateTables()
    {
        await using var migrationSource = Source();
        await PostgresSchemaMigrator.ApplyAsync(migrationSource, CancellationToken.None);
        var role = "ledger_test_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        // Identifiers and password are internally generated lowercase hexadecimal values, never user input.
            await using (var setup = migrationSource.CreateCommand($"CREATE ROLE {role} LOGIN PASSWORD '{password}'; GRANT USAGE ON SCHEMA pitchmind TO {role}; GRANT SELECT, INSERT ON pitchmind.match_events TO {role};"))
            await setup.ExecuteNonQueryAsync();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES")!)
            { Username = role, Password = password };
            await using var runtimeSource = NpgsqlDataSource.Create(builder.ConnectionString);
            var store = new PostgresMatchEventStore(runtimeSource); var matchId = MatchId.New();
            var evt = Event(matchId); await store.AppendAsync(evt, CancellationToken.None);
            Assert.Equal(evt, Assert.Single(await Read(store, matchId)));
            foreach (var sql in new[] { "UPDATE pitchmind.match_events SET clock_seconds = 0", "DELETE FROM pitchmind.match_events", "CREATE TABLE pitchmind.runtime_forbidden(id int)", "SELECT * FROM pitchmind.match_projections" })
            {
                await using var denied = runtimeSource.CreateCommand(sql);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
            }
        }
        finally
        {
            await using var cleanup = migrationSource.CreateCommand($"DROP OWNED BY {role}; DROP ROLE {role};");
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
