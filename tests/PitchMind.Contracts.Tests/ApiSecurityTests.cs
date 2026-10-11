using System.Net;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class ApiSecurityTests
{
    [Fact]
    public async Task Canonical_routes_reject_anonymous_and_unapproved_roles()
    {
        using var factory = ApiTestHost.CreateFactory();
        using var anonymous = factory.CreateClient();
        using var coach = ApiTestHost.CreateClient(factory, "coach");
        using var fan = ApiTestHost.CreateClient(factory, "fan");

        using var anonymousResponse = await anonymous.GetAsync("/api/v1/canonical/fixtures");
        using var coachResponse = await coach.GetAsync("/api/v1/canonical/fixtures");
        using var fanResponse = await fan.GetAsync("/api/v1/canonical/fixtures");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, coachResponse.StatusCode);
        Assert.Equal("application/problem+json", anonymousResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", coachResponse.Content.Headers.ContentType?.MediaType);
        using var anonymousBody = System.Text.Json.JsonDocument.Parse(await anonymousResponse.Content.ReadAsStringAsync());
        using var coachBody = System.Text.Json.JsonDocument.Parse(await coachResponse.Content.ReadAsStringAsync());
        Assert.Equal("UNAUTHORIZED", anonymousBody.RootElement.GetProperty("code").GetString());
        Assert.Equal("FORBIDDEN", coachBody.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(anonymousResponse.Headers.GetValues("X-Correlation-ID").Single()));
        // This test host has no PostgreSQL adapter; 503 proves the approved role passed authorization.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, fanResponse.StatusCode);
    }

    [Fact]
    public async Task Full_evidence_requires_analyst_or_admin_role()
    {
        using var factory = ApiTestHost.CreateFactory();
        using var fan = ApiTestHost.CreateClient(factory, "fan");
        using var analyst = ApiTestHost.CreateClient(factory, "analyst");

        using var fanResponse = await fan.GetAsync(
            "/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/evidence/press-1");
        using var analystResponse = await analyst.GetAsync(
            "/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/evidence/press-1");

        Assert.Equal(HttpStatusCode.Forbidden, fanResponse.StatusCode);
        // Authorization succeeded; no persistence adapter is configured in this contract test host.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, analystResponse.StatusCode);
    }

    [Fact]
    public async Task Fixture_demo_routes_exist_only_in_development_local_profile()
    {
        using var localFactory = ApiTestHost.CreateFactory();
        using var local = localFactory.CreateClient();
        using var localResponse = await local.GetAsync("/api/v1/matches");
        Assert.Equal(HttpStatusCode.OK, localResponse.StatusCode);

        using var cloudFactory = ApiTestHost.CreateFactory(
            "Production", "azure", "https://login.example.test/tenant/v2.0", "api://pitchmind");
        using var cloud = cloudFactory.CreateClient();
        using var cloudResponse = await cloud.GetAsync("/api/v1/matches");
        Assert.Equal(HttpStatusCode.NotFound, cloudResponse.StatusCode);
    }

    [Theory]
    [InlineData(null, "api://pitchmind", "Authentication:Issuer")]
    [InlineData("https://login.example.test/tenant/v2.0", null, "Authentication:Audience")]
    public void Non_local_profiles_reject_missing_issuer_or_audience(string? issuer, string? audience, string missingSetting)
    {
        var values = new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = issuer,
            ["Authentication:Audience"] = audience
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PitchMind.Api.ApiSecurityConfiguration.ResolveJwtSettings(false, "azure", configuration));

        Assert.Contains(missingSetting, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_local_profiles_reject_non_https_issuer_and_authority()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = "http://login.example.test/tenant/v2.0",
            ["Authentication:Audience"] = "api://pitchmind"
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PitchMind.Api.ApiSecurityConfiguration.ResolveJwtSettings(false, "production", configuration));

        Assert.Contains("absolute HTTPS URI", error.Message, StringComparison.Ordinal);
    }
}
