using System.Text;
using PitchMind.Infrastructure.Feeds;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class LocalCandidateFeedReplayProducerTests
{
    [Fact]
    public async Task ReplayAsync_preserves_raw_identity_order_payload_and_provenance()
    {
        var document = await ReadFixtureAsync();

        var replayed = await CollectAsync(document);

        Assert.Equal(document.Events.Count, replayed.Count);
        for (var index = 0; index < replayed.Count; index++)
        {
            var item = replayed[index];
            var original = document.Events[index];
            Assert.Equal(document.ContractVersion, item.ContractVersion);
            Assert.Equal(document.SourceSha256, item.SourceSha256);
            Assert.Equal(original.SourceEventId, item.SourceRecord.SourceEventId);
            Assert.Equal(original.SourceSequence, item.SourceRecord.SourceSequence);
            Assert.Equal(original.RawPayload.GetRawText(), item.SourceRecord.RawPayload.GetRawText());
            Assert.Equal(index + 1, item.ResumeCursor.NextSourceRecordIndex);
            Assert.Equal(original.SourceSequence, item.ResumeCursor.LastSourceSequence);
        }
    }

    [Fact]
    public async Task ReplayAsync_resumes_after_acknowledged_record_without_duplicate_or_gap()
    {
        var document = await ReadFixtureAsync();
        var first = Assert.Single(await CollectAsync(document, take: 1));

        var resumed = await CollectAsync(document, first.ResumeCursor);

        Assert.Equal(document.Events.Skip(1).Select(item => item.SourceEventId),
            resumed.Select(item => item.SourceRecord.SourceEventId));
    }

    [Fact]
    public async Task ReplayAsync_rejects_cursor_from_another_source_revision()
    {
        var document = await ReadFixtureAsync();
        var cursor = new CandidateFeedReplayCursor(document.ContractVersion, new string('0', 64), 0, null);

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CollectAsync(document, cursor));

        Assert.Contains("different candidate source revision", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReplayAsync_rejects_cursor_that_does_not_match_the_acknowledged_sequence()
    {
        var document = await ReadFixtureAsync();
        var cursor = new CandidateFeedReplayCursor(document.ContractVersion, document.SourceSha256, 1, 999);

        await Assert.ThrowsAsync<ArgumentException>(async () => await CollectAsync(document, cursor));
    }

    [Fact]
    public async Task ReplayAsync_rejects_malformed_document_records_before_emitting()
    {
        var document = await ReadFixtureAsync();
        var altered = document.Events.ToArray();
        altered[1] = altered[1] with { SourceSequence = 99 };
        var malformed = document with { Events = altered };

        await Assert.ThrowsAsync<ArgumentException>(async () => await CollectAsync(malformed));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public async Task ReplayAsync_rejects_unbounded_or_zero_rate(int recordsPerSecond)
    {
        var document = await ReadFixtureAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await CollectAsync(document, recordsPerSecond: recordsPerSecond));
    }

    [Fact]
    public async Task ReplayAsync_honors_cancellation_before_emitting()
    {
        var document = await ReadFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CollectAsync(document, cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task ReplayAsync_honors_cancellation_between_records()
    {
        var document = await ReadFixtureAsync();
        using var cancellation = new CancellationTokenSource();
        await using var replay = LocalCandidateFeedReplayProducer
            .ReplayAsync(document, recordsPerSecond: 1, cancellationToken: cancellation.Token)
            .GetAsyncEnumerator();

        Assert.True(await replay.MoveNextAsync());
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await replay.MoveNextAsync().AsTask());
    }

    private static async Task<CandidateFeedDocument> ReadFixtureAsync()
    {
        const string json = "[{\"eventId\":\"raw-a\",\"matchId\":\"candidate-match\",\"sequence\":0,\"clockSeconds\":0,\"period\":1,\"eventType\":\"ProviderRestart\",\"pitchX\":73.25},{\"eventId\":\"raw-b\",\"matchId\":\"candidate-match\",\"sequence\":8,\"clockSeconds\":2700.5,\"period\":1,\"eventType\":\"ProviderPass\",\"speed\":33},{\"eventId\":\"raw-c\",\"matchId\":\"candidate-match\",\"sequence\":41,\"clockSeconds\":5761,\"period\":2,\"eventType\":\"ProviderShot\",\"heartRate\":180}]";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await ManuLivCandidateFeedReader.ReadAsync(input);
    }

    private static async Task<List<CandidateReplayEnvelope>> CollectAsync(
        CandidateFeedDocument document,
        CandidateFeedReplayCursor? resumeFrom = null,
        int recordsPerSecond = 500,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        var result = new List<CandidateReplayEnvelope>();
        await foreach (var envelope in LocalCandidateFeedReplayProducer.ReplayAsync(
                           document, resumeFrom, recordsPerSecond, cancellationToken))
        {
            result.Add(envelope);
            if (take is not null && result.Count >= take.Value) break;
        }
        return result;
    }
}
