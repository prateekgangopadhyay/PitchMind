using System.Runtime.CompilerServices;

namespace PitchMind.Infrastructure.Feeds;

/// <summary>
/// Replays validated candidate records locally without mapping them to canonical football facts.
/// The consumer controls backpressure by requesting the next item and persists ResumeCursor only
/// after it has safely handled the corresponding raw envelope.
/// The configured rate paces records within this enumeration; a resumed enumeration emits its first
/// record immediately, so callers needing an aggregate session-wide cap must preserve pacing state.
/// </summary>
public static class LocalCandidateFeedReplayProducer
{
    public const int MaximumRecordsPerSecond = 500;

    public static async IAsyncEnumerable<CandidateReplayEnvelope> ReplayAsync(
        CandidateFeedDocument document,
        CandidateFeedReplayCursor? resumeFrom = null,
        int recordsPerSecond = 10,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.ContractVersion != ManuLivCandidateFeedReader.ContractVersion)
            throw new ArgumentException("Unsupported candidate feed contract version.", nameof(document));
        if (string.IsNullOrWhiteSpace(document.SourceSha256) || document.SourceSha256.Length != 64 ||
            document.SourceSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Candidate source hash is invalid.", nameof(document));
        if (document.Events is null || document.Events.Count == 0)
            throw new ArgumentException("Candidate feed has no records.", nameof(document));
        if (recordsPerSecond is < 1 or > MaximumRecordsPerSecond)
            throw new ArgumentOutOfRangeException(nameof(recordsPerSecond),
                $"Replay rate must be between 1 and {MaximumRecordsPerSecond} records per second.");

        ValidateDocumentRecords(document);
        var startIndex = ValidateResumeCursor(document, resumeFrom);
        var interval = TimeSpan.FromSeconds(1d / recordsPerSecond);
        for (var index = startIndex; index < document.Events.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index > startIndex)
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

            var sourceRecord = document.Events[index];
            var nextIndex = index + 1;
            var nextSequence = sourceRecord.SourceSequence;
            yield return new CandidateReplayEnvelope(
                document.ContractVersion,
                document.SourceSha256,
                sourceRecord,
                new CandidateFeedReplayCursor(document.ContractVersion, document.SourceSha256, nextIndex, nextSequence));
        }
    }

    private static int ValidateResumeCursor(CandidateFeedDocument document, CandidateFeedReplayCursor? cursor)
    {
        if (cursor is null) return 0;
        if (cursor.ContractVersion != document.ContractVersion ||
            !string.Equals(cursor.SourceSha256, document.SourceSha256, StringComparison.Ordinal))
            throw new ArgumentException("Replay cursor belongs to a different candidate source revision.", nameof(cursor));
        if (cursor.NextSourceRecordIndex < 0 || cursor.NextSourceRecordIndex > document.Events.Count)
            throw new ArgumentException("Replay cursor record index is outside this candidate source.", nameof(cursor));

        var expectedPreviousSequence = cursor.NextSourceRecordIndex == 0
            ? (long?)null
            : document.Events[cursor.NextSourceRecordIndex - 1].SourceSequence;
        if (cursor.LastSourceSequence != expectedPreviousSequence)
            throw new ArgumentException("Replay cursor does not match the last acknowledged source record.", nameof(cursor));

        return cursor.NextSourceRecordIndex;
    }

    private static void ValidateDocumentRecords(CandidateFeedDocument document)
    {
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        long? previousSequence = null;
        for (var index = 0; index < document.Events.Count; index++)
        {
            var record = document.Events[index];
            if (record is null || record.SourceRecordIndex != index ||
                string.IsNullOrWhiteSpace(record.SourceEventId) ||
                string.IsNullOrWhiteSpace(record.SourceMatchId) ||
                string.IsNullOrWhiteSpace(record.RawEventType) ||
                record.SourceSequence < 0 ||
                (previousSequence is not null && record.SourceSequence <= previousSequence.Value) ||
                !eventIds.Add(record.SourceEventId) ||
                record.RawPayload.ValueKind != System.Text.Json.JsonValueKind.Object ||
                !PayloadMatchesSourceFields(record))
                throw new ArgumentException("Candidate feed records are inconsistent with the raw source contract.", nameof(document));

            previousSequence = record.SourceSequence;
        }
    }

    private static bool PayloadMatchesSourceFields(CandidateRawEvent record)
    {
        var payload = record.RawPayload;
        return payload.TryGetProperty("eventId", out var eventId) && eventId.ValueKind == System.Text.Json.JsonValueKind.String &&
               string.Equals(eventId.GetString(), record.SourceEventId, StringComparison.Ordinal) &&
               payload.TryGetProperty("matchId", out var matchId) && matchId.ValueKind == System.Text.Json.JsonValueKind.String &&
               string.Equals(matchId.GetString(), record.SourceMatchId, StringComparison.Ordinal) &&
               payload.TryGetProperty("sequence", out var sequence) && sequence.TryGetInt64(out var rawSequence) &&
               rawSequence == record.SourceSequence &&
               payload.TryGetProperty("clockSeconds", out var clock) && clock.TryGetDecimal(out var rawClock) &&
               rawClock == record.RawClockValue &&
               payload.TryGetProperty("period", out var period) && period.TryGetInt32(out var rawPeriod) &&
               rawPeriod == record.RawPeriod &&
               payload.TryGetProperty("eventType", out var eventType) && eventType.ValueKind == System.Text.Json.JsonValueKind.String &&
               string.Equals(eventType.GetString(), record.RawEventType, StringComparison.Ordinal);
    }
}

/// <summary>Resume position tied to the exact raw candidate bytes and parser contract.</summary>
public sealed record CandidateFeedReplayCursor(
    string ContractVersion,
    string SourceSha256,
    int NextSourceRecordIndex,
    long? LastSourceSequence);

/// <summary>One immutable raw candidate record plus the cursor to persist after successful handling.</summary>
public sealed record CandidateReplayEnvelope(
    string ContractVersion,
    string SourceSha256,
    CandidateRawEvent SourceRecord,
    CandidateFeedReplayCursor ResumeCursor);
