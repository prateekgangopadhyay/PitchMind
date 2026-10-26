using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PitchMind.Api.Tests;

[Collection("EnvironmentVariables")]
public sealed class SimulationEndpointTests : IDisposable
{
    public SimulationEndpointTests()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("Runtime__Profile", "local");
        Environment.SetEnvironmentVariable("Identity__Authority", null);
        Environment.SetEnvironmentVariable("Identity__Audience", null);
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", "true");
        Environment.SetEnvironmentVariable("DISABLE_FOUNDRY_AGENT", "true");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("Identity__Authority", null);
        Environment.SetEnvironmentVariable("Identity__Audience", null);
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", null);
        Environment.SetEnvironmentVariable("DISABLE_FOUNDRY_AGENT", null);
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>();

    [Fact]
    public async Task AuthPreviewEndpointReturnsExpectedStructure()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/simulation/auth-preview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("headline", out var headline));
        Assert.False(string.IsNullOrWhiteSpace(headline.GetString()));
        Assert.True(root.TryGetProperty("featuredMatch", out var match));
        Assert.True(match.TryGetProperty("homeScore", out _));
        Assert.True(root.TryGetProperty("valueBadges", out var badges));
        Assert.True(badges.GetArrayLength() >= 4);
    }

    [Fact]
    public async Task HubEndpointReturnsHeroAndLiveMatches()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/simulation/hub");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("heroMatch", out var hero));
        Assert.True(hero.GetProperty("homeTeam").GetString() is "Arsenal" or "Manchester United");
        Assert.True(root.TryGetProperty("otherLiveMatches", out var others));
        Assert.True(others.GetArrayLength() >= 3);
        Assert.True(root.TryGetProperty("leagueSnapshot", out var standings));
        Assert.True(standings.GetArrayLength() >= 6);
    }

    [Fact]
    public async Task TeamsEndpointReturnsTacticalProfile()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/simulation/teams/arsenal");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Arsenal", root.GetProperty("name").GetString());
        Assert.Equal("4-3-3", root.GetProperty("formation").GetString());
        Assert.True(root.TryGetProperty("progressionRoutes", out var routes));
        Assert.True(routes.GetProperty("right").GetInt32() > 50);
    }

    [Fact]
    public async Task PlayersEndpointReturnsPlayerIntelligence()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/simulation/players/saka");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Bukayo Saka", root.GetProperty("name").GetString());
        Assert.Equal(7, root.GetProperty("number").GetInt32());
        Assert.True(root.TryGetProperty("whyItMatters", out var reasons));
        Assert.Equal(4, reasons.GetArrayLength());
    }

    [Fact]
    public async Task InsightsEndpointReturnsTacticalInsight()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/simulation/insights/arsenal-press-city");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Contains("press", root.GetProperty("title").GetString()!);
        Assert.True(root.TryGetProperty("causalChain", out var chain));
        Assert.Equal(4, chain.GetArrayLength());
    }

    [Fact]
    public async Task AnalystOverviewEndpointReturnsDiscussionAndSidebar()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/simulation/analyst/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("conversation", out var conversation));
        Assert.True(conversation.GetArrayLength() >= 2);
        Assert.True(root.TryGetProperty("matchContextSidebar", out var sidebar));
        Assert.True(sidebar.TryGetProperty("keyMoments", out _));
    }

    [Fact]
    public async Task SimulatedLoginSucceedsWithValidCredentials()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var content = new StringContent(
            JsonSerializer.Serialize(new { email = "analyst@pitchmind.io", password = "SecurePassword123!" }),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync("/api/v1/simulation/auth/login", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task NewPlayerIntelligenceEndpointReturnsDynamicIntelligence()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/players/saka/intelligence?seconds=4044");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Bukayo Saka", root.GetProperty("name").GetString());
        Assert.Equal(7, root.GetProperty("number").GetInt32());
        Assert.Equal(67, root.GetProperty("minutesPlayed").GetInt32());
        Assert.True(root.TryGetProperty("keyQuote", out _));
        Assert.True(root.TryGetProperty("matchStats", out var stats));
        Assert.True(stats.TryGetProperty("xa", out var xaProp) || stats.TryGetProperty("xA", out xaProp));
        Assert.Equal(0.32, xaProp.GetDouble());
        Assert.True(root.TryGetProperty("whyItMatters", out var reasons));
        Assert.Equal(4, reasons.GetArrayLength());
        Assert.True(root.TryGetProperty("touches", out var touches));
        Assert.True(touches.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("progressiveCarries", out var carries));
        Assert.True(carries.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("chanceCreation", out var chances));
        Assert.True(chances.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("receivingZones", out var zones));
        Assert.Equal(12, zones.GetArrayLength());
        Assert.True(root.TryGetProperty("defensiveWork", out var def));
        Assert.True(def.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("cumulativeXA", out var cumXA));
        Assert.True(cumXA.GetArrayLength() > 0);
        Assert.True(root.TryGetProperty("comparison", out var comp));
        Assert.Equal("Phil Foden", comp.GetProperty("opponentPlayer").GetString());
        Assert.True(root.TryGetProperty("recentImpact", out var impact));
        Assert.Equal(4, impact.GetArrayLength());
        Assert.True(root.TryGetProperty("analystAssessment", out var assessment));
        Assert.False(string.IsNullOrWhiteSpace(assessment.GetString()));
    }

    [Fact]
    public async Task NewPlayerIntelligenceEndpointCalculatesBrunoFernandesFromStream()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/players/bruno-fernandes/intelligence?seconds=5400");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("Bruno Fernandes", root.GetProperty("name").GetString());
        Assert.Equal("Manchester United", root.GetProperty("club").GetString());
        Assert.Equal(8, root.GetProperty("number").GetInt32());
        Assert.Equal(90, root.GetProperty("minutesPlayed").GetInt32());

        // 1. whyItMatters
        Assert.True(root.TryGetProperty("whyItMatters", out var whyItMatters));
        Assert.True(whyItMatters.GetArrayLength() >= 2);

        // 2. matchStats (calculated dynamically from 2,199 events in manu-liv-match-dataset.json)
        Assert.True(root.TryGetProperty("matchStats", out var matchStats));
        Assert.True(matchStats.GetProperty("touches").GetInt32() >= 100);
        Assert.True(matchStats.GetProperty("chancesCreated").GetInt32() >= 1);
        Assert.True(matchStats.GetProperty("progressiveCarries").GetInt32() >= 1);
        Assert.True(matchStats.GetProperty("shots").GetInt32() >= 1);
        Assert.True(matchStats.GetProperty("duelsWon").GetInt32() >= 1);

        // 3. touches (sampled touch coordinates for pitch visualization)
        Assert.True(root.TryGetProperty("touches", out var touches));
        Assert.True(touches.GetArrayLength() >= 10);

        // 4. progressiveCarries
        Assert.True(root.TryGetProperty("progressiveCarries", out var carries));
        Assert.True(carries.GetArrayLength() >= 1);

        // 5. chanceCreation
        Assert.True(root.TryGetProperty("chanceCreation", out var chances));
        Assert.True(chances.GetArrayLength() >= 1);

        // 6. receivingZones
        Assert.True(root.TryGetProperty("receivingZones", out var zones));
        Assert.Equal(12, zones.GetArrayLength());

        // 7. defensiveWork
        Assert.True(root.TryGetProperty("defensiveWork", out var defensiveWork));
        Assert.True(defensiveWork.GetArrayLength() >= 1);

        // 8. cumulativeXA
        Assert.True(root.TryGetProperty("cumulativeXA", out var cumXA));
        Assert.True(cumXA.GetArrayLength() >= 1);

        // 9. comparison (counterpart head-to-head comparison with Dominik Szoboszlai)
        Assert.True(root.TryGetProperty("comparison", out var comp));
        Assert.Equal("Dominik Szoboszlai", comp.GetProperty("opponentPlayer").GetString());
        Assert.True(comp.TryGetProperty("metrics", out var metrics));
        Assert.True(metrics.GetArrayLength() >= 4);

        // 10. recentImpact
        Assert.True(root.TryGetProperty("recentImpact", out var impact));
        Assert.True(impact.GetArrayLength() >= 1);

        // 11. analystAssessment
        Assert.True(root.TryGetProperty("analystAssessment", out var assessment));
        Assert.False(string.IsNullOrWhiteSpace(assessment.GetString()));

        // 12. progressiveCarriesStat
        Assert.True(root.TryGetProperty("progressiveCarriesStat", out var carriesStat));
        Assert.True(carriesStat.GetProperty("total").GetInt32() >= 1);

        // 13. chanceCreationStat
        Assert.True(root.TryGetProperty("chanceCreationStat", out var chanceStat));
        Assert.True(chanceStat.GetProperty("chancesCreated").GetInt32() >= 1);

        // 14. defensiveWorkStats
        Assert.True(root.TryGetProperty("defensiveWorkStats", out var defStats));
        Assert.True(defStats.TryGetProperty("pressures", out _));
    }

    [Fact]
    public async Task PlayersListEndpointReturnsAllTwentyTwoStreamPlayers()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/players");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal(22, root.GetArrayLength());

        var first = root[0];
        Assert.Equal("Bruno Fernandes", first.GetProperty("name").GetString());
        Assert.Equal("Manchester United", first.GetProperty("club").GetString());
        Assert.True(first.TryGetProperty("matchStats", out var stats));
        Assert.True(stats.GetProperty("touches").GetInt32() >= 100);
    }
}
