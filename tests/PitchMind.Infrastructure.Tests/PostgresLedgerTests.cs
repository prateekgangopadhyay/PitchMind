using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
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

    [PostgresIntegrationFact]
    public async Task SchemaMigrationsAreVersionedRepeatableAndAdmitRedCards()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);

        await using (var versions = source.CreateCommand("SELECT array_agg(version ORDER BY version) FROM pitchmind.schema_versions"))
            Assert.Equal(new[] { 1, 2, 3, 4 }, (int[])(await versions.ExecuteScalarAsync())!);
        foreach (var table in new[] { "matches", "tracking_frames", "match_projections", "insights", "insight_evidence", "quarantine", "consumer_checkpoints", "outbox" })
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
            await using (var admit = source.CreateCommand("UPDATE pitchmind.match_events SET canonical_revision = 1 WHERE match_id = $1 AND event_id = $2"))
            {
                admit.Parameters.AddWithValue(matchId);
                admit.Parameters.AddWithValue(futureEventId);
                Assert.Equal(1, await admit.ExecuteNonQueryAsync());
            }
            await using (var insight = source.CreateCommand("""
                INSERT INTO pitchmind.insights(insight_id, match_id, canonical_revision, revision, as_of_sequence,
                    detector_version, truth_class, summary, conditions_json)
                VALUES ($1, $2, 1, 7, 0, 'detector-v1', 'derived', 'Future evidence must be rejected', '{}'::jsonb)
                """))
            {
                insight.Parameters.AddWithValue(futureInsightId);
                insight.Parameters.AddWithValue(matchId);
                await insight.ExecuteNonQueryAsync();
            }
            await using (var mismatchedRevision = source.CreateCommand("""
                INSERT INTO pitchmind.insight_evidence(insight_id, evidence_kind, evidence_id, match_id,
                    canonical_revision, revision, relation, event_id)
                VALUES ($1, 'event', $2, $3, 2, 7, 'supports', $4)
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
                VALUES ($1, 'event', $2, $3, 1, 7, 'supports', $4)
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
