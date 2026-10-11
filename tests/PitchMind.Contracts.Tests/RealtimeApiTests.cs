using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PitchMind.Api;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class RealtimeApiTests
{
    private static readonly Guid MatchId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Projection_outbox_is_reduced_to_a_cursor_without_match_state()
    {
        var messageId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            messageId,
            matchId = MatchId,
            revision = 4,
            sequence = 19,
            clockSeconds = 3200,
            algorithmVersion = "rules-v2",
            sourceSha256 = new string('a', 64),
            stateSha256 = new string('b', 64),
            state = new { homeScore = 4, events = new[] { "private" } }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var cursor = RealtimeCursorTranslator.FromProjectionPublishedV1(messageId, payload,
            DateTimeOffset.Parse("2026-10-11T12:00:00Z"));
        var json = JsonSerializer.Serialize(cursor, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(MatchId.ToString("D"), cursor.MatchId);
        Assert.Equal(4, cursor.CanonicalRevision);
        Assert.Equal(19, cursor.LastSequence);
        Assert.DoesNotContain("homeScore", json, StringComparison.Ordinal);
        Assert.DoesNotContain("events", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("bad-hash")]
    public void Projection_outbox_rejects_unrecognized_or_malformed_payloads(string mutation)
    {
        var messageId = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new
        {
            messageId,
            matchId = MatchId,
            revision = 4,
            sequence = 19,
            clockSeconds = 3200,
            algorithmVersion = "rules-v2",
            sourceSha256 = new string('a', 64),
            stateSha256 = new string('b', 64),
            state = new { }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload = mutation switch
        {
            "extra" => payload.Replace("\"state\":{}", "\"state\":{},\"event\":{\"score\":1}", StringComparison.Ordinal),
            "duplicate" => payload.Replace("\"revision\":4", "\"revision\":4,\"revision\":5", StringComparison.Ordinal),
            "bad-hash" => payload.Replace(new string('a', 64), "bad", StringComparison.Ordinal),
            _ => payload
        };

        Assert.Throws<InvalidDataException>(() => RealtimeCursorTranslator.FromProjectionPublishedV1(
            messageId, payload, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Negotiate_requires_authentication_and_a_match_read_role_then_returns_only_cursor_details()
    {
        using var factory = CreateFactory(local: true);
        Assert.Equal("Development", factory.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName);
        Assert.Equal("local", factory.Services.GetRequiredService<IConfiguration>()["Runtime:Profile"]);
        Assert.Equal("local", factory.Services.GetRequiredService<IConfiguration>()["Realtime:Provider"]);
        Assert.IsType<LocalRealtimeConnectionNegotiator>(factory.Services.GetRequiredService<IRealtimeConnectionNegotiator>());
        using var anonymous = factory.CreateClient();
        using var unauthorized = factory.CreateClient();
        using var authorized = factory.CreateClient();
        unauthorized.DefaultRequestHeaders.Add("X-Test-Role", "coach");
        authorized.DefaultRequestHeaders.Add("X-Test-Role", "fan");

        using var anonymousResponse = await anonymous.PostAsJsonAsync("/api/v1/realtime/negotiate",
            new { matchId = MatchId, clientRevision = 2 });
        using var unauthorizedResponse = await unauthorized.PostAsJsonAsync("/api/v1/realtime/negotiate",
            new { matchId = MatchId, clientRevision = 2 });
        using var authorizedResponse = await authorized.PostAsJsonAsync("/api/v1/realtime/negotiate",
            new { matchId = MatchId, clientRevision = 2 });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
        using var anonymousHubHandshake = await anonymous.PostAsync("/hubs/matches/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousHubHandshake.StatusCode);
        using var body = JsonDocument.Parse(await authorizedResponse.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal("aspnetcore-signalr", root.GetProperty("transport").GetString());
        Assert.Equal("/hubs/matches", root.GetProperty("url").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("accessToken").ValueKind);
        Assert.True(root.GetProperty("revisionChanged").GetBoolean());
        Assert.Equal(4, root.GetProperty("cursor").GetProperty("canonicalRevision").GetInt32());
        Assert.Equal(19, root.GetProperty("cursor").GetProperty("lastSequence").GetInt64());
        using var missingMatchResponse = await authorized.PostAsJsonAsync("/api/v1/realtime/negotiate",
            new { matchId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), clientRevision = 2 });
        Assert.Equal(HttpStatusCode.NotFound, missingMatchResponse.StatusCode);
    }

    [Fact]
    public async Task Non_local_profile_fails_closed_when_azure_signalr_provider_is_not_registered()
    {
        using var factory = CreateFactory(local: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "analyst");

        using var response = await client.PostAsJsonAsync("/api/v1/realtime/negotiate",
            new { matchId = MatchId, clientRevision = (int?)null });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("SIGNALR_UNAVAILABLE", body.RootElement.GetProperty("code").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(bool local) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(local ? "Development" : "Production");
            builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey,
                local ? "Development" : "Production");
            builder.UseSetting("Runtime:Profile", local ? "local" : "azure");
            builder.UseSetting("Realtime:Provider", local ? "local" : "azure");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Runtime:Profile"] = local ? "local" : "azure",
                    ["Realtime:Provider"] = local ? "local" : "azure"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMatchProjectionReader>();
                services.AddSingleton<IMatchProjectionReader, TestProjectionReader>();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "test";
                        options.DefaultChallengeScheme = "test";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
            });
        });

    private sealed class TestProjectionReader : IMatchProjectionReader
    {
        private static readonly SelectedMatchProjection Projection = new(
            new MatchState(MatchId.ToString("D"), 3200, 2, MatchPhase.SecondHalf, 0, 0, null,
                11, 11, Array.Empty<MatchPlayerState>(), 19, "synthetic-test"),
            4, 19, 3200, "rules-v2", new string('a', 64), new string('b', 64));

        public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds, int? maximumRevision,
            CancellationToken cancellationToken) => Task.FromResult<SelectedMatchProjection?>(matchId.Value == RealtimeApiTests.MatchId ? Projection : null);

        public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection projection, int selectedSeconds,
            long afterSequence, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection projection, int selectedSeconds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user"),
                new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
