using System.Text;
using PitchMind.Infrastructure.Feeds;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class LocalCandidateFeedReplaySessionTests
{
    [Fact]
    public async Task Session_pause_waits_for_acknowledgement_and_resume_finishes_in_source_order()
    {
        var document = await ReadFixtureAsync();
        var root = CreateTemporaryDirectory();
        var store = new FileCandidateFeedReplayCheckpointStore(root);
        var handled = new List<string>();
        var enteredFirst = NewSignal();
        var releaseFirst = NewSignal();
        await using var session = await LocalCandidateFeedReplaySession.CreateAsync(document, store,
            async (envelope, cancellationToken) =>
            {
                if (handled.Count == 0)
                {
                    enteredFirst.TrySetResult();
                    await releaseFirst.Task.WaitAsync(cancellationToken);
                }
                handled.Add(envelope.SourceRecord.SourceEventId);
            }, recordsPerSecond: 500);

        var completion = session.Start();
        await enteredFirst.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pause = session.PauseAsync();
        Assert.Equal(CandidateFeedReplaySessionState.Pausing, session.State);
        Assert.False(pause.IsCompleted);
        Assert.Null(await store.LoadAsync());

        releaseFirst.TrySetResult();
        await pause.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CandidateFeedReplaySessionState.Paused, session.State);
        var acknowledged = Assert.IsType<CandidateFeedReplayCursor>(await store.LoadAsync());
        Assert.Equal(1, acknowledged.NextSourceRecordIndex);
        Assert.Equal(document.Events[0].SourceSequence, acknowledged.LastSourceSequence);

        await session.ResumeAsync();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(CandidateFeedReplaySessionState.Completed, session.State);
        Assert.Equal(document.Events.Select(item => item.SourceEventId), handled);
    }

    [Fact]
    public async Task Session_cancel_during_consumer_does_not_acknowledge_record()
    {
        var document = await ReadFixtureAsync();
        var store = new FileCandidateFeedReplayCheckpointStore(CreateTemporaryDirectory());
        var entered = NewSignal();
        await using var session = await LocalCandidateFeedReplaySession.CreateAsync(document, store,
            async (_, cancellationToken) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }, recordsPerSecond: 500);

        var completion = session.Start();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.CancelAsync();

        Assert.Equal(CandidateFeedReplaySessionState.Canceled, session.State);
        Assert.Null(await store.LoadAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion);
    }

    [Fact]
    public async Task Session_rejects_checkpoint_from_different_source_before_consuming()
    {
        var document = await ReadFixtureAsync();
        var store = new FileCandidateFeedReplayCheckpointStore(CreateTemporaryDirectory());
        await store.SaveAsync(new CandidateFeedReplayCursor(
            document.ContractVersion, new string('0', 64), 0, null));
        var consumed = false;
        await using var session = await LocalCandidateFeedReplaySession.CreateAsync(document, store,
            (_, _) => { consumed = true; return ValueTask.CompletedTask; }, recordsPerSecond: 500);

        await Assert.ThrowsAsync<ArgumentException>(() => session.Start());
        Assert.False(consumed);
    }

    [Fact]
    public async Task Session_restart_resumes_after_last_acknowledged_source_record()
    {
        var document = await ReadFixtureAsync();
        var store = new FileCandidateFeedReplayCheckpointStore(CreateTemporaryDirectory());
        var firstRun = new List<string>();
        await using (var interrupted = await LocalCandidateFeedReplaySession.CreateAsync(document, store,
                         (envelope, _) =>
                         {
                             firstRun.Add(envelope.SourceRecord.SourceEventId);
                             if (firstRun.Count == 2) throw new IOException("Simulated consumer failure.");
                             return ValueTask.CompletedTask;
                         }, recordsPerSecond: 500))
        {
            await Assert.ThrowsAsync<IOException>(() => interrupted.Start());
            Assert.Equal(CandidateFeedReplaySessionState.Faulted, interrupted.State);
        }

        var restartedRun = new List<string>();
        await using var restarted = await LocalCandidateFeedReplaySession.CreateAsync(document, store,
            (envelope, _) =>
            {
                restartedRun.Add(envelope.SourceRecord.SourceEventId);
                return ValueTask.CompletedTask;
            }, recordsPerSecond: 500);

        await restarted.Start();

        Assert.Equal(new[] { document.Events[1].SourceEventId, document.Events[2].SourceEventId }, restartedRun);
    }

    [Fact]
    public async Task Checkpoint_store_atomically_replaces_and_round_trips_acknowledged_cursor()
    {
        var document = await ReadFixtureAsync();
        var root = CreateTemporaryDirectory();
        var store = new FileCandidateFeedReplayCheckpointStore(root);
        var first = new CandidateFeedReplayCursor(document.ContractVersion, document.SourceSha256, 1,
            document.Events[0].SourceSequence);
        var second = new CandidateFeedReplayCursor(document.ContractVersion, document.SourceSha256, 2,
            document.Events[1].SourceSequence);

        await store.SaveAsync(first);
        await store.SaveAsync(second);

        Assert.Equal(second, await store.LoadAsync());
        Assert.Equal(new[] { "candidate-feed-replay-checkpoint.json" },
            Directory.GetFiles(root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Checkpoint_store_rejects_corrupt_checkpoint_instead_of_restarting_from_zero()
    {
        var root = CreateTemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(root, "candidate-feed-replay-checkpoint.json"), "{bad json");
        var store = new FileCandidateFeedReplayCheckpointStore(root);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync());
    }

    [Fact]
    public async Task Checkpoint_store_rejects_symbolic_link_root()
    {
        var root = CreateTemporaryDirectory();
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        var store = new FileCandidateFeedReplayCheckpointStore(link);
        var cursor = new CandidateFeedReplayCursor(ManuLivCandidateFeedReader.ContractVersion,
            new string('a', 64), 0, null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.SaveAsync(cursor));
    }

    private static async Task<CandidateFeedDocument> ReadFixtureAsync()
    {
        const string json = "[{\"eventId\":\"raw-a\",\"matchId\":\"candidate-match\",\"sequence\":0,\"clockSeconds\":0,\"period\":1,\"eventType\":\"ProviderRestart\"},{\"eventId\":\"raw-b\",\"matchId\":\"candidate-match\",\"sequence\":8,\"clockSeconds\":2700.5,\"period\":1,\"eventType\":\"ProviderPass\"},{\"eventId\":\"raw-c\",\"matchId\":\"candidate-match\",\"sequence\":41,\"clockSeconds\":5761,\"period\":2,\"eventType\":\"ProviderShot\"}]";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await ManuLivCandidateFeedReader.ReadAsync(input);
    }

    private static string CreateTemporaryDirectory() => Directory.CreateTempSubdirectory("pitchmind-replay-").FullName;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
