Warning: truncated output (original token count: 34201)
Total output lines: 2218

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Npgsql;
using PitchMind.Application.Abstractions;
using NpgsqlTypes;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Analytics;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using PitchMind.Infrastructure.Storage;
using PitchMind.TestFixtures;
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
    [Fact]
    public async Task LocalApiRoleProvisionerRejectsLowDiversityPasswordBeforeDatabaseConnection()
    {
        await using var source = NpgsqlDataSource.Create(
            "Host=127.0.0.1;Username=unused;Password=unused;Database=unused;Timeout=1");
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            LocalApiRoleProvisioner.ProvisionAsync(source, new string('a', 48), CancellationToken.None));
        Assert.Contains("12 unique hex digits", exception.Message, StringComparison.Ordinal);

        var trailingNewlinePassword = "0123456789abcdef0123456789abcdef0123456789abcdef\n";
        var formatException = await Assert.ThrowsAsync<ArgumentException>(() =>
            LocalApiRoleProvisioner.ProvisionAsync(source, trailingNewlinePassword, CancellationToken.None));
        Assert.Contains("48 lowercase hexadecimal characters", formatException.Message, StringComparison.Ordinal);
    }

    [PostgresIntegrationFact]
    public async Task LocalApiRoleCanReadCanonicalDataAndDispatchOutboxWithoutSchemaOrLedgerWrites()
    {
        await using var migrationSource = Source();
        await PostgresSchemaMigrator.ApplyAsync(migrationSource, CancellationToken.None);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        await LocalApiRoleProvisioner.ProvisionAsync(migrationSource, password, CancellationToken.None);

        await using (var overprivilege = migrationSource.CreateCommand("""
            ALTER ROLE pitchmind_api WITH SUPERUSER CREATEDB CREATEROLE INHERIT REPLICATION BYPASSRLS;
            CREATE ROLE pitchmind_api_privileged;
            GRANT pitchmind_api_privileged TO pitchmind_api;
            CREATE SEQUENCE pitchmind.api_privilege_probe;
            CREATE TABLE pitchmind.api_owned_probe (id integer);
            ALTER TABLE pitchmind.api_owned_probe OWNER TO pitchmind_api;
            CREATE SCHEMA api_schema_probe;
            CREATE TABLE api_schema_probe.probe (id integer);
            GRANT ALL PRIVILEGES ON SCHEMA pitchmind TO pitchmind_api;
            GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA pitchmind TO pitchmind_api;
            GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA pitchmind TO pitchmind_api;
            GRANT ALL PRIVILEGES ON SCHEMA api_schema_probe TO pitchmind_api;
            GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA api_schema_probe TO pitchmind_api;
            ALTER DEFAULT PRIVILEGES GRANT ALL ON TABLES TO pitchmind_api;
            ALTER DEFAULT PRIVILEGES IN SCHEMA pitchmind GRANT ALL ON TABLES TO pitchmind_api;
            """))
            await overprivilege.ExecuteNonQueryAsync();

        await using (var grantDatabasePrivilegesSql = migrationSource.CreateCommand(
                         "SELECT format('GRANT ALL PRIVILEGES ON DATABASE %I TO pitchmind_api', current_database())"))
        {
            var sql = (string)(await grantDatabasePrivilegesSql.ExecuteScalarAsync())!;
            await using var grantDatabasePrivileges = migrationSource.CreateCommand(sql);
            await grantDatabasePrivileges.ExecuteNonQueryAsync();
        }

        await LocalApiRoleProvisioner.ProvisionAsync(migrationSource, password, CancellationToken.None);

        await using (var futureTable = migrationSource.CreateCommand(
                         "CREATE TABLE pitchmind.api_default_acl_probe (id integer)"))
            await futureTable.ExecuteNonQueryAsync();

        await using (var roleFlags = migrationSource.CreateCommand("""
            SELECT rolsuper, rolcreatedb, rolcreaterole, rolinherit, rolreplication, rolbypassrls
            FROM pg_roles WHERE rolname = 'pitchmind_api'
            """))
        await using (var reader = await roleFlags.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.False(reader.GetBoolean(0));
            Assert.False(reader.GetBoolean(1));
            Assert.False(reader.GetBoolean(2));
            Assert.False(reader.GetBoolean(3));
            Assert.False(reader.GetBoolean(4));
            Assert.False(reader.GetBoolean(5));
        }

        await using (var membershipCount = migrationSource.CreateCommand(
                         "SELECT COUNT(*) FROM pg_auth_members WHERE member = 'pitchmind_api'::regrole"))
            Assert.Equal(0L, (long)(await membershipCount.ExecuteScalarAsync())!);

        await using (var privilegeCheck = migrationSource.CreateCommand("""
            SELECT has_database_privilege('pitchmind_api', current_database(), 'CREATE'),
                   has_schema_privilege('pitchmind_api', 'pitchmind', 'CREATE'),
                   has_table_privilege('pitchmind_api', 'pitchmind.match_events', 'UPDATE'),
                   has_table_privilege('pitchmind_api', 'pitchmind.api_default_acl_probe', 'SELECT'),
                   has_sequence_privilege('pitchmind_api', 'pitchmind.api_privilege_probe', 'USAGE'),
                   has_table_privilege('pitchmind_api', 'pitchmind.api_owned_probe', 'UPDATE'),
                   has_schema_privilege('pitchmind_api', 'api_schema_probe', 'CREATE'),
                   has_table_privilege('pitchmind_api', 'api_schema_probe.probe', 'SELECT'),
                   pg_get_userbyid((SELECT relowner FROM pg_class WHERE oid = 'pitchmind.api_owned_probe'::regclass))
                       = 'pitchmind_api'
            """))
        await using (var reader = await privilegeCheck.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.False(reader.GetBoolean(0));
            Assert.False(reader.GetBoolean(1));
            Assert.False(reader.GetBoolean(2));
            Assert.False(reader.GetBoolean(3));
            Assert.False(reader.GetBoolean(4));
            Assert.False(reader.GetBoolean(5));
            Assert.False(reader.GetBoolean(6));
            Assert.False(reader.GetBoolean(7));
            Assert.False(reader.GetBoolean(8));
        }

        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES"))
        { Username = "pitchmind_api", Password = password };
        try
        {
            await using var apiSource = NpgsqlDataSource.Create(builder.ConnectionString);
            await using (var read = apiSource.CreateCommand("SELECT COUNT(*) FROM pitchmind.matches"))
                Assert.True((long)(await read.ExecuteScalarAsync())! >= 0);
            await using (var readiness = apiSource.CreateCommand("SELECT COALESCE(MAX(version), 0) FROM pitchmind.schema_versions"))
                Assert.Equal(PostgresSchemaMigrator.CurrentSchemaVersion, (int)(await readiness.ExecuteScalarAsync())!);

            await using (var dispatch = apiSource.CreateCommand("UPDATE pitchmind.outbox SET attempt_count = attempt_count WHERE false"))
                Assert.Equal(0, await dispatch.ExecuteNonQueryAsync());

            foreach (var sql in new[]
            {
                "UPDATE pitchmind.match_events SET sequence = sequence WHERE false",
                "UPDATE pitchmind.schema_versions SET version = version WHERE false",
                "DELETE FROM pitchmind.outbox WHERE false",
                "SELECT COUNT(*) FROM pitchmind.api_default_acl_probe",
                "CREATE TABLE pitchmind.local_api_must_not_ddl (id integer)"
            })
            {
                await using var forbidden = apiSource.CreateCommand(sql);
                var denied = await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync());
                Assert.Equal("42501", denied.SqlState);
            }
        }
        finally
        {
            await using var cleanup = migrationSource.CreateCommand("""
                ALTER DEFAULT PRIVILEGES REVOKE ALL ON TABLES FROM pitchmind_api;
                ALTER DEFAULT PRIVILEGES IN SCHEMA pitchmind REVOKE ALL ON TABLES FROM pitchmind_api;
                DROP OWNED BY pitchmind_api;
                DROP ROLE IF EXISTS pitchmind_api;
                DROP ROLE IF EXISTS pitchmind_api_privileged;
                DROP SEQUENCE IF EXISTS pitchmind.api_privilege_probe;
                DROP TABLE IF EXISTS pitchmind.api_owned_probe;
                DROP TABLE IF EXISTS pitchmind.api_default_acl_probe;
                DROP SCHEMA IF EXISTS api_schema_probe CASCADE;
                """);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    // Called only by scripts/test-ep07-s05-live-api-e2e.py against its disposable
    // database. These values are synthetic test fixtures, not football facts.
    [PostgresIntegrationFact]
    public async Task SeedLocalBrowserJourneyFixtureForApiE2E()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await IsolateOutboxAsync(source);

        var matchId = new MatchId(Guid.Parse("33333333-3333-4333-8333-333333333333"));
        var insightId = "local-e2e-synthetic-insight";
        var eventId = Guid.Parse("aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee");
        var players = Enumerable.Range(1, 11)
            .SelectMany(number => new[] { ($"home-test-player-{number}", "home-test"), ($"away-test-player-{number}", "away-test") })
            .ToArray();
        var aggregate = new MatchAggregate(matchId, "home-test", "away-test", players,
            players.Select(player => player.Item1), "synthetic-test-only");
        var sourceHash = new string('a', 64);
        await new PostgresMatchProjectionStore(source).SaveMatchMetadataAsync(
            new MatchSourceMetadata(matchId.Value, "local-e2e-fixture-v1", "local-e2e-test-source-v1", sourceHash,
                "synthetic-test-only", 105m, 68m, ReplaySeed: aggregate.ReplaySeed), CancellationToken.None);
        var writer = new CanonicalMatchEventWriter(new PostgresCanonicalUpdateStore(source));

        var homePlayerId = "home-test-player-1";
        var kickoff = MatchEvent.Create(Guid.Parse("11111111-1111-4111-8111-111111111111"), matchId, 0,
            MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch);
        await writer.AppendAsync(aggregate, new SimulationEvent(kickoff, "home-test", homePlayerId, "KickOff", "complete"),
            1, "local-e2e-test-projector-v1", sourceHash, CancellationToken.None);
        var observedEvent = MatchEvent.Create(eventId, matchId, 1, MatchEventType.Pass, 10, 1,
            DateTimeOffset.UnixEpoch.AddSeconds(10));
        await writer.AppendAsync(aggregate, new SimulationEvent(observedEvent, "home-test", homePlayerId, "Pass", "complete"),
            2, "local-e2e-test-projector-v1", sourceHash, CancellationToken.None);
        await using (var rights = source.CreateCommand("""
            UPDATE pitchmind.match_events
            SET payload = payload || '{"source":{"rightsReviewStatus":"approved_for_use"}}'::jsonb
            WHERE match_id = $1 AND event_id = $2
            """))
        {
            rights.Parameters.AddWithValue(matchId.Value);
            rights.Parameters.AddWithValue(eventId);
            Assert.Equal(1, await rights.ExecuteNonQueryAsync());
        }

        await using (var insight = source.CreateCommand("""
            INSERT INTO pitchmind.insights(insight_id, match_id, canonical_revision, revision, as_of_sequence,
                detector_version, truth_class, summary, conditions_json)
            VALUES ($1, $2, 2, 2, 1, 'local-e2e-test-detector-v1', 'simulated',
                'Test-only evidence record. No match claim.', '{"fixture":"synthetic-test-only"}'::jsonb)
            """))
        {
            insight.Parameters.AddWithValue(insightId);
            insight.Parameters.AddWithValue(matchId.Value);
            await insight.ExecuteNonQueryAsync();
        }
        await using (var evidence = source.CreateCommand("""
            INSERT INTO pitchmind.insight_evidence(insight_id, evidence_kind, evidence_id, match_id,
                canonical_revision, evidence_canonical_revision, revision, relation, event_id)
            VALUES ($1, 'event', $2, $3, 2, 2, 2, 'supports', $4)
            """))
        {
            evidence.Parameters.AddWithValue(insightId);
            evidence.Parameters.AddWithValue(eventId.ToString("D"));
            evidence.Parameters.AddWithValue(matchId.Value);
            evidence.Parameters.AddWithValue(eventId);
            Assert.Equal(1, await evidence.ExecuteNonQueryAsync());
        }

        var insightReader = new PostgresMatchInsightReader(source);
        var persisted = await insightReader.GetAtAsync(matchId, insightId, 2, 2, 10, 1, CancellationToken.None);
        Assert.NotNull(persisted);
        using var persistedConditions = JsonDocument.Parse(persisted.ConditionsJson);
        Assert.Equal("synthetic-test-only", persistedConditions.RootElement.GetProperty("fixture").GetString());
        await using (var malformedConditions = source.CreateCommand("UPDATE pitchmind.insights SET conditions_json = '[]'::jsonb WHERE insight_id = $1"))
        {
            malformedConditions.Parameters.AddWithValue(insightId);
            Assert.Equal(1, await malformedConditions.ExecuteNonQueryAsync());
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => insightReader.GetAtAsync(
            matchId, insightId, 2, 2, 10, 1, CancellationToken.None));
        await using (var restoreConditions = source.CreateCommand("UPDATE pitchmind.insights SET conditions_json = '{\"fixture\":\"synthetic-test-only\"}'::jsonb WHERE insight_id = $1"))
        {
            restoreConditions.Parameters.AddWithValue(insightId);
            Assert.Equal(1, await restoreConditions.ExecuteNonQueryAsync());
        }
    }

    [PostgresIntegrationFact]
    public async Task TrackingFrameReaderUsesExactTimeRevisionSourceAndStableCompositePagination()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);

        var matchGuid = Guid.NewGuid();
        var matchId = new MatchId(matchGuid);
        const string sourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await using (var metadata = source.CreateCommand("""
            INSERT INTO pitchmind.matches(match_id, fixture_version, source_version, source_sha256, provenance,
                pitch_length_metres, pitch_width_metres)
            VALUES ($1, 'fixture-v1', 'source-v1', $2, 'synthetic', 105, 68)
            """))
        {
            metadata.Parameters.AddWithValue(matchGuid);
            metadata.Parameters.AddWithValue(sourceHash);
            await metadata.ExecuteNonQueryAsync();
        }
        await using (var projection = source.CreateCommand("""
            INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence,
                as_of_clock_seconds, algorithm_version, source_sha256, state_json, state_sha256)
            VALUES ($1, 9, 3, 5, 120, 'aggregate-v1', $2, '{}'::jsonb, $3)
            """))
        {
            projection.Parameters.AddWithValue(matchGuid);
            projection.Parameters.AddWithValue(sourceHash);
            projection.Parameters.AddWithValue(new string('b', 64));
            await projection.ExecuteNonQueryAsync();
        }

        async Task AddFrame(string frameId, long sequence, long canonicalRevision, int clockSeconds,
            string rightsReviewStatus = "approved_for_use")
        {
            var payload = JsonSerializer.Serialize(new
            {
                matchId = matchGuid.ToString("D"), frameId, sequence, canonicalRevision,
                matchTime = new { elapsedSeconds = clockSeconds, period = 2 },
                source = new { sourceSha256 = sourceHash, rightsReviewStatus }
            });
            await using var command = source.CreateCommand("""
                INSERT INTO pitchmind.tracking_frames(match_id, frame_id, canonical_revision, sequence, period,
                    clock_seconds, coordinate_version, quality, source_sha256, payload)
                VALUES ($1, $2, $3, $4, 2, $5, 'pitch-v1', '{}'::jsonb, $6, $7::jsonb)
                """);
            command.Parameters.AddWithValue(matchGuid);
            command.Parameters.AddWithValue(frameId);
            command.Parameters.AddWithValue(canonicalRevision);
            command.Parameters.AddWithValue(sequence);
            command.Parameters.AddWithValue(clockSeconds);
            command.Parameters.AddWithValue(sourceHash);
            command.Parameters.AddWithValue(payload);
            await command.ExecuteNonQueryAsync();
        }

        await AddFrame("frame-b", 3, 9, 120);
        await AddFrame("frame-a", 3, 9, 120);
        await AddFrame("old-source-revision", 2, 8, 121);
        await AddFrame("future-revision", 4, 10, 120);
        await AddFrame("future-sequence", 6, 9, 120);
        await AddFrame("other-time", 4, 9, 121);
        await AddFrame("pending-rights", 4, 9, 120, "pending");

        var selectedProjection = new SelectedMatchProjection(
            new MatchState(matchGuid.ToString("D"), 120, 2, MatchPhase.SecondHalf, 0, 0, null,
                11, 11, Array.Empty<MatchPlayerState>(), 6, "synthetic-test"),
            9, 3, 5, 120, "rules-v1", sourceHash, new string('b', 64));
        var reader = new PostgresMatchTrackingReader(source);
        var first = await reader.ReadAtAsync(selectedProjection, 120, null, null, 1, CancellationToken.None);
        Assert.Single(first.Frames);
        Assert.Equal("frame-a", first.Frames[0].FrameId);
        Assert.True(first.HasMore);
        Assert.Equal(3, first.NextAfterSequence);
        Assert.Equal("frame-a", first.NextAfterFrameId);

        var second = await reader.ReadAtAsync(selectedProjection, 120, first.NextAfterSequence,
            first.NextAfterFrameId, 1, CancellationToken.None);
        Assert.Single(second.Frames);
        Assert.Equal("frame-b", second.Frames[0].FrameId);
        Assert.False(second.HasMore);
        Assert.Null(await reader.ReadByIdAtAsync(selectedProjection, 120, "pending-rights", null, CancellationToken.None));

        var targeted = await reader.ReadByIdAtAsync(selectedProjection, 120, "frame-b", null, CancellationToken.None);
        Assert.NotNull(targeted);
        Assert.Equal("frame-b", targeted.FrameId);
        Assert.Equal(3, targeted.Sequence);
        var exactSource = await reader.ReadByIdAtAsync(selectedProjection, 121, "old-source-revision", 8, CancellationToken.None);
        Assert.NotNull(exactSource);
        Assert.Equal(8, exactSource.CanonicalRevision);
        Assert.Null(await reader.ReadByIdAtAsync(selectedProjection, 121, "old-source-revision", 7, CancellationToken.None));
    }

    [PostgresIntegrationFact]
    public Task TrackingFrameReaderRejectsOversizePayloadBeforeReturningIt() =>
        AssertTrackingFrameReaderRejectsOversizeJsonAsync("payload");

    [PostgresIntegrationFact]
    public Task TrackingFrameReaderRejectsOversizeQualityBeforeReturningIt() =>
        AssertTrackingFrameReaderRejectsOversizeJsonAsync("quality");

    private static async Task AssertTrackingFrameReaderRejectsOversizeJsonAsync(string oversizedField)
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);

        var matchGuid = Guid.NewGuid();
        const string sourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        await using (var metadata = source.CreateCommand("""
            INSERT INTO pitchmind.matches(match_id, fixture_version, source_version, source_sha256, provenance,
                pitch_length_metres, pitch_width_metres)
            VALUES ($1, 'fixture-v1', 'source-v1', $2, 'synthetic', 105, 68)
            """))
        {
            metadata.Parameters.AddWithValue(matchGuid);
            metadata.Parameters.AddWithValue(sourceHash);
            await metadata.ExecuteNonQueryAsync();
        }
        await using (var projection = source.CreateCommand("""
            INSERT INTO pitchmind.match_projections(match_id, canonical_revision, revision, as_of_sequence,
                as_of_clock_seconds, algorithm_version, source_sha256, state_json, state_sha256)
            VALUES ($1, 9, 3, 5, 120, 'aggregate-v1', $2, '{}'::jsonb, $3)
            """))
        {
            projection.Parameters.AddWithValue(matchGuid);
            projection.Parameters.AddWithValue(sourceHash);
            projection.Parameters.AddWithValue(new string('b', 64));
            await projection.ExecuteNonQueryAsync();
        }

        var payload = JsonSerializer.Serialize(new
        {
            matchId = matchGuid.ToString("D"), frameId = "oversize-frame", sequence = 3L, canonicalRevision = 9L,
            matchTime = new { elapsedSeconds = 120, period = 2 },
            source = new { sourceSha256 = sourceHash, rightsReviewStatus = "approved_for_use" },
            padding = new string('x', oversizedField == "payload" ? 40_000 : 8)
        });
        var quality = JsonSerializer.Serialize(new
        {
            padding = new string('x', oversizedField == "quality" ? 5_000 : 8)
        });
        await using (var insertFrame = source.CreateCommand("""
            INSERT INTO pitchmind.tracking_frames(match_id, frame_id, canonical_revision, sequence, period,
                clock_seconds, coordinate_version, quality, source_sha256, payload)
            VALUES ($1, 'oversize-frame', 9, 3, 2, 120, 'pitch-v1', $4::jsonb, $2, $3::jsonb)
            """))
        {
            insertFrame.Parameters.AddWithValue(matchGuid);
            insertFrame.Parameters.AddWithValue(sourceHash);
            insertFrame.Parameters.AddWithValue(payload);
            insertFrame.Parameters.AddWithValue(quality);
            await insertFrame.ExecuteNonQueryAsync();
        }

        var selectedProjection = new SelectedMatchProjection(
            new MatchState(matchGuid.ToString("D"), 120, 2, MatchPhase.SecondHalf, 0, 0, null,
                11, 11, Array.Empty<MatchPlayerState>(), 6, "synthetic-test"),
            9, 3, 5, 120, "rules-v1", sourceHash, new string('b', 64));
        var reader = new PostgresMatchTrackingReader(source);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAtAsync(
            selectedProjection, 120, null, null, 1, CancellationToken.None));
    }

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
  …22201 tokens truncated…         frame.Parameters.AddWithValue(matchId.Value);
            frame.Parameters.AddWithValue(frameId);
            frame.Parameters.AddWithValue(sequence);
            frame.Parameters.AddWithValue(update.SourceSha256);
            frame.Parameters.AddWithValue(rightsStatus);
            await frame.ExecuteNonQueryAsync();
        }
        await AddInsight(validInsightId, "derived", "event", update.Event.EventId.ToString(), 1, 1, 0, 1);
        await AddInsight(validFrameInsightId, "derived", "frame", validFrameId, 1, 1, 0, 1);
        await AddInsight(futureEventInsightId, "derived", "event", futureEvent.EventId.ToString(), 1, 1, 0, 2);
        await AddInsight(metricInsightId, "derived", "condition", "unpersisted-metric", 1, 1, 0, null);
        await AddInsight(futureFrameInsightId, "derived", "frame", futureFrameId, 1, 1, 0, 1);
        await AddInsight(pendingFrameInsightId, "derived", "frame", pendingFrameId, 1, 1, 0, 1);
        await AddInsight(pendingEventInsightId, "derived", "event", futureEvent.EventId.ToString(), 2, 2, 1, 2);
        await using (var missingRightsFrame = source.CreateCommand("""
            INSERT INTO pitchmind.tracking_frames(match_id, frame_id, sequence, period, clock_seconds, coordinate_version, quality, source_sha256, payload, canonical_revision)
            VALUES ($1, $2, 0, 1, 0, 'pitch-v1', '{}'::jsonb, $3, '{}'::jsonb, 1)
            """))
        {
            missingRightsFrame.Parameters.AddWithValue(matchId.Value);
            missingRightsFrame.Parameters.AddWithValue(missingRightsFrameId);
            missingRightsFrame.Parameters.AddWithValue(update.SourceSha256);
            await missingRightsFrame.ExecuteNonQueryAsync();
        }
        await AddInsight(missingRightsFrameInsightId, "derived", "frame", missingRightsFrameId, 1, 1, 0, 1);
        await AddInsight(earlierEventInsightId, "derived", "event", update.Event.EventId.ToString(), 2, 2, 0, 1);
        await AddInsight(earlierFrameInsightId, "derived", "frame", validFrameId, 2, 2, 0, 1);

        var projectionReader = new PostgresMatchProjectionReader(source);
        var projection = await projectionReader.GetForApiAtAsync(matchId, 0, 1, null, CancellationToken.None);
        Assert.NotNull(projection);
        var insightReader = new PostgresMatchInsightReader(source);
        var page = await insightReader.ReadAtAsync(matchId, projection.CanonicalRevision, projection.ProjectionRevision,
            0, projection.AsOfSequence,
            null, null, 10, CancellationToken.None);
        Assert.Equal(new[] { validFrameInsightId, validInsightId }.OrderBy(i => i).ToArray(),
            page.Insights.Select(i => i.InsightId).OrderBy(i => i).ToArray());
        var visible = Assert.Single(page.Insights, i => i.InsightId == validInsightId);
        Assert.Equal(update.Event.EventId.ToString(), Assert.Single(visible.Evidence).Id);
        Assert.Equal("source-v1", visible.SourceVersion);
        Assert.Equal("approved-synthetic-test", visible.Provenance);
        Assert.Equal(update.SourceSha256, visible.SourceSha256);
        Assert.Null(await insightReader.GetAtAsync(matchId, futureEventInsightId, projection.CanonicalRevision, projection.ProjectionRevision, 0,
            projection.AsOfSequence, CancellationToken.None));
        Assert.Null(await insightReader.GetAtAsync(matchId, metricInsightId, projection.CanonicalRevision, projection.ProjectionRevision, 0,
            projection.AsOfSequence, CancellationToken.None));
        Assert.Null(await insightReader.GetAtAsync(matchId, futureFrameInsightId, projection.CanonicalRevision, projection.ProjectionRevision, 0,
            projection.AsOfSequence, CancellationToken.None));
        Assert.Null(await insightReader.GetAtAsync(matchId, pendingFrameInsightId, projection.CanonicalRevision, projection.ProjectionRevision, 0,
            projection.AsOfSequence, CancellationToken.None));
        Assert.Null(await insightReader.GetAtAsync(matchId, missingRightsFrameInsightId, projection.CanonicalRevision,
            projection.ProjectionRevision, 0, projection.AsOfSequence, CancellationToken.None));
        var validFrameEvidence = await insightReader.ReadEvidenceAtAsync(matchId, validFrameInsightId,
            projection.CanonicalRevision, projection.ProjectionRevision, 0, projection.AsOfSequence, CancellationToken.None);
        var framePayload = Assert.Single(validFrameEvidence!);
        Assert.Equal(validFrameId, framePayload.Id);
        Assert.Equal(0, framePayload.Sequence);
        Assert.Equal(0, framePayload.ClockSeconds);
        Assert.Equal(1, framePayload.Period);
        Assert.Equal("{\"source\": {\"rightsReviewStatus\": \"approved_for_use\"}}", framePayload.PayloadJson);
        var eventEvidence = await insightReader.ReadEvidenceAtAsync(matchId, validInsightId,
            projection.CanonicalRevision, projection.ProjectionRevision, 0, projection.AsOfSequence, CancellationToken.None);
        Assert.Equal(update.Event.EventId.ToString(), Assert.Single(eventEvidence!).Id);
        Assert.Null(await insightReader.ReadEvidenceAtAsync(matchId, futureFrameInsightId, projection.CanonicalRevision,
            projection.ProjectionRevision, 0,
            projection.AsOfSequence, CancellationToken.None));
        Assert.Null(await insightReader.ReadEvidenceAtAsync(matchId, pendingFrameInsightId, projection.CanonicalRevision,
            projection.ProjectionRevision, 0, projection.AsOfSequence, CancellationToken.None));
        Assert.Null(await insightReader.ReadEvidenceAtAsync(matchId, missingRightsFrameInsightId, projection.CanonicalRevision,
            projection.ProjectionRevision, 0, projection.AsOfSequence, CancellationToken.None));

        // Evidence visible to the later projection still cannot support an insight persisted earlier.
        var laterProjection = await projectionReader.GetForApiAtAsync(matchId, 10, null, null, CancellationToken.None);
        Assert.NotNull(laterProjection);
        Assert.Equal(1, laterProjection.AsOfSequence);
        foreach (var futureInsightId in new[] { futureEventInsightId, futureFrameInsightId })
        {
            Assert.Null(await insightReader.GetAtAsync(matchId, futureInsightId, laterProjection.CanonicalRevision,
                laterProjection.ProjectionRevision, 10,
                laterProjection.AsOfSequence, CancellationToken.None));
            Assert.Null(await insightReader.ReadEvidenceAtAsync(matchId, futureInsightId, laterProjection.CanonicalRevision,
                laterProjection.ProjectionRevision, 10,
                laterProjection.AsOfSequence, CancellationToken.None));
        }
        var laterPage = await insightReader.ReadAtAsync(matchId, laterProjection.CanonicalRevision, laterProjection.ProjectionRevision,
            10, laterProjection.AsOfSequence,
            null, null, 10, CancellationToken.None);
        Assert.DoesNotContain(laterPage.Insights, insight => insight.InsightId == futureEventInsightId);
        Assert.DoesNotContain(laterPage.Insights, insight => insight.InsightId == futureFrameInsightId);
        Assert.DoesNotContain(laterPage.Insights, insight => insight.InsightId == pendingFrameInsightId);
        Assert.DoesNotContain(laterPage.Insights, insight => insight.InsightId == missingRightsFrameInsightId);
        Assert.DoesNotContain(laterPage.Insights, insight => insight.InsightId == pendingEventInsightId);
        Assert.Null(await insightReader.GetAtAsync(matchId, pendingEventInsightId, laterProjection.CanonicalRevision,
            laterProjection.ProjectionRevision, 10, laterProjection.AsOfSequence, CancellationToken.None));
        Assert.Null(await insightReader.ReadEvidenceAtAsync(matchId, pendingEventInsightId, laterProjection.CanonicalRevision,
            laterProjection.ProjectionRevision, 10, laterProjection.AsOfSequence, CancellationToken.None));
        Assert.Contains(laterPage.Insights, insight => insight.InsightId == earlierEventInsightId);
        Assert.Contains(laterPage.Insights, insight => insight.InsightId == earlierFrameInsightId);
        var earlierEvent = Assert.Single((await insightReader.ReadEvidenceAtAsync(matchId, earlierEventInsightId,
            laterProjection.CanonicalRevision, laterProjection.ProjectionRevision, 10,
            laterProjection.AsOfSequence, CancellationToken.None))!);
        Assert.Equal(1, earlierEvent.SourceCanonicalRevision);
        Assert.Equal(update.Event.EventId.ToString(), earlierEvent.Id);
        var earlierFrame = Assert.Single((await insightReader.ReadEvidenceAtAsync(matchId, earlierFrameInsightId,
            laterProjection.CanonicalRevision, laterProjection.ProjectionRevision, 10,
            laterProjection.AsOfSequence, CancellationToken.None))!);
        Assert.Equal(1, earlierFrame.SourceCanonicalRevision);
        Assert.Equal(validFrameId, earlierFrame.Id);
    }

    private static async Task<List<MatchEvent>> ReadSelected(IMatchProjectionReader reader,
        SelectedMatchProjection projection, int selectedSeconds)
    {
        var events = new List<MatchEvent>();
        await foreach (var item in reader.ReadEventsAtAsync(projection, selectedSeconds, CancellationToken.None)) events.Add(item);
        return events;
    }

    private static async Task<List<MatchReplayFrame>> ReadReplayFrames(IMatchProjectionReader reader,
        SelectedMatchProjection projection, int selectedSeconds)
    {
        var frames = new List<MatchReplayFrame>();
        await foreach (var item in reader.ReadReplayFramesAtAsync(projection, selectedSeconds, CancellationToken.None)) frames.Add(item);
        return frames;
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

    private sealed class OutboxMatchCleanup(NpgsqlDataSource source, MatchId matchId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using var command = source.CreateCommand("DELETE FROM pitchmind.outbox WHERE match_id = $1");
            command.Parameters.AddWithValue(matchId.Value);
            await command.ExecuteNonQueryAsync();
        }
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
        var conflict = await Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(evt with { ClockSeconds = 42 }, CancellationToken.None));
        Assert.Equal("EVENT_ID_PAYLOAD_CONFLICT", conflict.Code);
        Assert.Equal(MatchEventIngestionDisposition.Quarantine, conflict.Disposition);
        Assert.Equal(evt, Assert.Single(await Read(store, matchId)));
    }
    [PostgresIntegrationFact]
    public async Task SequenceConflictsAndGapsRollbackWithoutChangingLedger()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source); var matchId = MatchId.New(); var first = Event(matchId);
        var gap = await Assert.ThrowsAsync<MatchEventIngestionException>(() => store.AppendAsync(Event(matchId, 2), CancellationToken.None));
        Assert.Equal("SEQUENCE_GAP", gap.Code);
        Assert.Equal(MatchEventIngestionDisposition.HoldForReconciliation, gap.Disposition);
        Assert.Empty(await Read(store, matchId));
        await store.AppendAsync(first, CancellationToken.None);
        var usedSequence = await Assert.ThrowsAsync<MatchEventIngestionException>(() => store.AppendAsync(Event(matchId), CancellationToken.None));
        Assert.Equal("SEQUENCE_ALREADY_USED", usedSequence.Code);
        Assert.Equal(MatchEventIngestionDisposition.Quarantine, usedSequence.Disposition);
        gap = await Assert.ThrowsAsync<MatchEventIngestionException>(() => store.AppendAsync(Event(matchId, 2), CancellationToken.None));
        Assert.Equal("SEQUENCE_GAP", gap.Code);
        Assert.Equal(MatchEventIngestionDisposition.HoldForReconciliation, gap.Disposition);
        var next = Event(matchId, 1); await store.AppendAsync(next, CancellationToken.None);
        Assert.Equal(new[] { first, next }, await Read(store, matchId));
    }

    [PostgresIntegrationFact]
    public async Task OutOfOrderEventAgainstLedgerGapIsHeldForReconciliation()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source); var matchId = MatchId.New();
        await store.AppendAsync(Event(matchId), CancellationToken.None);
        var later = Event(matchId, 2);
        await using (var command = source.CreateCommand("""
            INSERT INTO pitchmind.match_events(event_id, match_id, sequence, event_type, clock_seconds, period, occurred_at, payload)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """))
        {
            command.Parameters.AddWithValue(later.EventId);
            command.Parameters.AddWithValue(matchId.Value);
            command.Parameters.AddWithValue(later.Sequence);
            command.Parameters.AddWithValue((int)later.Type);
            command.Parameters.AddWithValue(later.ClockSeconds);
            command.Parameters.AddWithValue(later.Period);
            command.Parameters.AddWithValue(later.OccurredAt.ToUniversalTime());
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(later));
            await command.ExecuteNonQueryAsync();
        }

        var outOfOrder = await Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(Event(matchId, 1), CancellationToken.None));

        Assert.Equal("OUT_OF_ORDER_EVENT", outOfOrder.Code);
        Assert.Equal(MatchEventIngestionDisposition.HoldForReconciliation, outOfOrder.Disposition);
        Assert.Equal<long?>(3, outOfOrder.ExpectedSequence);
        Assert.Equal(1L, outOfOrder.ReceivedSequence);
        await using var countCommand = source.CreateCommand("SELECT COUNT(*) FROM pitchmind.match_events WHERE match_id = $1");
        countCommand.Parameters.AddWithValue(matchId.Value);
        Assert.Equal(2L, (long)(await countCommand.ExecuteScalarAsync())!);
    }

    [PostgresIntegrationFact]
    public async Task EventIdConflictAcrossMatchesIsSerializedAndNeverMutatesTheSecondLedger()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source);
        var eventId = Guid.NewGuid();
        var first = Event(MatchId.New()) with { EventId = eventId };
        var second = Event(MatchId.New()) with { EventId = eventId };

        async Task<(MatchEvent? Appended, MatchEventIngestionException? Rejected)> TryAppend(MatchEvent matchEvent)
        {
            try { await store.AppendAsync(matchEvent, CancellationToken.None); return (matchEvent, null); }
            catch (MatchEventIngestionException exception) { return (null, exception); }
        }

        var results = await Task.WhenAll(TryAppend(first), TryAppend(second));
        var rejected = Assert.Single(results, result => result.Rejected is not null).Rejected!;
        var winner = Assert.Single(results, result => result.Appended is not null).Appended!;
        Assert.Equal("EVENT_ID_PAYLOAD_CONFLICT", rejected.Code);
        Assert.Equal(MatchEventIngestionDisposition.Quarantine, rejected.Disposition);
        Assert.Single(await Read(store, winner.MatchId));
        var loser = winner.MatchId == first.MatchId ? second : first;
        Assert.Empty(await Read(store, loser.MatchId));
    }

    [PostgresIntegrationFact]
    public async Task OffsetDistinctTimestampForSameInstantIsRejectedWithoutChangingLedger()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source);
        var original = MatchEvent.Create(Guid.NewGuid(), MatchId.New(), 0, MatchEventType.KickOff, 0, 1,
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        await store.AppendAsync(original, CancellationToken.None);
        var offsetDistinct = original with { OccurredAt = original.OccurredAt.ToOffset(TimeSpan.FromHours(1)) };

        var conflict = await Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(offsetDistinct, CancellationToken.None));

        Assert.Equal("EVENT_ID_PAYLOAD_CONFLICT", conflict.Code);
        var persisted = Assert.Single(await Read(store, original.MatchId));
        Assert.True(persisted.OccurredAt.EqualsExact(original.OccurredAt));
    }

    [PostgresIntegrationFact]
    public async Task InvalidEventGetsStableQuarantineClassificationBeforePersistence()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source);
        var invalid = Event(MatchId.New()) with { Type = (MatchEventType)999 };

        var exception = await Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(invalid, CancellationToken.None));

        Assert.Equal("INVALID_EVENT_PAYLOAD", exception.Code);
        Assert.Equal(MatchEventIngestionDisposition.Quarantine, exception.Disposition);
        Assert.Empty(await Read(store, invalid.MatchId));
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
