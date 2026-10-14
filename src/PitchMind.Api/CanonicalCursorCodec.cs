using System.Text.Json;

namespace PitchMind.Api;

internal static class CanonicalCursorCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string EncodeEvent(Guid matchId, int selectedTimeSeconds, long canonicalRevision,
        int projectionRevision, long afterSequence) =>
        Encode(new EventCursor(matchId, selectedTimeSeconds, canonicalRevision, projectionRevision, afterSequence));

    public static EventCursor DecodeEvent(string cursor, Guid matchId, int selectedTimeSeconds, int? requestedRevision)
    {
        var value = Decode<EventCursor>(cursor);
        if (value.MatchId != matchId || value.SelectedTimeSeconds != selectedTimeSeconds ||
            requestedRevision is not null && requestedRevision != value.ProjectionRevision ||
            value.CanonicalRevision <= 0 || value.ProjectionRevision <= 0 || value.AfterSequence < -1)
            throw new ArgumentException("Cursor does not match the requested match, time, or revision.", nameof(cursor));
        return value;
    }

    public static string EncodeInsight(Guid matchId, int selectedTimeSeconds, long canonicalRevision,
        int projectionRevision, long afterSequence, string afterInsightId) =>
        Encode(new InsightCursor(matchId, selectedTimeSeconds, canonicalRevision, projectionRevision, afterSequence, afterInsightId));

    public static InsightCursor DecodeInsight(string cursor, Guid matchId, int selectedTimeSeconds, int? requestedRevision)
    {
        var value = Decode<InsightCursor>(cursor);
        if (value.MatchId != matchId || value.SelectedTimeSeconds != selectedTimeSeconds ||
            requestedRevision is not null && requestedRevision != value.ProjectionRevision ||
            value.CanonicalRevision <= 0 || value.ProjectionRevision <= 0 || value.AfterSequence < 0 ||
            string.IsNullOrWhiteSpace(value.AfterInsightId) || value.AfterInsightId.Length > 256)
            throw new ArgumentException("Cursor does not match the requested match, time, or revision.", nameof(cursor));
        return value;
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

    internal sealed record EventCursor(Guid MatchId, int SelectedTimeSeconds, long CanonicalRevision,
        int ProjectionRevision, long AfterSequence);
    internal sealed record InsightCursor(Guid MatchId, int SelectedTimeSeconds, long CanonicalRevision,
        int ProjectionRevision, long AfterSequence, string AfterInsightId);
}
