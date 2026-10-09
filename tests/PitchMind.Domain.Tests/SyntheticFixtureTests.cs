using PitchMind.Domain.Simulation;
using PitchMind.Domain.Events;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
namespace PitchMind.Domain.Tests;
public sealed class SyntheticFixtureTests
{
    [Fact] public void FixtureIsRepeatableAndComplete()
    {
        var first = SyntheticFixtureGenerator.Generate(); var second = SyntheticFixtureGenerator.Generate();
        Assert.Equal(first.Events, second.Events); Assert.Equal(5400, first.DurationSeconds);
        Assert.Equal(MatchEventType.KickOff, first.Events[0].Event.Type);
        Assert.Equal(MatchEventType.FullTime, first.Events[^1].Event.Type);
        Assert.Equal(Enumerable.Range(0, first.Events.Count).Select(i => (long)i), first.Events.Select(e => e.Event.Sequence));
        Assert.Equal(first.Events.Count, first.Events.Select(e => e.Event.EventId).Distinct().Count());
        Assert.All(first.Frames, frame => { Assert.Equal(22, frame.Players.Count); Assert.All(frame.Players, p => { Assert.InRange(p.X, 0, 105); Assert.InRange(p.Y, 0, 68); }); });
    }
    [Fact] public void AttackDirectionChangesAtHalfTime()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        Assert.Equal(5, fixture.Frames.Single(f => f.ClockSeconds == 2670).Players.Single(p => p.PlayerId == "harbour-1").X);
        Assert.Equal(100, fixture.Frames.Single(f => f.ClockSeconds == 2700).Players.Single(p => p.PlayerId == "harbour-1").X);
    }

    [Fact] public void SeedAndFixtureVersionProduceStableIdentitiesAndFrames()
    {
        var first = SyntheticFixtureGenerator.Generate(123);
        var second = SyntheticFixtureGenerator.Generate(123);
        var third = SyntheticFixtureGenerator.Generate(123);
        var differentSeed = SyntheticFixtureGenerator.Generate(124);

        Assert.Equal(first.MatchId, second.MatchId);
        Assert.NotEqual(first.MatchId, differentSeed.MatchId);
        Assert.Equal(first.Events, second.Events);
        Assert.Equal(JsonSerializer.Serialize(first.Frames), JsonSerializer.Serialize(second.Frames));
        Assert.Equal(Fingerprint(first), Fingerprint(second));
        Assert.Equal(Fingerprint(second), Fingerprint(third));
        Assert.NotEqual(first.Events[0].Event.EventId, differentSeed.Events[0].Event.EventId);
        Assert.Equal("synthetic-v2", first.Version);
        Assert.Equal("synthetic", first.Provenance);
    }

    [Fact] public void ScenarioIncludesPeriodsCardsSubstitutionAndLegalScoreTransitions()
    {
        var fixture = SyntheticFixtureGenerator.Generate();
        var events = fixture.Events;
        Assert.Contains(events, e => e.Event.Type == MatchEventType.HalfTime && e.Event.Period == 1);
        Assert.Contains(events, e => e.Event.Type == MatchEventType.KickOff && e.Event.Period == 2);
        Assert.Contains(events, e => e.Event.Type == MatchEventType.YellowCard);
        Assert.Contains(events, e => e.Event.Type == MatchEventType.Substitution && e.RelatedPlayerId is not null);
        Assert.Equal(5400, events[^1].Event.ClockSeconds);

        var home = 0;
        var away = 0;
        foreach (var item in events)
        {
            if (item.Event.Type != MatchEventType.Goal) continue;
            if (item.TeamId == fixture.Home.Id) home++;
            else if (item.TeamId == fixture.Away.Id) away++;
            else Assert.Fail("A goal must belong to one of the fixture teams.");
        }
        Assert.Equal((1, 1), (home, away));

        var beforeHomeSubstitution = fixture.Frames.Single(f => f.ClockSeconds == 3570).Players;
        var afterHomeSubstitution = fixture.Frames.Single(f => f.ClockSeconds == 3600).Players;
        Assert.Contains(beforeHomeSubstitution, p => p.PlayerId == "harbour-7");
        Assert.DoesNotContain(beforeHomeSubstitution, p => p.PlayerId == "harbour-12");
        Assert.DoesNotContain(afterHomeSubstitution, p => p.PlayerId == "harbour-7");
        Assert.Contains(afterHomeSubstitution, p => p.PlayerId == "harbour-12");

        var beforeAwaySubstitution = fixture.Frames.Single(f => f.ClockSeconds == 4080).Players;
        var afterAwaySubstitution = fixture.Frames.Single(f => f.ClockSeconds == 4100).Players;
        Assert.Contains(beforeAwaySubstitution, p => p.PlayerId == "ridge-9");
        Assert.DoesNotContain(beforeAwaySubstitution, p => p.PlayerId == "ridge-12");
        Assert.DoesNotContain(afterAwaySubstitution, p => p.PlayerId == "ridge-9");
        Assert.Contains(afterAwaySubstitution, p => p.PlayerId == "ridge-12");
    }

    [Fact] public void DefinitionRejectsEventsWhosePeriodConflictsWithMatchClock()
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        definition["scenarioEvents"]![0]!["period"] = 2;

        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    [Fact] public void DefinitionRejectsDuplicateTerminalMarkers()
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        var scenarioEvents = definition["scenarioEvents"]!.AsArray();
        scenarioEvents.Add(scenarioEvents[scenarioEvents.Count - 1]!.DeepClone());

        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    [Fact] public void DefinitionRejectsExtraKickoffOutsideThePeriodBoundaries()
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        var extraKickoff = definition["scenarioEvents"]![0]!.DeepClone();
        extraKickoff["clockSeconds"] = 100;
        definition["scenarioEvents"]!.AsArray().Add(extraKickoff);

        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    [Fact] public void DefinitionRejectsMissingStartTimeAndNullCollections()
    {
        var missingStart = JsonNode.Parse(ReadDefinition())!.AsObject();
        missingStart.Remove("startUtc");
        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(missingStart.ToJsonString()));

        var nullTeams = JsonNode.Parse(ReadDefinition())!;
        nullTeams["teams"] = null;
        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(nullTeams.ToJsonString()));

        var nullPlayer = JsonNode.Parse(ReadDefinition())!;
        nullPlayer["teams"]![0]!["players"]![0] = null;
        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(nullPlayer.ToJsonString()));
    }

    [Theory]
    [InlineData("pitchLengthMetres", 89)]
    [InlineData("pitchLengthMetres", 121)]
    [InlineData("pitchWidthMetres", 44)]
    [InlineData("pitchWidthMetres", 91)]
    public void DefinitionRejectsDimensionsOutsideFootballPitchRanges(string property, double value)
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        definition[property] = value;
        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    [Fact] public void DefinitionRejectsFrameOverrideForInactivePlayer()
    {
        var definition = JsonNode.Parse(ReadDefinition())!;
        definition["frameOverrides"]![0]!["playerPositions"]!["harbour-12"] = new JsonArray(42, 24);

        Assert.Throws<InvalidOperationException>(() => SyntheticFixtureGenerator.GenerateFromDefinitionJson(definition.ToJsonString()));
    }

    private static string ReadDefinition()
    {
        using var stream = typeof(SyntheticFixtureGenerator).Assembly
            .GetManifestResourceStream("PitchMind.Domain.Simulation.synthetic-fixture-v2.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string Fingerprint(SyntheticFixture fixture)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(fixture));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
