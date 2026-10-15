using PitchMind.Domain.Match;

namespace PitchMind.Application.Matches;

public enum MatchEventIngestionFailure
{
    EventIdPayloadConflict,
    InvalidEventPayload,
    SequenceAlreadyUsed,
    SequenceGap,
    OutOfOrder
}

public enum MatchEventIngestionDisposition
{
    Quarantine,
    HoldForReconciliation
}

/// <summary>Stable machine-readable failure context for a rejected canonical event append.</summary>
public sealed class MatchEventIngestionException : InvalidOperationException
{
    public MatchEventIngestionException(
        MatchId matchId,
        Guid eventId,
        MatchEventIngestionFailure failure,
        long? expectedSequence,
        long receivedSequence,
        Exception? innerException = null)
        : base(CreateMessage(failure, expectedSequence, receivedSequence), innerException)
    {
        MatchId = matchId;
        EventId = eventId;
        Failure = failure;
        ExpectedSequence = expectedSequence;
        ReceivedSequence = receivedSequence;
    }

    public MatchId MatchId { get; }
    public Guid EventId { get; }
    public MatchEventIngestionFailure Failure { get; }
    public long? ExpectedSequence { get; }
    public long ReceivedSequence { get; }
    public string Code => Failure switch
    {
        MatchEventIngestionFailure.EventIdPayloadConflict => "EVENT_ID_PAYLOAD_CONFLICT",
        MatchEventIngestionFailure.InvalidEventPayload => "INVALID_EVENT_PAYLOAD",
        MatchEventIngestionFailure.SequenceAlreadyUsed => "SEQUENCE_ALREADY_USED",
        MatchEventIngestionFailure.SequenceGap => "SEQUENCE_GAP",
        MatchEventIngestionFailure.OutOfOrder => "OUT_OF_ORDER_EVENT",
        _ => throw new ArgumentOutOfRangeException(nameof(Failure))
    };

    public MatchEventIngestionDisposition Disposition => Failure is
        MatchEventIngestionFailure.EventIdPayloadConflict or MatchEventIngestionFailure.InvalidEventPayload or MatchEventIngestionFailure.SequenceAlreadyUsed
            ? MatchEventIngestionDisposition.Quarantine
            : MatchEventIngestionDisposition.HoldForReconciliation;

    private static string CreateMessage(MatchEventIngestionFailure failure, long? expectedSequence, long receivedSequence) =>
        $"Canonical event append rejected ({failure}); expected sequence {expectedSequence?.ToString() ?? "unknown"}, received {receivedSequence}.";
}
