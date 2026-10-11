using System.Net;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace PitchMind.Api.Tests;

[CollectionDefinition("ApiEnvironment", DisableParallelization = true)]
public sealed class ApiEnvironmentCollection { }

[Collection("ApiEnvironment")]
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
    public async Task InvalidIdsTimesAndPageSizesAreRejectedBeforeCanonicalDatabaseAccess()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var oversizedInsightId = new string('i', 257);
        var invalidRequests = new (string Path, string Code)[]
        {
            ("/api/v1/canonical/matches/not-a-guid/summary?atSeconds=0", "INVALID_MATCH_ID"),
            ($"/api/v1/canonical/matches/{Guid.Empty:D}/summary?atSeconds=0", "INVALID_MATCH_ID"),
            ("/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/summary?atSeconds=-1", "INVALID_TIME"),
            ("/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/summary?atSeconds=5401", "INVALID_TIME"),
            ("/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/events?limit=0", "INVALID_LIMIT"),
            ("/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/events?limit=101", "INVALID_LIMIT"),
            ("/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/events?limit=2147483647", "INVALID_LIMIT"),
            ($"/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/insights/{oversizedInsightId}?atSeconds=0", "INVALID_INSIGHT_ID")
        };

        foreach (var (path, code) in invalidRequests)
        {
            using var response = await SendAs(client, path, "fan");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(code, await ProblemCode(response));
        }
    }

    [Fact]
    public async Task DevelopmentDemoIsExplicitlyClassifiedAsSynthetic()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v1/matches");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("synthetic-demo", Assert.Single(response.Headers.GetValues("X-PitchMind-Data-Class")));
    }

    [Fact]
    public async Task OversizedRequestBodyIsRejectedWithTyped413()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var content = new ByteArrayContent(new byte[4097]);

        using var response = await client.PostAsync("/api/v1/matches/11111111-1111-1111-1111-111111111111/analyst", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("PAYLOAD_TOO_LARGE", await ProblemCode(response));
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
    public async Task CanonicalReadRateLimitAlsoAppliesToAuthenticatedClient()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        for (var requestNumber = 1; requestNumber <= 121; requestNumber++)
        {
            using var response = await SendAs(client, "/api/v1/canonical/fixtures", "fan");

            if (requestNumber < 121)
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.Equal("DEPENDENCY_UNAVAILABLE", await ProblemCode(response));
                continue;
            }

            Assert.Equal((HttpStatusCode)429, response.StatusCode);
            Assert.Equal("RATE_LIMITED", await ProblemCode(response));
        }
    }

    [Fact]
    public async Task AuthenticatedCanonicalReadQuotaFollowsSubjectAcrossClientAddresses()
    {
        using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter>(new TestRemoteIpStartupFilter())));
        using var client = factory.CreateClient();

        for (var requestNumber = 1; requestNumber <= 120; requestNumber++)
        {
            using var response = await SendAs(client, "/api/v1/canonical/fixtures", "fan", "192.0.2.10");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        using var requestFromAnotherAddress = await SendAs(client,
            "/api/v1/canonical/fixtures", "fan", "198.51.100.20");

        Assert.Equal((HttpStatusCode)429, requestFromAnotherAddress.StatusCode);
        Assert.Equal("RATE_LIMITED", await ProblemCode(requestFromAnotherAddress));
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
            Assert.Equal(HttpStatusCode.NotFound, demoResponse.StatusCode);
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
    public async Task EntraJwtValidationRejectsInvalidClaimsSignaturesAndRolesWithoutRemoteMetadata()
    {
        const string issuer = "https://issuer.test.local/tenant/v2.0";
        const string audience = "api://pitchmind-test";
        using var signingKey = RSA.Create(2048);
        using var foreignKey = RSA.Create(2048);
        using var factory = CreateJwtFactory(issuer, audience, signingKey);
        using var client = factory.CreateClient();
        var replay = "/api/v1/canonical/matches/11111111-1111-1111-1111-111111111111/replay?atSeconds=0";

        using (var validFanRead = await SendBearer(client, "/api/v1/canonical/fixtures",
                   CreateToken(issuer, audience, signingKey, "fan")))
            Assert.Equal(HttpStatusCode.ServiceUnavailable, validFanRead.StatusCode);

        using (var wrongIssuer = await SendBearer(client, "/api/v1/canonical/fixtures",
                   CreateToken("https://attacker.test.local/", audience, signingKey, "fan")))
            Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);

        using (var wrongAudience = await SendBearer(client, "/api/v1/canonical/fixtures",
                   CreateToken(issuer, "api://another-api", signingKey, "fan")))
            Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);

        using (var expired = await SendBearer(client, "/api/v1/canonical/fixtures",
                   CreateToken(issuer, audience, signingKey, "fan", DateTime.UtcNow.AddMinutes(-10))))
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);

        using (var invalidSignature = await SendBearer(client, "/api/v1/canonical/fixtures",
                   CreateToken(issuer, audience, foreignKey, "fan")))
            Assert.Equal(HttpStatusCode.Unauthorized, invalidSignature.StatusCode);

        using (var missingRole = await SendBearer(client, "/api/v1/canonical/fixtures",
                   CreateToken(issuer, audience, signingKey)))
            Assert.Equal(HttpStatusCode.Forbidden, missingRole.StatusCode);

        using (var fanReplay = await SendBearer(client, replay,
                   CreateToken(issuer, audience, signingKey, "fan")))
            Assert.Equal(HttpStatusCode.Forbidden, fanReplay.StatusCode);

        using (var analystReplay = await SendBearer(client, replay,
                   CreateToken(issuer, audience, signingKey, "analyst")))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, analystReplay.StatusCode);
            Assert.Equal("DEPENDENCY_UNAVAILABLE", await ProblemCode(analystReplay));
        }
    }

    [Fact]
    public async Task AzureProfileRejectsPartiallyConfiguredAuthorityAndAudience()
    {
        using var factory = CreateAzureFactory(new Dictionary<string, string?>
        {
            ["Runtime:Profile"] = "azure",
            ["Identity:Authority"] = "https://issuer.test.local/tenant/v2.0",
            ["Identity:Audience"] = null,
            ["Identity:DevelopmentHeaderAuthEnabled"] = "false"
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using var client = factory.CreateClient();
            using var response = await client.GetAsync("/health");
        });

        Assert.Contains("must be configured together", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AzureProfileRejectsInsecureAuthorityAndDevelopmentHeaderAuthentication()
    {
        var invalidConfigurations = new (Dictionary<string, string?> Values, string ExpectedMessage)[]
        {
            (new Dictionary<string, string?>
            {
                ["Runtime:Profile"] = "azure",
                ["Identity:Authority"] = "http://issuer.test.local/tenant/v2.0",
                ["Identity:Audience"] = "api://pitchmind-test",
                ["Identity:DevelopmentHeaderAuthEnabled"] = "false"
            }, "explicit HTTPS Entra authority"),
            (new Dictionary<string, string?>
            {
                ["Runtime:Profile"] = "azure",
                ["Identity:Authority"] = "https://issuer.test.local/tenant/v2.0",
                ["Identity:Audience"] = "api://pitchmind-test",
                ["Identity:DevelopmentHeaderAuthEnabled"] = "true"
            }, "only in the local Development profile")
        };

        foreach (var (configuration, expectedMessage) in invalidConfigurations)
        {
            using var factory = CreateAzureFactory(configuration);
            var exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                using var client = factory.CreateClient();
                using var response = await client.GetAsync("/health");
            });

            Assert.Contains(expectedMessage, exception.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static WebApplicationFactory<Program> CreateJwtFactory(string issuer, string audience, RSA signingKey)
    {
        var authorityConfiguration = new OpenIdConnectConfiguration { Issuer = issuer };
        authorityConfiguration.SigningKeys.Add(new RsaSecurityKey(signingKey));
        var configurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(authorityConfiguration);
        return CreateAzureFactory(new Dictionary<string, string?>
        {
            ["Runtime:Profile"] = "azure",
            ["Identity:Authority"] = issuer,
            ["Identity:Audience"] = audience,
            ["Identity:DevelopmentHeaderAuthEnabled"] = "false"
        }).WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // Keep discovery local while preserving ApiSecurity's production token-validation parameters.
                options.Authority = null;
                options.MetadataAddress = string.Empty;
                options.ConfigurationManager = configurationManager;
            })));
    }

    private static WebApplicationFactory<Program> CreateAzureFactory(Dictionary<string, string?> configuration) =>
        CreateAzureFactoryCore(configuration);

    private static WebApplicationFactory<Program> CreateAzureFactoryCore(Dictionary<string, string?> configuration)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        foreach (var (key, value) in configuration)
            Environment.SetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal), value);
        return new WebApplicationFactory<Program>();
    }

    private static string CreateToken(string issuer, string audience, RSA signingKey, string? role = null,
        DateTime? expiresUtc = null)
    {
        var claims = new List<Claim> { new("sub", "local-security-test") };
        if (role is not null) claims.Add(new Claim("roles", role));
        var expiry = expiresUtc ?? DateTime.UtcNow.AddMinutes(5);
        var notBefore = expiry < DateTime.UtcNow ? expiry.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(-1);
        var token = new JwtSecurityToken(issuer, audience, claims, notBefore,
            expiry,
            new SigningCredentials(new RsaSecurityKey(signingKey), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<HttpResponseMessage> SendBearer(HttpClient client, string route, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
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

    private static WebApplicationFactory<Program> CreateSignedAzureFactory()
    {
        const string issuer = "https://pitchmind-test-issuer.example";
        const string audience = "api://pitchmind-api";
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("Runtime__Profile", "azure");
        Environment.SetEnvironmentVariable("Identity__Authority", issuer);
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

    private static async Task<HttpResponseMessage> SendAs(HttpClient client, string route, string role,
        string? testRemoteIp = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Add("X-Dev-Subject", "qa-user");
        request.Headers.Add("X-Dev-Roles", role);
        if (testRemoteIp is not null) request.Headers.Add("X-Test-Remote-IP", testRemoteIp);
        return await client.SendAsync(request);
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                var address = context.Request.Headers["X-Test-Remote-IP"].ToString();
                if (IPAddress.TryParse(address, out var parsed)) context.Connection.RemoteIpAddress = parsed;
                await nextMiddleware();
            });
            next(app);
        };
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
