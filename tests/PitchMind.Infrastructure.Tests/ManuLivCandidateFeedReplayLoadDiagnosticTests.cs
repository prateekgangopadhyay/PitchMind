using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using PitchMind.Infrastructure.Feeds;
using Xunit;
using Xunit.Abstractions;

namespace PitchMind.Infrastructure.Tests;

/// <summary>
/// Opt-in diagnostic for the local raw replay and file-checkpoint path. This is not an
/// ingestion acceptance test: it does not exercise Event Hubs, EventWorker, PostgreSQL,
/// canonical admission, or an approved burst duration/payload budget.
/// </summary>
public sealed class ManuLivCandidateFeedReplayLoadDiagnosticTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "LoadDiagnostic")]
    public async Task Replays_the_single_match_candidate_with_local_durable_checkpoints()
    {
        var sourcePath = FindRepositoryFile("data/simulations/manu-liv-match-dataset.json");
        await using var source = File.OpenRead(sourcePath);
        var document = await ManuLivCandidateFeedReader.ReadAsync(source);
        var matchIds = document.Events.Select(record => record.SourceMatchId)
            .Distinct(StringComparer.Ordinal).ToArray();
        Assert.Single(matchIds);

        var observedIds = new List<string>(document.Events.Count);
        var observedSequences = new List<long>(document.Events.Count);
        var replayedPayload = new StringBuilder();
        var checkpointDirectory = Path.Combine(FindRepositoryRoot(sourcePath), $".ep03-s05-diagnostic-{Guid.NewGuid():N}");
        var checkpointStore = new FileCandidateFeedReplayCheckpointStore(checkpointDirectory);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await using (var session = await LocalCandidateFeedReplaySession.CreateAsync(
                             document,
                             checkpointStore,
                             (envelope, _) =>
                             {
                                 observedIds.Add(envelope.SourceRecord.SourceEventId);
                                 observedSequences.Add(envelope.SourceRecord.SourceSequence);
                                 replayedPayload.Append(envelope.SourceRecord.RawPayload.GetRawText()).Append('\n');
                                 return ValueTask.CompletedTask;
                             },
                             recordsPerSecond: LocalCandidateFeedReplayProducer.MaximumRecordsPerSecond))
            {
                await session.Start();
                stopwatch.Stop();
                Assert.Equal(CandidateFeedReplaySessionState.Completed, session.State);
                Assert.Equal(document.Events.Count, session.LastAcknowledgedCursor?.NextSourceRecordIndex);
            }
        }
        finally
        {
            if (Directory.Exists(checkpointDirectory)) Directory.Delete(checkpointDirectory, recursive: true);
        }

        Assert.Equal(document.Events.Count, observedIds.Count);
        Assert.Equal(document.Events.Select(record => record.SourceEventId), observedIds);
        Assert.Equal(document.Events.Select(record => record.SourceSequence), observedSequences);
        var replayedPayloadSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(replayedPayload.ToString())))
            .ToLowerInvariant();
        var observedRecordsPerSecond = observedIds.Count / stopwatch.Elapsed.TotalSeconds;

        output.WriteLine($"Story: EP03-S05 local replay diagnostic (not acceptance)");
        output.WriteLine($"Source path: {Path.GetRelativePath(FindRepositoryRoot(sourcePath), sourcePath)}");
        output.WriteLine($"Source SHA-256: {document.SourceSha256}");
        output.WriteLine($"Match partitions: {matchIds.Length} ({matchIds[0]})");
        output.WriteLine($"Records delivered and checkpointed: {observedIds.Count}");
        output.WriteLine($"Configured producer rate: {LocalCandidateFeedReplayProducer.MaximumRecordsPerSecond} records/second");
        output.WriteLine($"Observed duration including local file checkpoint writes: {stopwatch.Elapsed.TotalSeconds:F3} seconds");
        output.WriteLine($"Observed delivered rate: {observedRecordsPerSecond:F2} records/second");
        output.WriteLine($"Ordered event ID / sequence SHA-256: {HashLines(observedIds.Zip(observedSequences, (id, sequence) => $"{id}\t{sequence}"))}");
        output.WriteLine($"Replayed raw payload SHA-256 (one JSON object per line): {replayedPayloadSha256}");
        output.WriteLine("Sink: test callback; checkpoint: local temporary file; Event Hubs/PostgreSQL/canonical admission: not exercised.");
    }

    private static string FindRepositoryFile(string relativePath)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                directory = directory.Parent;
            }
        }

        throw new FileNotFoundException($"Could not locate repository input '{relativePath}'.");
    }

    private static string FindRepositoryRoot(string sourcePath)
    {
        var directory = new FileInfo(sourcePath).Directory;
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
               !File.Exists(Path.Combine(directory.FullName, ".git")))
            directory = directory.Parent;
        return directory?.FullName ?? Path.GetFullPath(Path.Combine(sourcePath, "..", "..", ".."));
    }

    private static string HashLines(IEnumerable<string> lines) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))))
            .ToLowerInvariant();
}
