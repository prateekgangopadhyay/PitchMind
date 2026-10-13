using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using PitchMind.Domain.Events;
using Xunit;

namespace PitchMind.Domain.Tests;

public sealed class MatchAggregateTests
{
    private static (MatchAggregate Aggregate, SyntheticFixture Fixture) Create()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var players = fixture.Home.Players.Concat(fixture.Away.Players).Select(player => (player.Id, player.TeamId));
        var active = fixture.Frames.Single(frame => frame.ClockSeconds == 0).Players.Select(player => player.PlayerId);
        return (new(new(Guid.Parse(fixture.MatchId)), fixture.Home.Id, fixture.Away.Id, players, active, fixture.Provenance), fixture);
    }

    [Fact]
    public void SameEventIdAndPayloadIsIdempotent()
    {
        var (aggregate, fixture) = Create();
        var kickoff = fixture.Events[0];
        aggregate.Apply(kickoff);
        aggregate.Apply(kickoff);

        Assert.Equal(0, aggregate.Snapshot().LastSequence);
        Assert.Equal(kickoff.TeamId, aggregate.Snapshot().PossessionTeamId);
    }

    [Fact]
    public void EmptyEventIdIsRejectedWithoutChangingState()
    {
        var (aggregate, fixture) = Create();
        var invalidEvent = fixture.Events[0] with
        {
            Event = fixture.Events[0].Event with { EventId = Guid.Empty }
        };

        Assert.Throws<InvalidOperationException>(() => aggregate.Apply(invalidEvent));

        var state = aggregate.Snapshot();
        Assert.Equal(-1, state.LastSequence);
        Assert.Equal(MatchPhase.NotStarted, state.Phase);
        Assert.Equal(0, state.HomeScore);
        Assert.Equal(0, state.AwayScore);
    }

    [Fact]
    public void ReusedEventIdWithDifferentPayloadIsRejectedWithoutChangingState()
    {
        var (aggregate, fixture) = Create();
        var kickoff = fixture.Events[0];
        aggregate.Apply(kickoff);

        Assert.Throws<InvalidOperationException>(() => aggregate.Apply(kickoff with { Outcome = "changed" }));
        Assert.Equal(0, aggregate.Snapshot().LastSequence);
        Assert.Equal(0, aggregate.Snapshot().HomeScore);
    }

    [Fact]
    public void ProjectionCapturesCardsSubstitutionsGoalsAndPeriodTransitions()
    {
        var (aggregate, fixture) = Create();
        foreach (var item in fixture.Events) aggregate.Apply(item);
        var state = aggregate.Snapshot();

        Assert.Equal(MatchPhase.FullTime, state.Phase);
        Assert.Equal(2, state.Period);
        Assert.Equal(1, state.HomeScore);
        Assert.Equal(1, state.AwayScore);
        Assert.Equal(1, state.Players.Single(player => player.PlayerId == "ridge-4").YellowCards);
        Assert.False(state.Players.Single(player => player.PlayerId == "harbour-7").Active);
        Assert.True(state.Players.Single(player => player.PlayerId == "harbour-12").Active);
        Assert.Equal(11, state.HomeActivePlayers);
        Assert.Equal(11, state.AwayActivePlayers);
    }

    [Fact]
    public void ReapplyingGoalDoesNotScoreTwice()
    {
        var (aggregate, fixture) = Create();
        var goal = fixture.Events.First(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.Goal);
        foreach (var item in fixture.Events.TakeWhile(item => item.Event.Sequence <= goal.Event.Sequence)) aggregate.Apply(item);
        aggregate.Apply(goal);

        Assert.Equal(1, aggregate.Snapshot().HomeScore);
        Assert.Equal(goal.Event.Sequence, aggregate.Snapshot().LastSequence);
    }

    [Fact]
    public void SubstitutionOfInactiveOutgoingPlayerIsRejectedWithoutRosterMutation()
    {
        var (aggregate, fixture) = Create();
        var substitution = fixture.Events.First(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.Substitution);
        foreach (var item in fixture.Events.Where(item => item.Event.Sequence < substitution.Event.Sequence)) aggregate.Apply(item);
        var invalidSubstitution = substitution with
        {
            Event = substitution.Event with { EventId = Guid.NewGuid() },
            PlayerId = substitution.RelatedPlayerId!,
            RelatedPlayerId = substitution.PlayerId
        };

        Assert.Throws<InvalidOperationException>(() => aggregate.Apply(invalidSubstitution));
        var state = aggregate.Snapshot();
        Assert.True(state.Players.Single(player => player.PlayerId == "harbour-7").Active);
        Assert.False(state.Players.Single(player => player.PlayerId == "harbour-12").Active);
    }

    [Fact]
    public void EventAfterFullTimeIsRejectedWithoutChangingScore()
    {
        var (aggregate, fixture) = Create();
        foreach (var item in fixture.Events) aggregate.Apply(item);
        var afterFullTime = fixture.Events[^1] with
        {
            Event = fixture.Events[^1].Event with
            { EventId = Guid.NewGuid(), Sequence = fixture.Events[^1].Event.Sequence + 1, ClockSeconds = 5401 }
        };

        Assert.Throws<InvalidOperationException>(() => aggregate.Apply(afterFullTime));
        Assert.Equal(1, aggregate.Snapshot().HomeScore);
        Assert.Equal(1, aggregate.Snapshot().AwayScore);
    }

    [Fact]
    public void OutOfOrderSequenceIsRejected()
    {
        var (aggregate, fixture) = Create();
        Assert.Throws<InvalidOperationException>(() => aggregate.Apply(fixture.Events[1]));
        Assert.Equal(-1, aggregate.Snapshot().LastSequence);
        Assert.Equal(MatchPhase.NotStarted, aggregate.Snapshot().Phase);
    }

    [Fact]
    public void UndefinedEventTypeIsRejectedWithoutAdvancingMatchState()
    {
        var (aggregate, fixture) = Create();
        var invalid = fixture.Events[0] with
        {
            Event = fixture.Events[0].Event with { Type = (MatchEventType)999 }
        };

        Assert.Throws<InvalidOperationException>(() => aggregate.Apply(invalid));

        var state = aggregate.Snapshot();
        Assert.Equal(-1, state.LastSequence);
        Assert.Equal(0, state.ClockSeconds);
        Assert.Equal(MatchPhase.NotStarted, state.Phase);
        Assert.Null(state.PossessionTeamId);
    }

    [Fact]
    public void SecondCautionDismissesPlayerAndUpdatesNumericalSituation()
    {
        var (aggregate, fixture) = Create();
        var firstCaution = fixture.Events.First(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.YellowCard);
        foreach (var item in fixture.Events.TakeWhile(item => item.Event.Sequence <= firstCaution.Event.Sequence)) aggregate.Apply(item);
        var secondCaution = firstCaution with
        {
            Event = firstCaution.Event with { EventId = Guid.NewGuid(), Sequence = firstCaution.Event.Sequence + 1 }
        };
        aggregate.Apply(secondCaution);

        var state = aggregate.Snapshot();
        var cautionedPlayer = state.Players.Single(player => player.PlayerId == firstCaution.PlayerId);
        Assert.Equal(2, cautionedPlayer.YellowCards);
        Assert.True(cautionedPlayer.Dismissed);
        Assert.False(cautionedPlayer.Active);
        Assert.Equal(10, state.AwayActivePlayers);
    }

    [Fact]
    public void RedCardDismissesAnActivePlayer()
    {
        var (aggregate, fixture) = Create();
        var source = fixture.Events.First(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.YellowCard);
        foreach (var item in fixture.Events.TakeWhile(item => item.Event.Sequence < source.Event.Sequence)) aggregate.Apply(item);
        aggregate.Apply(source with { Event = source.Event with { EventId = Guid.NewGuid(), Type = PitchMind.Domain.Events.MatchEventType.RedCard } });

        var state = aggregate.Snapshot();
        Assert.True(state.Players.Single(player => player.PlayerId == source.PlayerId).Dismissed);
        Assert.Equal(10, state.AwayActivePlayers);
    }

    [Fact]
    public void DismissedPlayerCannotReturnAsSubstitute()
    {
        var (aggregate, fixture) = Create();
        var firstSubstitution = fixture.Events.First(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.Substitution && item.TeamId == fixture.Home.Id);
        foreach (var item in fixture.Events.TakeWhile(item => item.Event.Sequence <= firstSubstitution.Event.Sequence)) aggregate.Apply(item);
        var nextSequence = firstSubstitution.Event.Sequence + 1;
        var secondCaution = fixture.Events.First(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.YellowCard) with
        {
            Event = fixture.Events.First(item => item.Event.Type == PitchMind.Domain.Events.MatchEventType.YellowCard).Event with
            { EventId = Guid.NewGuid(), Sequence = nextSequence++, ClockSeconds = firstSubstitution.Event.ClockSeconds + 1, Period = firstSubstitution.Event.Period },
            PlayerId = firstSubstitution.RelatedPlayerId!,
            TeamId = fixture.Home.Id
        };
        aggregate.Apply(secondCaution);
        aggregate.Apply(secondCaution with { Event = secondCaution.Event with { EventId = Guid.NewGuid(), Sequence = nextSequence++ } });
        var returnAttempt = firstSubstitution with
        {
            Event = firstSubstitution.Event with
            { EventId = Guid.NewGuid(), Sequence = nextSequence, ClockSeconds = firstSubstitution.Event.ClockSeconds + 2 },
            PlayerId = "harbour-8",
            RelatedPlayerId = firstSubstitution.RelatedPlayerId
        };

        Assert.Throws<InvalidOperationException>(() => aggregate.Apply(returnAttempt));
        Assert.False(aggregate.Snapshot().Players.Single(player => player.PlayerId == firstSubstitution.RelatedPlayerId).Active);
    }

    [Fact]
    public void InitialRosterRequiresElevenPlayersPerTeam()
    {
        var (aggregate, fixture) = Create();
        var allHome = Enumerable.Range(1, 22).Select(index => ($"home-{index}", fixture.Home.Id)).ToArray();

        Assert.Throws<ArgumentException>(() => new MatchAggregate(aggregate.Id, fixture.Home.Id, fixture.Away.Id,
            allHome, allHome.Select(player => player.Item1), fixture.Provenance));
    }
}
