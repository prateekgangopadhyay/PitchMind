using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
using PitchMind.Domain.Simulation;
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
            canonicalRevision = 4_294_967_300L,
            projectionRevision = 4,
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
        Assert.Equal(4_294_967_300L, cursor.CanonicalRevision);
        Assert.Equal(19, cursor.LastSequence);
        Assert.DoesNotContain("homeScore", json, StringComparison.Ordinal);
        Assert.DoesNotContain("events", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Subscription_state_deduplicates_inflight_and_active_subscriptions()
    {
        var state = new RealtimeSubscriptionState();
        var matchId = Guid.NewGuid();

        Assert.Equal(SubscriptionReservation.Reserved, state.TryBegin(matchId));
        Assert.Equal(SubscriptionReservation.AlreadyInProgress, state.TryBegin(matchId));

        state.Complete(matchId, subscribed: true);
        Assert.Equal(SubscriptionReservation.AlreadySubscribed, state.TryBegin(matchId));

        state.Remove(matchId);
        Assert.Equal(SubscriptionReservation.Reserved, state.TryBegin(matchId));
    }

    [Fact]
    public void Subscription_state_bounds_unique_lookup_attempts_and_active_groups()
    {
        var state = new RealtimeSubscriptionState();
        var matchIds = Enumerable.Range(0, RealtimeSubscriptionState.MaxUniqueAttemptsPerConnection)
            .Select(_ => Guid.NewGuid()).ToArray();

        for (var i = 0; i < RealtimeSubscriptionState.MaxActiveSubscriptions; i++)
        {
            Assert.Equal(SubscriptionReservation.Reserved, state.TryBegin(matchIds[i]));
            state.Complete(matchIds[i], subscribed: true);
        }

        Assert.Equal(SubscriptionReservation.LimitExceeded, state.TryBegin(Guid.NewGuid()));
        state.Remove(matchIds[0]);
        Assert.Equal(SubscriptionReservation.Reserved, state.TryBegin(matchIds[10]));
        state.Complete(matchIds[10], subscribed: false);

        for (var i = 11; i < RealtimeSubscriptionState.MaxUniqueAttemptsPerConnection; i++)
        {
            Assert.Equal(SubscriptionReservation.Reserved, state.TryBegin(matchIds[i]));
            state.Complete(matchIds[i], subscribed: false);
        }

        Assert.Equal(SubscriptionReservation.LimitExceeded, state.TryBegin(Guid.NewGuid()));
    }

    [Fact]
    public void Subscription_state_releases_failed_authorization_reservations_without_refunding_attempts()
    {
        var state = new RealtimeSubscriptionState();
        var matchId = Guid.NewGuid();

        Assert.Equal(SubscriptionReservation.Reserved, state.TryBegin(matchId));
        state.Complete(matchId, subscribed: false);
        Assert.Equal(SubscriptionReservation.Reserved, state.TryBegin(matchId));
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
            canonicalRevision = 4L,
            projectionRevision = 1,
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
            "duplicate" => payload.Replace("\"canonicalRevision\":4", "\"canonicalRevision\":4,\"canonicalRevision\":5", StringComparison.Ordinal),
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
        Assert.Equal(4_294_967_300L, root.GetProperty("cursor").GetProperty("canonicalRevision").GetInt64());
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

    [Fact]
    public async Task Local_browser_bearer_and_hub_query_tokens_use_the_dev_adapter_only_on_the_hub_path()
    {
        using var factory = CreateFactory(local: true, testAuthentication: false);
        var token = CreateLocalDevelopmentToken("browser-realtime-test", "fan");
        using var bearerClient = factory.CreateClient();
        bearerClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var negotiate = await bearerClient.PostAsJsonAsync("/api/v1/realtime/negotiate",
            new { matchId = MatchId, clientRevision = (long?)null });
        Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);

        using var hubClient = factory.CreateClient();
        hubClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var hubHandshake = await hubClient.PostAsync("/hubs/matches/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.OK, hubHandshake.StatusCode);

        using var queryClient = factory.CreateClient();
        using var websocketHandshake = await queryClient.GetAsync(
            $"/hubs/matches?access_token={Uri.EscapeDataString(token)}");
        Assert.NotEqual(HttpStatusCode.Unauthorized, websocketHandshake.StatusCode);

        using var queryOnlyApi = await queryClient.PostAsJsonAsync(
            $"/api/v1/realtime/negotiate?access_token={Uri.EscapeDataString(token)}",
            new { matchId = MatchId, clientRevision = (long?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, queryOnlyApi.StatusCode);
    }

    [Theory]
    [InlineData("/hubs/matches", "?access_token=local-token", "local-token")]
    [InlineData("/hubs/matches/negotiate", "?access_token=local-token", null)]
    [InlineData("/api/v1/realtime/negotiate", "?access_token=local-token", null)]
    [InlineData("/hubs/other", "?access_token=local-token", null)]
    [InlineData("/hubs/matches", "?access_token=one&access_token=two", null)]
    [InlineData("/hubs/matches", "?access_token=", null)]
    public void Jwt_query_token_extraction_is_limited_to_one_bounded_token_on_the_exact_hub_path(
        string path, string query, string? expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);

        Assert.Equal(expected, ApiSecurity.GetMatchHubQueryToken(context.Request));
    }

    [Fact]
    public async Task Negotiate_uses_the_shared_api_rate_limit()
    {
        using var factory = CreateFactory(local: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "fan");
        HttpResponseMessage? last = null;

        for (var requestNumber = 0; requestNumber < 121; requestNumber++)
        {
            last?.Dispose();
            last = await client.PostAsJsonAsync("/api/v1/realtime/negotiate", new { matchId = MatchId });
        }

        using var throttled = last ?? throw new InvalidOperationException("No negotiation response was received.");
        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
    }

    private static string CreateLocalDevelopmentToken(string subject, params string[] roles)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { sub = subject, roles });
        var encoded = Convert.ToBase64String(payload).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"pmdev.v1.{encoded}";
    }

    private static WebApplicationFactory<Program> CreateFactory(bool local, bool testAuthentication = true) => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(local ? "Development" : "Production");
            builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey,
                local ? "Development" : "Production");
            builder.UseSetting("Runtime:Profile", local ? "local" : "azure");
            builder.UseSetting("Realtime:Provider", local ? "local" : "azure");
            builder.UseSetting("Identity:Authority", local ? string.Empty : "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000001/v2.0");
            builder.UseSetting("Identity:Audience", local ? string.Empty : "api://pitchmind-test");
            builder.UseSetting("Identity:DevelopmentHeaderAuthEnabled", local ? "true" : "false");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Runtime:Profile"] = local ? "local" : "azure",
                    ["Realtime:Provider"] = local ? "local" : "azure",
                    ["Identity:Authority"] = local ? null : "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000001/v2.0",
                    ["Identity:Audience"] = local ? null : "api://pitchmind-test",
                    ["Identity:DevelopmentHeaderAuthEnabled"] = local ? "true" : "false"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMatchProjectionReader>();
                services.AddSingleton<IMatchProjectionReader, TestProjectionReader>();
                if (testAuthentication)
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
            4_294_967_300L, 3, 19, 3200, "rules-v2", new string('a', 64), new string('b', 64));

        public Task<SelectedMatchProjection?> GetForApiAtAsync(MatchId matchId, int selectedSeconds,
            long? maximumCanonicalRevision, int? maximumProjectionRevision, CancellationToken cancellationToken) =>
            Task.FromResult<SelectedMatchProjection?>(matchId.Value == RealtimeApiTests.MatchId ? Projection : null);

        public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MatchReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection projection,
            int selectedSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection projection,
            int selectedSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();

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
