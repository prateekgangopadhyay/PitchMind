using PitchMind.Contracts.Streaming;
using PitchMind.Contracts.V1;

namespace PitchMind.Api;

/// <summary>Projects a committed ProjectionPublishedV1 outbox payload into a cursor-only browser message.</summary>
public static class RealtimeCursorTranslator
{
    public static RealtimeCursorV1 FromProjectionPublishedV1(Guid outboxMessageId, string payloadJson,
        DateTimeOffset serverUtc)
    {
        var envelope = ProjectionPublishedV1Envelope.Parse(outboxMessageId, payloadJson);
        if (serverUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Cursor server time must be UTC.", nameof(serverUtc));

        return new RealtimeCursorV1("matchUpdated", envelope.MatchId.ToString("D"), envelope.CanonicalRevision,
            envelope.Sequence, serverUtc, 1);
    }
}
