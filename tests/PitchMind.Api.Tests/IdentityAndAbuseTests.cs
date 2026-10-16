using System.Net;
using System.IdentityModel.Tokens.Jwt;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace PitchMind.Api.Tests;

[Collection("EnvironmentVariables")]
public sealed class IdentityAndAbuseTests
{
    [Fact]
    public async Task CanonicalReadsRequireAnAuthenticatedRoleAndEmitSecurityHeaders()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/canonical/fixtures");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ProblemCode(response));
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Contains("frame-ancestors 'none'", Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task CorsPreflightAllowsOnlyConfiguredOriginsAndKeepsSecurityHeaders()
    {
        const string allowedOrigin = "https://app.pitchmind.example";
        using var factory = CreateCorsFactory(allowedOrigin);
        using var client = factory.CreateClient();

        using var allowed = await SendPreflight(client, allowedOrigin);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal(allowedOrigin, Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Contains("nosniff", allowed.Headers.GetValues("X-Content-Type-Options"));

        using var denied = await SendPreflight(client, "https://attacker.example");
        Assert.False(denied.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ReplayRejectsFanRoleAndPassesAnalystRoleToCanonicalAdapter()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var route = "/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/replay?atSeconds=0";

        using var fanResponse = await SendAs(client, route, "fan");
        Assert.Equal(HttpStatusCode.Forbidden, fanResponse.StatusCode);
        Assert.Equal("FORBIDDEN", await ProblemCode(fanResponse));

        using var analystResponse = await SendAs(client, route, "analyst");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, analystResponse.StatusCode);
        Assert.Equal("DEPENDENCY_UNAVAILABLE", await ProblemCode(analystResponse));
    }

    [Fact]
    public async Task MaliciousSelectedTimeIsRejectedBeforeCanonicalDatabaseAccess()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await SendAs(client,
            "/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/summary?atSeconds=999999", "fan");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_TIME", await ProblemCode(response));
    }

    [Fact]
    public async Task DevelopmentDoesNotServeUnapprovedSyntheticDemoData()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/matches");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("X-PitchMind-Data-Class"));
    }

