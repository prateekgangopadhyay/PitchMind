using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;

namespace PitchMind.Application.Tests;

public sealed class MatchEventValidatorTests
{
    [Xunit.Fact]
    public void Validate_AcceptsValidEvent()
    {
        var evt = MatchEvent.Create(Guid.NewGuid(), MatchId.New(), 1, MatchEventType.Pass, 1, 1, DateTimeOffset.UtcNow);
        MatchEventValidator.Validate(evt);
    }
}
