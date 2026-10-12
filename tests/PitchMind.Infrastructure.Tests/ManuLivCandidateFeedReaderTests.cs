using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitchMind.Infrastructure.Feeds;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class ManuLivCandidateFeedReaderTests
{
    [Fact]
    public async Task ReadAsync_parses_pinned_candidate_without_promoting_it_to_canonical_data()
    {
        var repositoryRoot = new DirectoryInfo(AppContext.BaseDirectory);
        for (var parent = 0; parent < 5; parent++)
            repositoryRoot = repositoryRoot.Parent ?? throw new DirectoryNotFoundException("Repository root was not found from test output.");

        var path = Path.Combine(repositoryRoot.FullName, "data", "simulations", "manu-liv-match-dataset.json");
        await using var input = File.OpenRead(path);

        var result = await ManuLivCandidateFeedReader.ReadAsync(input);

        Assert.Equal("e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c", result.SourceSha256);
        Assert.Equal(2199, result.Events.Count);
        Assert.Equal(Enumerable.Range(1, 2199).Select(value => (long)value), result.Events.Select(item => item.SourceSequence));
        Assert.Equal(48_048, result.Events.Sum(item => item.RawPayload.GetProperty("playerPositions").GetArrayLength()));
        Assert.Equal("manu-liv-raw-v1", result.ContractVersion);
        Assert.Contains(result.Events, item => item.RawPayload.TryGetProperty("pitchX", out _));
        Assert.DoesNotContain(result.Events, item => item.RawPayload.TryGetProperty("pitchXMetres", out _));
    }

    [Fact]
    public async Task ReadAsync_preserves_raw_source_values_and_hash_without_canonicalizing()
    {
        const string json = "[{\"eventId\":\"raw-event-01\",\"matchId\":\"raw-match\",\"sequence\":41,\"clockSeconds\":2700,\"period\":2,\"eventType\":\"ProviderSpecificRestart\",\"pitchX\":73.25,\"playerPositions\":[{\"x\":50,\"speed\":33,\"heartRate\":180}],\"metadata\":{\"period\":1}}]";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await ManuLivCandidateFeedReader.ReadAsync(input);

        Assert.Equal("manu-liv-raw-v1", result.ContractVersion);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant(), result.SourceSha256);
        var item = Assert.Single(result.Events);
        Assert.Equal(0, item.SourceRecordIndex);
        Assert.Equal("raw-event-01", item.SourceEventId);
        Assert.Equal("raw-match", item.SourceMatchId);
        Assert.Equal(41, item.SourceSequence);
        Assert.Equal(2700m, item.RawClockValue);
        Assert.Equal(2, item.RawPeriod);
        Assert.Equal("ProviderSpecificRestart", item.RawEventType);

        // Unitless coordinates, unknown event labels and metric values stay raw JSON.
        Assert.True(item.RawPayload.TryGetProperty("pitchX", out var rawPitchX));
        Assert.Equal(73.25m, rawPitchX.GetDecimal());
        Assert.Equal(33, item.RawPayload.GetProperty("playerPositions")[0].GetProperty("speed").GetInt32());
        Assert.Equal(180, item.RawPayload.GetProperty("playerPositions")[0].GetProperty("heartRate").GetInt32());
        Assert.Equal(1, item.RawPayload.GetProperty("metadata").GetProperty("period").GetInt32());
    }

    [Fact]
    public async Task ReadAsync_preserves_source_order_and_does_not_assume_a_one_based_sequence()
    {
        const string json = "[{\"eventId\":\"a\",\"matchId\":\"m\",\"sequence\":0,\"clockSeconds\":2,\"period\":1,\"eventType\":\"A\"},{\"eventId\":\"b\",\"matchId\":\"m\",\"sequence\":8,\"clockSeconds\":2,\"period\":1,\"eventType\":\"B\"}]";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var result = await ManuLivCandidateFeedReader.ReadAsync(input);

        Assert.Collection(result.Events,
            first => { Assert.Equal(0, first.SourceSequence); Assert.Equal("a", first.SourceEventId); },
            second => { Assert.Equal(8, second.SourceSequence); Assert.Equal("b", second.SourceEventId); });
    }

    [Fact]
    public async Task ReadAsync_rejects_nonincreasing_sequence_with_safe_diagnostic()
    {
        const string json = "[{\"eventId\":\"a\",\"matchId\":\"m\",\"sequence\":5,\"clockSeconds\":1,\"period\":1,\"eventType\":\"A\"},{\"eventId\":\"b\",\"matchId\":\"m\",\"sequence\":5,\"clockSeconds\":2,\"period\":1,\"eventType\":\"B\"}]";
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var error = await Assert.ThrowsAsync<CandidateFeedParseException>(
            () => ManuLivCandidateFeedReader.ReadAsync(input));

        Assert.Equal("source_sequence_not_increasing", error.Code);
        Assert.Equal(1, error.SourceRecordIndex);
        Assert.DoesNotContain("raw-event", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_rejects_duplicate_ids_missing_fields_and_non_array_roots()
    {
        const string duplicateIds = "[{\"eventId\":\"a\",\"matchId\":\"m\",\"sequence\":1,\"clockSeconds\":1,\"period\":1,\"eventType\":\"A\"},{\"eventId\":\"a\",\"matchId\":\"m\",\"sequence\":2,\"clockSeconds\":2,\"period\":1,\"eventType\":\"A\"}]";
        await using var duplicateInput = new MemoryStream(Encoding.UTF8.GetBytes(duplicateIds));
        var duplicateError = await Assert.ThrowsAsync<CandidateFeedParseException>(
            () => ManuLivCandidateFeedReader.ReadAsync(duplicateInput));
        Assert.Equal("duplicate_source_event_id", duplicateError.Code);

        const string missingSequence = "[{\"eventId\":\"a\",\"matchId\":\"m\",\"clockSeconds\":1,\"period\":1,\"eventType\":\"A\"}]";
        await using var missingInput = new MemoryStream(Encoding.UTF8.GetBytes(missingSequence));
        var missingError = await Assert.ThrowsAsync<CandidateFeedParseException>(
            () => ManuLivCandidateFeedReader.ReadAsync(missingInput));
        Assert.Equal("invalid_sequence", missingError.Code);

        await using var rootInput = new MemoryStream(Encoding.UTF8.GetBytes("{}"));
        var rootError = await Assert.ThrowsAsync<CandidateFeedParseException>(
            () => ManuLivCandidateFeedReader.ReadAsync(rootInput));
        Assert.Equal("invalid_json", rootError.Code);
    }

    [Fact]
    public async Task ReadAsync_rejects_duplicate_json_keys_and_oversized_sources()
    {
        const string duplicateKey = "[{\"eventId\":\"a\",\"matchId\":\"m\",\"sequence\":1,\"sequence\":2,\"clockSeconds\":1,\"period\":1,\"eventType\":\"A\"}]";
        await using var duplicateInput = new MemoryStream(Encoding.UTF8.GetBytes(duplicateKey));
        var duplicateError = await Assert.ThrowsAsync<CandidateFeedParseException>(
            () => ManuLivCandidateFeedReader.ReadAsync(duplicateInput));
        Assert.Equal("duplicate_json_property", duplicateError.Code);

        await using var oversizedInput = new OversizedGeneratedStream(ManuLivCandidateFeedReader.MaximumSourceBytes + 1);
        var sizeError = await Assert.ThrowsAsync<CandidateFeedParseException>(
            () => ManuLivCandidateFeedReader.ReadAsync(oversizedInput));
        Assert.Equal("source_too_large", sizeError.Code);
    }

    [Fact]
    public async Task ReadAsync_accepts_nonseekable_sources_and_rejects_empty_input()
    {
        await using var nonSeekableInput = new NonSeekableMemoryStream(Encoding.UTF8.GetBytes("[{\"eventId\":\"a\",\"matchId\":\"m\",\"sequence\":0,\"clockSeconds\":0,\"period\":0,\"eventType\":\"A\"}]"));
        var parsed = await ManuLivCandidateFeedReader.ReadAsync(nonSeekableInput);
        Assert.Single(parsed.Events);

        await using var emptyInput = new MemoryStream(Encoding.UTF8.GetBytes("[]"));
        var error = await Assert.ThrowsAsync<CandidateFeedParseException>(
            () => ManuLivCandidateFeedReader.ReadAsync(emptyInput));
        Assert.Equal("empty_candidate_feed", error.Code);
    }

    private sealed class NonSeekableMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    }

    private sealed class OversizedGeneratedStream(long bytesRemaining) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 1; // Deliberately lies: the reader must enforce actual bytes read.
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(Span<byte> buffer)
        {
            var count = (int)Math.Min(buffer.Length, bytesRemaining);
            buffer[..count].Fill((byte)' ');
            bytesRemaining -= count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
    }
}
