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
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("Identity__Authority", null);
        Environment.SetEnvironmentVariable("Identity__Audience", null);
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", null);
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
}
