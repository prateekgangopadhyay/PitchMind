using Npgsql;
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
