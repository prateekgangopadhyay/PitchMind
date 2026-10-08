using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class OrderedMatchEventBufferTests
{
    private readonly MatchId _match = MatchId.New();
    private MatchEvent Event(long sequence, Guid? id = null, MatchId? match = null) =>
        MatchEvent.Create(id ?? Guid.NewGuid(), match ?? _match, sequence, MatchEventType.Pass, 10, 1, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ReordersOutOfOrderArrivalsWithoutPublishingGaps()
    {
        var buffer = new OrderedMatchEventBuffer(_match);
        Assert.Equal(IngestionDisposition.Buffered, buffer.Accept(Event(2)).Disposition);
        Assert.Empty(buffer.Accept(Event(1)).Ready);
        var ready = buffer.Accept(Event(0)).Ready;
        Assert.Equal(new long[] { 0, 1, 2 }, ready.Select(e => e.Sequence));
        Assert.Equal(3, buffer.NextExpectedSequence);
    }

    [Fact]
    public void DuplicateIdentityIsNeverPublishedTwice()
    {
        var buffer = new OrderedMatchEventBuffer(_match);
        var matchEvent = Event(0);
        Assert.Single(buffer.Accept(matchEvent).Ready);
        Assert.Equal(IngestionDisposition.Duplicate, buffer.Accept(matchEvent).Disposition);
    }

    [Fact]
    public void ConflictingSequenceIsQuarantined()
    {
        var buffer = new OrderedMatchEventBuffer(_match);
        buffer.Accept(Event(2));
        Assert.Equal("conflicting_sequence", buffer.Accept(Event(2)).Reason);
    }

    [Fact]
    public void WrongMatchAndStaleSequenceAreQuarantined()
    {
        var buffer = new OrderedMatchEventBuffer(_match);
        Assert.Equal("wrong_match", buffer.Accept(Event(0, match: MatchId.New())).Reason);
        buffer.Accept(Event(0));
        Assert.Equal("stale_sequence", buffer.Accept(Event(0)).Reason);
    }

    [Fact]
    public void PendingBufferIsBounded()
    {
        var buffer = new OrderedMatchEventBuffer(_match, maxPending: 1);
        Assert.Equal(IngestionDisposition.Buffered, buffer.Accept(Event(2)).Disposition);
        Assert.Equal("pending_capacity_exceeded", buffer.Accept(Event(3)).Reason);
        Assert.Equal(1, buffer.PendingCount);
    }
}
