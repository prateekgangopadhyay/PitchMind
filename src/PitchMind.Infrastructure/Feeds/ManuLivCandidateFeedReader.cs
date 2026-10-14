using System.Security.Cryptography;
using System.Text.Json;

namespace PitchMind.Infrastructure.Feeds;

/// <summary>
/// Reads the owner-approved candidate JSON as raw source evidence. This reader deliberately
/// does not map event types, clocks, coordinates, identities, or player metrics into domain facts.
/// </summary>
public static class ManuLivCandidateFeedReader
{
    public const string ContractVersion = "manu-liv-raw-v1";
    public const long MaximumSourceBytes = 64 * 1024 * 1024;

    public static async Task<CandidateFeedDocument> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("Candidate source must be readable.", nameof(source));

        var events = new List<CandidateRawEvent>();
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        long? previousSequence = null;
        using var boundedSource = new BoundedHashingReadStream(source, MaximumSourceBytes);
        try
        {
            await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(
                               boundedSource, cancellationToken: cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var recordIndex = events.Count;
                if (item.ValueKind != JsonValueKind.Object)
                    throw new CandidateFeedParseException("record_not_object", recordIndex);

                var raw = item.Clone();
                RejectDuplicateProperties(raw, recordIndex);
                var eventId = RequiredString(raw, "eventId", recordIndex);
                var matchId = RequiredString(raw, "matchId", recordIndex);
                var eventType = RequiredString(raw, "eventType", recordIndex);
                var sequence = RequiredInt64(raw, "sequence", recordIndex);
                var rawClockValue = RequiredDecimal(raw, "clockSeconds", recordIndex);
                var period = RequiredInt32(raw, "period", recordIndex);

                if (sequence < 0)
                    throw new CandidateFeedParseException("invalid_source_sequence", recordIndex);
                if (previousSequence is not null && sequence <= previousSequence.Value)
                    throw new CandidateFeedParseException("source_sequence_not_increasing", recordIndex);
                if (!eventIds.Add(eventId))
                    throw new CandidateFeedParseException("duplicate_source_event_id", recordIndex);

                events.Add(new CandidateRawEvent(
                    recordIndex,
                    eventId,
                    matchId,
                    sequence,
                    rawClockValue,
                    period,
                    eventType,
                    raw));
                previousSequence = sequence;
            }
        }
        catch (JsonException)
        {
            throw new CandidateFeedParseException("invalid_json", events.Count);
        }

        if (events.Count == 0)
            throw new CandidateFeedParseException("empty_candidate_feed", 0);

        var sourceHash = await boundedSource.CompleteAndGetSha256Async(cancellationToken);
        return new CandidateFeedDocument(ContractVersion, sourceHash, events.AsReadOnly());
    }

    private static string RequiredString(JsonElement record, string property, int index)
    {
        if (!record.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new CandidateFeedParseException($"invalid_{property}", index);

        return value.GetString()!;
    }

    private static long RequiredInt64(JsonElement record, string property, int index)
    {
        if (!record.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var result))
            throw new CandidateFeedParseException($"invalid_{property}", index);

        return result;
    }

    private static int RequiredInt32(JsonElement record, string property, int index)
    {
        if (!record.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result))
            throw new CandidateFeedParseException($"invalid_{property}", index);

        return result;
    }

    private static decimal RequiredDecimal(JsonElement record, string property, int index)
    {
        if (!record.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDecimal(out var result))
            throw new CandidateFeedParseException($"invalid_{property}", index);

        return result;
    }

    private static void RejectDuplicateProperties(JsonElement value, int recordIndex)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw new CandidateFeedParseException("duplicate_json_property", recordIndex);
                    RejectDuplicateProperties(property.Value, recordIndex);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    RejectDuplicateProperties(item, recordIndex);
                break;
        }
    }
}

/// <summary>Hashes exactly the bytes consumed by the parser and enforces a cap on actual reads.</summary>
internal sealed class BoundedHashingReadStream(Stream inner, long maximumBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private long _bytesRead;
    private bool _reachedEnd;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_reachedEnd || buffer.IsEmpty) return 0;
        var read = inner.Read(LimitBuffer(buffer));
        ProcessRead(buffer[..read], read);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_reachedEnd || buffer.IsEmpty) return 0;
        var limited = LimitBuffer(buffer);
        var read = await inner.ReadAsync(limited, cancellationToken);
        ProcessRead(buffer.Span[..read], read);
        return read;
    }

    public async Task<string> CompleteAndGetSha256Async(CancellationToken cancellationToken)
    {
        var discard = new byte[8192];
        while (!_reachedEnd)
            await ReadAtLeastAsync(discard.AsMemory(), 1, throwOnEndOfStream: false, cancellationToken);

        return Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hash.Dispose();
        // The caller owns the source stream.
        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync()
    {
        _hash.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();

    private Memory<byte> LimitBuffer(Memory<byte> buffer)
    {
        var allowed = Math.Min(buffer.Length, maximumBytes - _bytesRead + 1);
        return buffer[..checked((int)allowed)];
    }

    private Span<byte> LimitBuffer(Span<byte> buffer)
    {
        var allowed = Math.Min(buffer.Length, maximumBytes - _bytesRead + 1);
        return buffer[..checked((int)allowed)];
    }

    private void ProcessRead(ReadOnlySpan<byte> bytes, int read)
    {
        if (read == 0)
        {
            _reachedEnd = true;
            return;
        }
        if (_bytesRead + read > maximumBytes)
            throw new CandidateFeedParseException("source_too_large", 0);

        _hash.AppendData(bytes);
        _bytesRead += read;
    }
}

public sealed record CandidateFeedDocument(
    string ContractVersion,
    string SourceSha256,
    IReadOnlyList<CandidateRawEvent> Events);

/// <summary>Raw source identifiers and values; never a normalized canonical football event.</summary>
public sealed record CandidateRawEvent(
    int SourceRecordIndex,
    string SourceEventId,
    string SourceMatchId,
    long SourceSequence,
    decimal RawClockValue,
    int RawPeriod,
    string RawEventType,
    JsonElement RawPayload);

public sealed class CandidateFeedParseException(string code, int sourceRecordIndex)
    : Exception($"Candidate feed rejected ({code}) at record {sourceRecordIndex}.")
{
    public string Code { get; } = code;
    public int SourceRecordIndex { get; } = sourceRecordIndex;
}
