using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class ApiSecurityTests
{
    [Fact]
    public async Task Jwt_bearer_validation_rejects_wrong_issuer_audience_expiry_and_signature()
    {
        const string issuer = "https://issuer.test.local/tenant/v2.0";
        const string audience = "api://pitchmind-test";
        using var signingKey = RSA.Create(2048);
        using var foreignKey = RSA.Create(2048);
        using var factory = ApiTestHost.CreateJwtFactory(issuer, audience, signingKey);
        using var client = factory.CreateClient();

        using var validFan = await SendBearer(client, "/api/v1/canonical/fixtures",
            CreateToken(issuer, audience, signingKey, "fan"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, validFan.StatusCode);

        using var wrongIssuer = await SendBearer(client, "/api/v1/canonical/fixtures",
            CreateToken("https://attacker.test.local/", audience, signingKey, "fan"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);

        using var wrongAudience = await SendBearer(client, "/api/v1/canonical/fixtures",
            CreateToken(issuer, "api://another-api", signingKey, "fan"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);

        using var expired = await SendBearer(client, "/api/v1/canonical/fixtures",
            CreateToken(issuer, audience, signingKey, "fan", DateTime.UtcNow.AddMinutes(-10)));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        using var invalidSignature = await SendBearer(client, "/api/v1/canonical/fixtures",
            CreateToken(issuer, audience, foreignKey, "fan"));
        Assert.Equal(HttpStatusCode.Unauthorized, invalidSignature.StatusCode);
    }

    [Fact]
    public async Task Jwt_bearer_roles_are_applied_after_cryptographic_validation()
    {
        const string issuer = "https://issuer.test.local/tenant/v2.0";
        const string audience = "api://pitchmind-test";
        using var signingKey = RSA.Create(2048);
        using var factory = ApiTestHost.CreateJwtFactory(issuer, audience, signingKey);
        using var client = factory.CreateClient();
        var evidence = "/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/evidence/press-1?atSeconds=0";

        using var missingRole = await SendBearer(client, "/api/v1/canonical/fixtures",
            CreateToken(issuer, audience, signingKey));
        Assert.Equal(HttpStatusCode.Forbidden, missingRole.StatusCode);

        using var fanEvidence = await SendBearer(client, evidence, CreateToken(issuer, audience, signingKey, "fan"));
        Assert.Equal(HttpStatusCode.Forbidden, fanEvidence.StatusCode);

        using var analystEvidence = await SendBearer(client, evidence, CreateToken(issuer, audience, signingKey, "analyst"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, analystEvidence.StatusCode);
    }

    [Fact]
    public async Task Canonical_quota_follows_validated_subject_across_client_ips_and_returns_typed_problem()
    {
        const string issuer = "https://issuer.test.local/tenant/v2.0";
        const string audience = "api://pitchmind-test";
        using var signingKey = RSA.Create(2048);
        using var factory = ApiTestHost.CreateJwtFactory(issuer, audience, signingKey);
        using var client = factory.CreateClient();
        var token = CreateToken(issuer, audience, signingKey, "fan");

        for (var requestNumber = 0; requestNumber < 120; requestNumber++)
        {
            using var response = await SendBearer(client, "/api/v1/canonical/fixtures", token, "192.0.2.10");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        using var requestFromAnotherIp = await SendBearer(client, "/api/v1/canonical/fixtures", token, "198.51.100.20");
        Assert.Equal(HttpStatusCode.TooManyRequests, requestFromAnotherIp.StatusCode);
        Assert.Equal("application/problem+json", requestFromAnotherIp.Content.Headers.ContentType?.MediaType);
        Assert.Equal("RATE_LIMITED", await ProblemCode(requestFromAnotherIp));
        Assert.DoesNotContain("local-security-test", await requestFromAnotherIp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyst_quota_remains_thirty_per_minute_for_local_ip_fallback()
    {
        using var factory = ApiTestHost.CreateFactory();
        using var client = factory.CreateClient();
        var route = "/api/v1/matches/00000000-0000-0000-0000-000000000001/analyst";

        for (var requestNumber = 0; requestNumber < 30; requestNumber++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new { insightId = "test", atSeconds = 0, audience = "fan" })
            };
            request.Headers.Add("X-Test-Remote-IP", "203.0.113.10");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var rejected = new HttpRequestMessage(HttpMethod.Post, route)
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { insightId = "test", atSeconds = 0, audience = "fan" })
        };
        rejected.Headers.Add("X-Test-Remote-IP", "203.0.113.10");
        using var rejectedResponse = await client.SendAsync(rejected);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejectedResponse.StatusCode);
        Assert.Equal("RATE_LIMITED", await ProblemCode(rejectedResponse));
    }

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

    private static string CreateToken(string issuer, string audience, RSA signingKey, string? role = null,
        DateTime? expiresUtc = null)
    {
        var claims = new List<Claim> { new("sub", "local-security-test") };
        if (role is not null) claims.Add(new Claim("roles", role));
        var expiry = expiresUtc ?? DateTime.UtcNow.AddMinutes(5);
        var notBefore = expiry < DateTime.UtcNow ? expiry.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(-1);
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore, expiry,
            new SigningCredentials(new RsaSecurityKey(signingKey), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<HttpResponseMessage> SendBearer(HttpClient client, string route, string token,
        string? testRemoteIp = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (testRemoteIp is not null) request.Headers.Add("X-Test-Remote-IP", testRemoteIp);
        return await client.SendAsync(request);
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }
}
