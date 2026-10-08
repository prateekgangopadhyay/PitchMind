using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Domain.Tests;

public sealed class MatchEventTests
{
    [Xunit.Fact]
    public void Create_RejectsNegativeSequence()
    {
        Xunit.Assert.Throws<ArgumentOutOfRangeException>(() =>
            MatchEvent.Create(Guid.NewGuid(), MatchId.New(), -1, MatchEventType.Pass, 12, 1, DateTimeOffset.UtcNow));
    }

    [Xunit.Fact]
    public void Create_PreservesEventIdentity()
    {
        var id = Guid.NewGuid();
        var result = MatchEvent.Create(id, MatchId.New(), 1, MatchEventType.Pass, 12, 1, DateTimeOffset.UtcNow);
        Xunit.Assert.Equal(id, result.EventId);
    }
}
