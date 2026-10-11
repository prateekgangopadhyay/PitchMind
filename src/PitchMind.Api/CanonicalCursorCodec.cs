using System.Text.Json;
using PitchMind.Application.Abstractions;

namespace PitchMind.Api;

internal static class CanonicalCursorCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string EncodeEvent(Guid matchId, int selectedTimeSeconds, int revision, long afterSequence) =>
        Encode(new EventCursor(matchId, selectedTimeSeconds, revision, afterSequence));

    public static EventCursor DecodeEvent(string cursor, Guid matchId, int selectedTimeSeconds, int? requestedRevision)
    {
        var value = Decode<EventCursor>(cursor);
        if (value.MatchId != matchId || value.SelectedTimeSeconds != selectedTimeSeconds ||
            requestedRevision is not null && requestedRevision != value.CanonicalRevision || value.AfterSequence < -1)
            throw new ArgumentException("Cursor does not match the requested match, time, or revision.", nameof(cursor));
        return value;
    }

    public static string EncodeInsight(Guid matchId, int selectedTimeSeconds, int revision,
        long afterSequence, string afterInsightId) =>
        Encode(new InsightCursor(matchId, selectedTimeSeconds, revision, afterSequence, afterInsightId));

    public static InsightCursor DecodeInsight(string cursor, Guid matchId, int selectedTimeSeconds, int? requestedRevision)
    {
        var value = Decode<InsightCursor>(cursor);
        if (value.MatchId != matchId || value.SelectedTimeSeconds != selectedTimeSeconds ||
            requestedRevision is not null && requestedRevision != value.CanonicalRevision || value.AfterSequence < 0 ||
            string.IsNullOrWhiteSpace(value.AfterInsightId) || value.AfterInsightId.Length > 256)
            throw new ArgumentException("Cursor does not match the requested match, time, or revision.", nameof(cursor));
        return value;
    }

    public static string EncodeFrame(Guid matchId, int selectedTimeSeconds, int revision, string sourceSha256,
        long afterSequence, string afterFrameId) =>
        Encode(new FrameCursor(matchId, selectedTimeSeconds, revision, sourceSha256, afterSequence, afterFrameId));

    public static FrameCursor DecodeFrame(string cursor, Guid matchId, int selectedTimeSeconds, int? requestedRevision)
    {
        var value = Decode<FrameCursor>(cursor);
        if (value.MatchId != matchId || value.SelectedTimeSeconds != selectedTimeSeconds ||
            requestedRevision is not null && requestedRevision != value.CanonicalRevision || value.CanonicalRevision <= 0 ||
            value.AfterSequence < 0 || string.IsNullOrWhiteSpace(value.AfterFrameId) || value.AfterFrameId.Length > 128 ||
            string.IsNullOrWhiteSpace(value.SourceSha256) || value.SourceSha256.Length != 64 ||
            value.SourceSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Cursor does not match the requested match, time, or revision.", nameof(cursor));
        return value;
    }

    public static void ValidateFrameProjection(FrameCursor cursor, SelectedMatchProjection projection)
    {
        if (cursor.CanonicalRevision != projection.Revision || cursor.SourceSha256 != projection.SourceSha256)
            throw new ArgumentException("Cursor does not match the selected projection source.", nameof(cursor));
    }

    private static string Encode<T>(T value) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, Json))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static T Decode<T>(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > 2048)
            throw new ArgumentException("Cursor must contain 1 to 2048 characters.", nameof(cursor));
        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            return JsonSerializer.Deserialize<T>(Convert.FromBase64String(normalized), Json)
                ?? throw new ArgumentException("Cursor payload is empty.", nameof(cursor));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Cursor is not valid base64url.", nameof(cursor), exception);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Cursor payload is invalid.", nameof(cursor), exception);
        }
    }

    internal sealed record EventCursor(Guid MatchId, int SelectedTimeSeconds, int CanonicalRevision, long AfterSequence);
    internal sealed record InsightCursor(Guid MatchId, int SelectedTimeSeconds, int CanonicalRevision,
        long AfterSequence, string AfterInsightId);
    internal sealed record FrameCursor(Guid MatchId, int SelectedTimeSeconds, int CanonicalRevision,
        string SourceSha256, long AfterSequence, string AfterFrameId);
}
