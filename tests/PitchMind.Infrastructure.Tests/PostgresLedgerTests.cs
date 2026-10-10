using Npgsql;
using NpgsqlTypes;
using System.Text.Json;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Infrastructure.Storage;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class PostgresIntegrationFactAttribute : FactAttribute
{
    public PostgresIntegrationFactAttribute()
    {
        var connectionString = Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
            Skip = "Set PITCHMIND_TEST_POSTGRES to an isolated local PostgreSQL test database; not evidence of a passing integration test.";
        else if (!PostgresTestDatabaseGuard.TryValidate(connectionString, out var reason))
            Skip = $"Unsafe PostgreSQL test target: {reason}";
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
    private static NpgsqlDataSource Source() => NpgsqlDataSource.Create(
        PostgresTestDatabaseGuard.RequireSafeConnectionString(Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES")));

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
    public async Task QuarantineEvidenceSurvivesReopenAndMigrationCanBeReapplied()
    {
        var record = new QuarantineRecord(Guid.NewGuid(), null, "fixture/events/17", new string('a', 64),
            "INVALID_EVENT_PAYLOAD", new DateTimeOffset(2026, 10, 11, 12, 30, 0, TimeSpan.FromHours(5.5)));
        await using (var source = Source())
        {
            await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
            await new PostgresQuarantineRecordStore(source).AppendAsync(record, CancellationToken.None);
            await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        }

        await using var reopened = Source();
        var store = new PostgresQuarantineRecordStore(reopened);
        Assert.Equal(record with { ObservedAt = record.ObservedAt.ToUniversalTime() },
            await store.ReadAsync(record.RecordId, CancellationToken.None));
        await using var metadata = reopened.CreateCommand("""
            SELECT (SELECT COUNT(*) FROM pitchmind.schema_versions WHERE version = 3),
                   (SELECT retry_status IS NULL FROM pitchmind.quarantine WHERE record_id = $1)
            """);
        metadata.Parameters.AddWithValue(record.RecordId);
        await using var reader = await metadata.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(reader.GetBoolean(1));
    }

    [PostgresIntegrationFact]
    public async Task QuarantineV4HardensAnAlreadyRecordedV3TableAndPreservesRows()
    {
        await using var source = Source();
        await using (var reset = source.CreateCommand("""
            DROP TABLE IF EXISTS pitchmind.quarantine;
            DROP TABLE IF EXISTS pitchmind.match_events;
            DROP TABLE IF EXISTS pitchmind.schema_versions;
            DROP FUNCTION IF EXISTS pitchmind.reject_quarantine_mutation();
            """))
            await reset.ExecuteNonQueryAsync();
        await using (var v1 = source.CreateCommand(PostgresSchemaMigrator.SchemaSql))
            await v1.ExecuteNonQueryAsync();
        await using (var v2 = source.CreateCommand(PostgresSchemaMigrator.EventTypeRangeV2Sql))
            await v2.ExecuteNonQueryAsync();
        await using (var v3 = source.CreateCommand(PostgresSchemaMigrator.QuarantineV3Sql))
            await v3.ExecuteNonQueryAsync();
        var legacyId = Guid.NewGuid();
        await using (var legacyRow = source.CreateCommand("""
            INSERT INTO pitchmind.quarantine(record_id, source_ptr, reason, observed_at)
            VALUES ($1, $2, $3, $4)
            """))
        {
            legacyRow.Parameters.AddWithValue(legacyId);
            legacyRow.Parameters.AddWithValue("legacy/v3/item");
            legacyRow.Parameters.AddWithValue("LEGACY_V3_RECORD");
            legacyRow.Parameters.AddWithValue(DateTimeOffset.UtcNow);
            await legacyRow.ExecuteNonQueryAsync();
        }

        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);

        await using var versions = source.CreateCommand("SELECT COUNT(*) FROM pitchmind.schema_versions WHERE version IN (3, 4)");
        Assert.Equal(2L, (long)(await versions.ExecuteScalarAsync())!);
        await using var preserved = source.CreateCommand("SELECT COUNT(*) FROM pitchmind.quarantine WHERE record_id = $1");
        preserved.Parameters.AddWithValue(legacyId);
        Assert.Equal(1L, (long)(await preserved.ExecuteScalarAsync())!);
        await using var zeroId = source.CreateCommand("""
            INSERT INTO pitchmind.quarantine(record_id, source_ptr, reason, observed_at)
            VALUES ($1, $2, $3, $4)
            """);
        zeroId.Parameters.AddWithValue(Guid.Empty);
        zeroId.Parameters.AddWithValue("legacy/v3/zero-id");
        zeroId.Parameters.AddWithValue("INVALID_EVENT");
        zeroId.Parameters.AddWithValue(DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<PostgresException>(() => zeroId.ExecuteNonQueryAsync());
    }

    [PostgresIntegrationFact]
    public async Task QuarantineV4RejectsLegacyZeroIdAndRollsBackWithoutDataLoss()
    {
        await using var source = Source();
        await using (var reset = source.CreateCommand("""
            DROP TABLE IF EXISTS pitchmind.quarantine;
            DROP TABLE IF EXISTS pitchmind.match_events;
            DROP TABLE IF EXISTS pitchmind.schema_versions;
            DROP FUNCTION IF EXISTS pitchmind.reject_quarantine_mutation();
            """))
            await reset.ExecuteNonQueryAsync();
        await using (var v1 = source.CreateCommand(PostgresSchemaMigrator.SchemaSql))
            await v1.ExecuteNonQueryAsync();
        await using (var v2 = source.CreateCommand(PostgresSchemaMigrator.EventTypeRangeV2Sql))
            await v2.ExecuteNonQueryAsync();
        await using (var v3 = source.CreateCommand(PostgresSchemaMigrator.QuarantineV3Sql))
            await v3.ExecuteNonQueryAsync();

        await using (var legacyRow = source.CreateCommand("""
            INSERT INTO pitchmind.quarantine(record_id, source_ptr, reason, observed_at)
            VALUES ($1, $2, $3, $4)
            """))
        {
            legacyRow.Parameters.AddWithValue(Guid.Empty);
            legacyRow.Parameters.AddWithValue("legacy/v3/zero-id");
            legacyRow.Parameters.AddWithValue("INVALID_LEGACY_ID");
            legacyRow.Parameters.AddWithValue(DateTimeOffset.UtcNow);
            await legacyRow.ExecuteNonQueryAsync();
        }

        var migrationFailure = await Assert.ThrowsAsync<PostgresException>(
            () => PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None));
        Assert.Equal(PostgresErrorCodes.CheckViolation, migrationFailure.SqlState);

        await using (var rollbackEvidence = source.CreateCommand("""
            SELECT (SELECT COUNT(*) FROM pitchmind.schema_versions WHERE version = 3),
                   (SELECT COUNT(*) FROM pitchmind.schema_versions WHERE version = 4),
                   (SELECT COUNT(*) FROM pitchmind.quarantine WHERE record_id = $1),
                   (SELECT COUNT(*) FROM pg_constraint
                    WHERE conrelid = 'pitchmind.quarantine'::regclass AND conname = 'quarantine_record_id_nonzero'),
                   (SELECT COUNT(*) FROM pg_trigger
                    WHERE tgrelid = 'pitchmind.quarantine'::regclass
                      AND tgname IN ('quarantine_immutable', 'quarantine_no_truncate')),
                   (SELECT to_regprocedure('pitchmind.reject_quarantine_mutation()') IS NULL)
            """))
        {
            rollbackEvidence.Parameters.AddWithValue(Guid.Empty);
            await using var reader = await rollbackEvidence.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal(0L, reader.GetInt64(1));
            Assert.Equal(1L, reader.GetInt64(2));
            Assert.Equal(0L, reader.GetInt64(3));
            Assert.Equal(0L, reader.GetInt64(4));
            Assert.True(reader.GetBoolean(5));
        }

        await using (var removeInvalidTestRow = source.CreateCommand(
            "DELETE FROM pitchmind.quarantine WHERE record_id = $1 AND source_ptr = $2"))
        {
            removeInvalidTestRow.Parameters.AddWithValue(Guid.Empty);
            removeInvalidTestRow.Parameters.AddWithValue("legacy/v3/zero-id");
            Assert.Equal(1, await removeInvalidTestRow.ExecuteNonQueryAsync());
        }
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        await using var applied = source.CreateCommand("SELECT COUNT(*) FROM pitchmind.schema_versions WHERE version = 4");
        Assert.Equal(1L, (long)(await applied.ExecuteScalarAsync())!);
    }

    [PostgresIntegrationFact]
    public async Task QuarantineRejectsMalformedHashAndReasonWithoutWritingEvidence()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresQuarantineRecordStore(source);
        var record = new QuarantineRecord(Guid.NewGuid(), Guid.NewGuid(), "source/item/1", "not-a-hash",
            "invalid reason", DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync(record, CancellationToken.None));
        await using var count = source.CreateCommand("SELECT COUNT(*) FROM pitchmind.quarantine WHERE record_id = $1");
        count.Parameters.AddWithValue(record.RecordId);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [PostgresIntegrationFact]
    public async Task QuarantineDatabaseRejectsZeroRecordId()
    {
        await using var source = Source();
        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresQuarantineRecordStore(source);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AppendAsync(
            new QuarantineRecord(Guid.Empty, null, "source/item/2", null, "INVALID_EVENT", DateTimeOffset.UtcNow),
            CancellationToken.None));

        await using var zeroId = source.CreateCommand("""
            INSERT INTO pitchmind.quarantine(record_id, source_ptr, reason, observed_at)
            VALUES ($1, $2, $3, $4)
            """);
        zeroId.Parameters.AddWithValue(Guid.Empty);
        zeroId.Parameters.AddWithValue("source/item/zero-id");
        zeroId.Parameters.AddWithValue("INVALID_EVENT");
        zeroId.Parameters.AddWithValue(DateTimeOffset.UtcNow);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => zeroId.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
    }

    [PostgresIntegrationFact]
    public async Task QuarantineDatabaseRejectsMutationsForRoleWithExplicitTablePrivileges()
    {
        await using var migrationSource = Source();
        await PostgresSchemaMigrator.ApplyAsync(migrationSource, CancellationToken.None);
        var role = "quarantine_test_" + Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using (var setup = migrationSource.CreateCommand($"CREATE ROLE {role} LOGIN PASSWORD '{password}'; GRANT USAGE ON SCHEMA pitchmind TO {role}; GRANT SELECT, INSERT, UPDATE, DELETE, TRUNCATE ON pitchmind.quarantine TO {role};"))
            await setup.ExecuteNonQueryAsync();
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("PITCHMIND_TEST_POSTGRES")!)
            { Username = role, Password = password };
            await using var runtimeSource = NpgsqlDataSource.Create(builder.ConnectionString);
            var store = new PostgresQuarantineRecordStore(runtimeSource);
            var record = new QuarantineRecord(Guid.NewGuid(), null, "source/item/application-role", null,
                "INVALID_EVENT", new DateTimeOffset(2026, 10, 11, 12, 30, 0, TimeSpan.Zero));
            await store.AppendAsync(record, CancellationToken.None);
            Assert.Equal(record, await store.ReadAsync(record.RecordId, CancellationToken.None));

            foreach (var sql in new[]
            {
                "UPDATE pitchmind.quarantine SET reason = 'REPAIRED' WHERE record_id = $1",
                "DELETE FROM pitchmind.quarantine WHERE record_id = $1"
            })
            {
                await using var mutation = runtimeSource.CreateCommand(sql);
                mutation.Parameters.AddWithValue(record.RecordId);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync());
                Assert.Equal("55000", exception.SqlState);
            }

            await using (var truncate = runtimeSource.CreateCommand("TRUNCATE pitchmind.quarantine"))
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(() => truncate.ExecuteNonQueryAsync());
                Assert.Equal("55000", exception.SqlState);
            }
            Assert.Equal(record, await store.ReadAsync(record.RecordId, CancellationToken.None));
        }
        finally
        {
            await using var cleanup = migrationSource.CreateCommand($"REVOKE ALL PRIVILEGES ON TABLE pitchmind.quarantine FROM {role}; REVOKE USAGE ON SCHEMA pitchmind FROM {role}; DROP ROLE IF EXISTS {role}");
            await cleanup.ExecuteNonQueryAsync();
        }
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
    public async Task NewAppendsAgainstLegacyLedgerGapAreHeldUntilContinuityIsReconciled()
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

        var nextAfterMax = await Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(Event(matchId, 3), CancellationToken.None));
        Assert.Equal("SEQUENCE_GAP", nextAfterMax.Code);
        Assert.Equal(MatchEventIngestionDisposition.HoldForReconciliation, nextAfterMax.Disposition);
        Assert.Equal<long?>(1, nextAfterMax.ExpectedSequence);
        Assert.Equal(3L, nextAfterMax.ReceivedSequence);

        var missingSequence = await Assert.ThrowsAsync<MatchEventIngestionException>(
            () => store.AppendAsync(Event(matchId, 1), CancellationToken.None));
        Assert.Equal("SEQUENCE_GAP", missingSequence.Code);
        Assert.Equal(MatchEventIngestionDisposition.HoldForReconciliation, missingSequence.Disposition);
        Assert.Equal<long?>(1, missingSequence.ExpectedSequence);
        Assert.Equal(1L, missingSequence.ReceivedSequence);
        await using var countCommand = source.CreateCommand("SELECT COUNT(*) FROM pitchmind.match_events WHERE match_id = $1");
        countCommand.Parameters.AddWithValue(matchId.Value);
        Assert.Equal(2L, (long)(await countCommand.ExecuteScalarAsync())!);
    }

    [PostgresIntegrationFact]
    public async Task EventTypeRangeV2MigratesExistingV1SchemaAndAcceptsCurrentEnumValues()
    {
        await using var source = Source();
        await using (var createSchema = source.CreateCommand("CREATE SCHEMA IF NOT EXISTS pitchmind"))
            await createSchema.ExecuteNonQueryAsync();
        await using (var resetTables = source.CreateCommand("DROP TABLE IF EXISTS pitchmind.match_events; DROP TABLE IF EXISTS pitchmind.schema_versions"))
            await resetTables.ExecuteNonQueryAsync();
        await using (var createV1 = source.CreateCommand(PostgresSchemaMigrator.SchemaSql))
            await createV1.ExecuteNonQueryAsync();

        var store = new PostgresMatchEventStore(source);
        var matchId = MatchId.New();
        await Assert.ThrowsAsync<PostgresException>(
            () => store.AppendAsync(Event(matchId) with { Type = MatchEventType.Turnover }, CancellationToken.None));

        await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);

        var currentTypes = new[] { MatchEventType.Turnover, MatchEventType.FullTime, MatchEventType.Pressure };
        for (var sequence = 0; sequence < currentTypes.Length; sequence++)
            await store.AppendAsync(Event(matchId, sequence) with { Type = currentTypes[sequence] }, CancellationToken.None);

        Assert.Equal(currentTypes, (await Read(store, matchId)).Select(item => item.Type));
        await using var versionCommand = source.CreateCommand("SELECT COUNT(*) FROM pitchmind.schema_versions WHERE version IN (2, 3, 4)");
        Assert.Equal(3L, (long)(await versionCommand.ExecuteScalarAsync())!);
    }

    [PostgresIntegrationFact]
    public async Task EventIdConflictAcrossMatchesIsSerializedAndNeverMutatesTheSecondLedger()
    {
        await using var source = Source(); await PostgresSchemaMigrator.ApplyAsync(source, CancellationToken.None);
        var store = new PostgresMatchEventStore(source);
        var eventId = Guid.NewGuid();
        var first = Event(MatchId.New()) with { EventId = eventId };
        var second = Event(MatchId.New()) with { EventId = eventId };

        async Task<MatchEventIngestionException?> TryAppend(MatchEvent matchEvent)
        {
            try { await store.AppendAsync(matchEvent, CancellationToken.None); return null; }
            catch (MatchEventIngestionException exception) { return exception; }
        }

        var results = await Task.WhenAll(TryAppend(first), TryAppend(second));
        var rejected = Assert.Single(results, result => result is not null)!;
        Assert.Equal("EVENT_ID_PAYLOAD_CONFLICT", rejected.Code);
        Assert.Equal(MatchEventIngestionDisposition.Quarantine, rejected.Disposition);
        Assert.Single(await Read(store, first.MatchId));
        Assert.Empty(await Read(store, second.MatchId));
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
            foreach (var sql in new[] { "UPDATE pitchmind.match_events SET clock_seconds = 0", "DELETE FROM pitchmind.match_events", "CREATE TABLE pitchmind.runtime_forbidden(id int)" })
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
