namespace PitchMind.Contracts.V1;

/// <summary>A revisioned cursor notification. It never carries canonical match facts.</summary>
public sealed record RealtimeCursorV1(
    string Kind,
    string MatchId,
    long CanonicalRevision,
    long LastSequence,
    DateTimeOffset ServerUtc,
    int SchemaVersion);

public sealed record RealtimeNegotiateRequest(Guid MatchId, long? ClientRevision);

/// <summary>LOCAL SignalR endpoint details plus the canonical head cursor observed at negotiation time.</summary>
public sealed record RealtimeNegotiateResponse(
    string Transport,
    string Url,
    string? AccessToken,
    RealtimeCursorV1 Cursor,
    bool RevisionChanged);