    [Fact]
    public async Task OversizedRequestBodyIsRejectedWithTyped413()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Subject", "body-limit-analyst");
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "analyst");
        using var content = new ByteArrayContent(new byte[4097]);

        using var response = await client.PostAsync("/api/v1/matches/11111111-1111-1111-1111-111111111111/analyst", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("REQUEST_TOO_LARGE", await ProblemCode(response));
    }

    [Fact]
    public async Task CanonicalReadRateLimitReturnsTyped429()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        HttpResponseMessage? last = null;

        for (var request = 0; request < 121; request++)
        {
            last?.Dispose();
            last = await client.GetAsync("/api/v1/canonical/fixtures");
        }

        using (last)
        {
            Assert.Equal((HttpStatusCode)429, last!.StatusCode);
            Assert.Equal("application/problem+json", last.Content.Headers.ContentType?.MediaType);
            Assert.Equal("RATE_LIMITED", await ProblemCode(last));
        }
    }

    [Fact]
    public async Task EntraConfiguredAzureProfileChallengesAnonymousRequests()
    {
        using var factory = CreateEntraFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/canonical/fixtures");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("UNAUTHORIZED", await ProblemCode(response));
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task AzureProfileDoesNotMapSyntheticRoutesForSignedFanButCanonicalReadRemainsAuthorized()
    {
        using var factory = CreateSignedAzureFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", CreateSignedAzureToken("fan"));

        const string matchId = "11111111-1111-1111-1111-111111111111";
        var demoPaths = new[]
        {
            $"/api/v1/matches/{matchId}/state?atSeconds=0",
            $"/api/v1/matches/{matchId}/events?atSeconds=0",
            $"/api/v1/matches/{matchId}/insights?atSeconds=0",
            $"/api/v1/matches/{matchId}/key-moments?atSeconds=0",
            $"/api/v1/matches/{matchId}/evidence/smoke?atSeconds=0",
            $"/api/v1/matches/{matchId}/evidence/smoke/replay?atSeconds=0",
            $"/api/v1/matches/{matchId}/analyst",
            "/api/v1/matches"
        };
        foreach (var path in demoPaths)
        {
            using var request = path.EndsWith("/analyst", StringComparison.Ordinal)
                ? new HttpRequestMessage(HttpMethod.Post, path)
                {
                    Content = System.Net.Http.Json.JsonContent.Create(new { insightId = "smoke", atSeconds = 0, audience = "fan" })
                }
                : new HttpRequestMessage(HttpMethod.Get, path);
            using var demoResponse = await client.SendAsync(request);
            Assert.NotEqual(HttpStatusCode.OK, demoResponse.StatusCode);
            Assert.False(demoResponse.Headers.Contains("X-PitchMind-Data-Class"));
        }

        using var canonicalResponse = await client.GetAsync("/api/v1/canonical/fixtures");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, canonicalResponse.StatusCode);
        Assert.Equal("DEPENDENCY_UNAVAILABLE", await ProblemCode(canonicalResponse));
    }

    [Fact]
    public async Task MissingLocalIdentityUsesTypedProblemAndAuditsFailureStatus()
    {
        using var logs = new CapturingLoggerProvider();
        using var factory = CreateFactory(developmentHeaderAuth: false, loggerProvider: logs);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/canonical/fixtures");
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("IDENTITY_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
        Assert.True(body.RootElement.TryGetProperty("correlationId", out _));
        Assert.False(body.RootElement.TryGetProperty("CorrelationId", out _));
        Assert.Contains(logs.Messages, message => message.Contains("completed with 503", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ProductionCannotStartWithoutEntraAuthorityAndAudience()
    {
        using var factory = CreateFactory(development: false);

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health");
        });

        Assert.Contains("Azure runtime profile requires configured Entra authority and audience",
            exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AzureProfileRejectsNonEntraIdentityAuthority()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("Runtime__Profile", "azure");
        Environment.SetEnvironmentVariable("Identity__Authority", "https://issuer.example/tenant/v2.0");
        Environment.SetEnvironmentVariable("Identity__Audience", "api://pitchmind-api");
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", "false");
        using var factory = new WebApplicationFactory<Program>();

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health");
        });

        Assert.Contains("login.microsoftonline.com", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool development = true,
        bool developmentHeaderAuth = true, ILoggerProvider? loggerProvider = null) =>
        CreateConfiguredFactory(development, developmentHeaderAuth, loggerProvider);

    private static WebApplicationFactory<Program> CreateConfiguredFactory(bool development,
        bool developmentHeaderAuth = true, ILoggerProvider? loggerProvider = null)
    {
        var environment = development ? "Development" : "Production";
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environment);
        Environment.SetEnvironmentVariable("Runtime__Profile", development ? "local" : "azure");
        Environment.SetEnvironmentVariable("Identity__Authority", null);
        Environment.SetEnvironmentVariable("Identity__Audience", null);
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", developmentHeaderAuth ? "true" : "false");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", null);
        var factory = new WebApplicationFactory<Program>();
        return loggerProvider is null
            ? factory
            : factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(loggerProvider)));
    }

    private static WebApplicationFactory<Program> CreateEntraFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("Runtime__Profile", "azure");
        Environment.SetEnvironmentVariable("Identity__Authority", "https://login.microsoftonline.com/tenant-id/v2.0");
        Environment.SetEnvironmentVariable("Identity__Audience", "api://pitchmind-api");
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", "false");
        return new WebApplicationFactory<Program>();
    }

    private static WebApplicationFactory<Program> CreateCorsFactory(string allowedOrigin)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("Runtime__Profile", "local");
        Environment.SetEnvironmentVariable("Identity__Authority", null);
        Environment.SetEnvironmentVariable("Identity__Audience", null);
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", "true");
        Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", allowedOrigin);
        return new WebApplicationFactory<Program>();
    }

    private static Task<HttpResponseMessage> SendPreflight(HttpClient client, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/canonical/fixtures");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return client.SendAsync(request);
    }

    private static WebApplicationFactory<Program> CreateSignedAzureFactory()
    {
        const string issuer = "https://pitchmind-test-issuer.example";
        const string audience = "api://pitchmind-api";
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("Runtime__Profile", "azure");
        Environment.SetEnvironmentVariable("Identity__Authority", "https://login.microsoftonline.com/tenant-id/v2.0");
        Environment.SetEnvironmentVariable("Identity__Audience", audience);
        Environment.SetEnvironmentVariable("Identity__DevelopmentHeaderAuthEnabled", "false");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = $"{issuer}/.well-known/openid-configuration";
                    options.ConfigurationManager = null!;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = issuer,
                        ValidateAudience = true,
                        ValidAudience = audience,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = AzureTestSigningKey(),
                        RequireSignedTokens = true,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,
                        RoleClaimType = "roles"
                    };
                })));
    }

    private static string CreateSignedAzureToken(string role)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "https://pitchmind-test-issuer.example",
            audience: "api://pitchmind-api",
            claims: [new Claim("sub", "signed-test-user"), new Claim("roles", role)],
            notBefore: now.AddMinutes(-1),
            expires: now.AddMinutes(5),
            signingCredentials: new SigningCredentials(AzureTestSigningKey(), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static SymmetricSecurityKey AzureTestSigningKey() =>
        new(Encoding.UTF8.GetBytes("pitchmind-azure-test-signing-key-32-bytes-min"));

    private static async Task<HttpResponseMessage> SendAs(HttpClient client, string route, string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add("X-Dev-Subject", "qa-user");
        request.Headers.Add("X-Dev-Roles", role);
        return await client.SendAsync(request);
    }

    private static async Task<string?> ProblemCode(HttpResponseMessage response)
    {
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
        }
    }
}
