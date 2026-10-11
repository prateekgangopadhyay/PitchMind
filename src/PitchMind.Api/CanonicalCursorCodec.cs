using System.Text;
using System.Text.Json;
using PitchMind.Application.Abstractions;

namespace PitchMind.Api;

internal static class CanonicalCursorCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const int MaximumCursorLength = 2048;
    private const int MaximumFrameIdCodePoints = 128;

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
        long afterSequence, string afterFrameId)
    {
        ValidateFrameKey(afterSequence, afterFrameId);
        var encodedFrameId = ToBase64Url(Encoding.UTF8.GetBytes(afterFrameId));
        var cursor = Encode(new FrameCursorPayload(matchId, selectedTimeSeconds, revision, sourceSha256,
            afterSequence, encodedFrameId));
        if (cursor.Length > MaximumCursorLength)
            throw new ArgumentException("Frame cursor exceeds the maximum encoded length.", nameof(afterFrameId));
        return cursor;
    }

    public static FrameCursor DecodeFrame(string cursor, Guid matchId, int selectedTimeSeconds, int? requestedRevision)
    {
        var payload = Decode<FrameCursorPayload>(cursor);
        if (payload.MatchId != matchId || payload.SelectedTimeSeconds != selectedTimeSeconds ||
            requestedRevision is not null && requestedRevision != payload.CanonicalRevision || payload.CanonicalRevision <= 0 ||
            payload.AfterSequence < 0 || string.IsNullOrWhiteSpace(payload.AfterFrameId) ||
            string.IsNullOrWhiteSpace(payload.SourceSha256) || payload.SourceSha256.Length != 64 ||
            payload.SourceSha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("Cursor does not match the requested match, time, or revision.", nameof(cursor));
        var afterFrameId = FromBase64Url(payload.AfterFrameId);
        ValidateFrameKey(payload.AfterSequence, afterFrameId);
        return new FrameCursor(payload.MatchId, payload.SelectedTimeSeconds, payload.CanonicalRevision,
            payload.SourceSha256, payload.AfterSequence, afterFrameId);
    }

    public static void ValidateFrameProjection(FrameCursor cursor, SelectedMatchProjection projection)
    {
        if (cursor.CanonicalRevision != projection.Revision || cursor.SourceSha256 != projection.SourceSha256)
            throw new ArgumentException("Cursor does not match the selected projection source.", nameof(cursor));
    }

    private static string Encode<T>(T value, JsonSerializerOptions? options = null) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, options ?? Json))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static T Decode<T>(string cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor) || cursor.Length > MaximumCursorLength)
            throw new ArgumentException($"Cursor must contain 1 to {MaximumCursorLength} characters.", nameof(cursor));
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

    private static void ValidateFrameKey(long afterSequence, string afterFrameId)
    {
        if (afterSequence < 0 || IsInvalidFrameId(afterFrameId))
            throw new ArgumentException("Frame cursor key must contain a valid sequence and frame ID.", nameof(afterFrameId));
    }

    private static bool IsInvalidFrameId(string? frameId) => string.IsNullOrWhiteSpace(frameId) ||
        frameId.EnumerateRunes().Count() > MaximumFrameIdCodePoints;

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string FromBase64Url(string encoded)
    {
        try
        {
            var normalized = encoded.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
            return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(normalized));
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Frame cursor key is not valid base64url UTF-8.", nameof(encoded), exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("Frame cursor key is not valid base64url UTF-8.", nameof(encoded), exception);
        }
    }

    internal sealed record EventCursor(Guid MatchId, int SelectedTimeSeconds, int CanonicalRevision, long AfterSequence);
    internal sealed record InsightCursor(Guid MatchId, int SelectedTimeSeconds, int CanonicalRevision,
        long AfterSequence, string AfterInsightId);
    internal sealed record FrameCursor(Guid MatchId, int SelectedTimeSeconds, int CanonicalRevision,
        string SourceSha256, long AfterSequence, string AfterFrameId);
    private sealed record FrameCursorPayload(Guid MatchId, int SelectedTimeSeconds, int CanonicalRevision,
        string SourceSha256, long AfterSequence, string AfterFrameId);
}
