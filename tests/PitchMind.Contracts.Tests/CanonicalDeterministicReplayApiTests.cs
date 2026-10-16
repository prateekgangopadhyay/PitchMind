using System.Net;
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
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Analytics;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using PitchMind.TestFixtures;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CanonicalDeterministicReplayApiTests
{
    private static readonly int SelectedSeconds = 3308;

    [Fact]
    public async Task Deterministic_replay_returns_revision_pinned_state_digest_and_no_future_event()
    {
        var data = ReplayData.Create(SelectedSeconds);
        using var factory = CreateFactory(data.Reader);
        using var client = CreateClient(factory, "analyst");

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{data.MatchId:D}/replay/deterministic?atSeconds={SelectedSeconds}&canonicalRevision={data.Projection.CanonicalRevision}&limit=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(data.Projection.CanonicalRevision, root.GetProperty("snapshot").GetProperty("canonicalRevision").GetInt64());
        Assert.Equal(SelectedSeconds, root.GetProperty("snapshot").GetProperty("selectedTimeSeconds").GetInt32());
        Assert.Equal(data.Projection.StateSha256, root.GetProperty("replayedStateSha256").GetString());
        Assert.Equal(64, root.GetProperty("replayInputSha256").GetString()?.Length);
        var firstPage = root.GetProperty("events");
        Assert.Equal(100, firstPage.GetProperty("items").GetArrayLength());
        Assert.True(firstPage.GetProperty("hasMore").GetBoolean());
        Assert.True(firstPage.GetProperty("nextCursor").GetString()?.Length > 0);
        Assert.DoesNotContain(data.Fixture.Events.Where(item => item.Event.ClockSeconds > SelectedSeconds),
            future => firstPage.GetProperty("items").EnumerateArray().Any(item =>
                item.GetProperty("eventId").GetGuid() == future.Event.EventId));
        Assert.False(root.TryGetProperty("insights", out _));
        Assert.False(root.TryGetProperty("derivedInsights", out _));

        var cursor = Uri.EscapeDataString(firstPage.GetProperty("nextCursor").GetString()!);
        using var secondResponse = await client.GetAsync($"/api/v1/canonical/matches/{data.MatchId:D}/replay/deterministic?atSeconds={SelectedSeconds}&canonicalRevision={data.Projection.CanonicalRevision}&cursor={cursor}&limit=100");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        using var secondJson = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync());
        var secondPage = secondJson.RootElement.GetProperty("events");
        Assert.Equal(data.Events.Count - 100, secondPage.GetProperty("items").GetArrayLength());
        Assert.False(secondPage.GetProperty("hasMore").GetBoolean());
    }

    [Theory]
    [InlineData("fan", HttpStatusCode.Forbidden)]
    [InlineData("analyst", HttpStatusCode.BadRequest)]
    public async Task Deterministic_replay_enforces_role_and_selection_bounds(string role, HttpStatusCode expected)
    {
        var data = ReplayData.Create(SelectedSeconds);
        using var factory = CreateFactory(data.Reader);
        using var client = CreateClient(factory, role);
        var suffix = role == "fan" ? $"atSeconds={SelectedSeconds}" : "atSeconds=5401";

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{data.MatchId:D}/replay/deterministic?{suffix}");

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("INVALID_TIME", json.RootElement.GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Deterministic_replay_rejects_a_future_event_from_the_read_adapter()
    {
        var data = ReplayData.Create(SelectedSeconds, includeFutureEvent: true);
        using var factory = CreateFactory(data.Reader);
        using var client = CreateClient(factory, "analyst");

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{data.MatchId:D}/replay/deterministic?atSeconds={SelectedSeconds}");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CANONICAL_DATA_INVALID", json.RootElement.GetProperty("code").GetString());
    }

    private static WebApplicationFactory<Program> CreateFactory(ReplayProjectionReader reader) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Runtime:Profile", "local");
            builder.UseSetting("Identity:DevelopmentHeaderAuthEnabled", "true");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Runtime:Profile"] = "local",
                    ["Identity:DevelopmentHeaderAuthEnabled"] = "true"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMatchProjectionReader>();
                services.AddSingleton<IMatchProjectionReader>(reader);
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "deterministic-replay-test";
                        options.DefaultChallengeScheme = "deterministic-replay-test";
                        options.DefaultForbidScheme = "deterministic-replay-test";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("deterministic-replay-test", _ => { });
            });
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault();
            if (role is null) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim("sub", "replay-api-test"), new Claim("roles", role)], Scheme.Name, "sub", "roles");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }

    private sealed record ReplayData(SyntheticFixture Fixture, Guid MatchId, SelectedMatchProjection Projection,
        MatchReplaySeed Seed, IReadOnlyList<MatchEvent> Events, IReadOnlyList<SimulationEvent> ReplayEvents,
        ReplayProjectionReader Reader)
    {
        public static ReplayData Create(int selectedSeconds, bool includeFutureEvent = false)
        {
            var fixture = SyntheticFixtureGenerator.Generate();
            var matchId = new MatchId(Guid.Parse(fixture.MatchId));
            var players = fixture.Home.Players.Select(player => (player.Id, fixture.Home.Id))
                .Concat(fixture.Away.Players.Select(player => (player.Id, fixture.Away.Id))).ToArray();
            var aggregate = new MatchAggregate(matchId, fixture.Home.Id, fixture.Away.Id, players,
                players.Select(player => player.Item1), fixture.Provenance);
            var selectedReplayEvents = fixture.Events.Where(item => item.Event.ClockSeconds <= selectedSeconds).ToList();
            var replayEvents = selectedReplayEvents.ToList();
            if (includeFutureEvent)
                replayEvents.Add(fixture.Events.First(item => item.Event.ClockSeconds > selectedSeconds));
            foreach (var item in selectedReplayEvents) aggregate.Apply(item);

            var state = aggregate.Snapshot();
            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var stateHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
            var sourceHash = new string('a', 64);
            var projection = new SelectedMatchProjection(state, 7, 4, state.LastSequence,
                state.ClockSeconds, "aggregate-v1", sourceHash, stateHash);
            var canonicalEvents = selectedReplayEvents.Select(item => item.Event).ToArray();
            return new(fixture, matchId.Value, projection, aggregate.ReplaySeed,
                Array.AsReadOnly(canonicalEvents), Array.AsReadOnly(replayEvents.ToArray()),
                new ReplayProjectionReader(projection, aggregate.ReplaySeed, canonicalEvents, replayEvents.ToArray()));
        }
    }

    private sealed class ReplayProjectionReader(SelectedMatchProjection projection, MatchReplaySeed seed,
        IReadOnlyList<MatchEvent> events, IReadOnlyList<SimulationEvent> replayEvents) : IMatchProjectionReader
    {
        public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SelectedMatchProjection?> GetForApiAtAsync(MatchId matchId, int selectedSeconds,
            long? maximumCanonicalRevision, int? maximumProjectionRevision, CancellationToken cancellationToken) =>
            Task.FromResult<SelectedMatchProjection?>(matchId.Value == Guid.Parse(projection.State.MatchId) &&
                selectedSeconds >= projection.AsOfClockSeconds &&
                (maximumCanonicalRevision is null || projection.CanonicalRevision <= maximumCanonicalRevision) &&
                (maximumProjectionRevision is null || projection.ProjectionRevision <= maximumProjectionRevision)
                ? projection : null);

        public Task<SelectedMatchProjection?> GetAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MatchReplaySnapshot?> ReadReplaySnapshotAtAsync(MatchId matchId, int selectedSeconds,
            long upToCanonicalRevision, string algorithmVersion, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MatchReplaySeed?> ReadReplaySeedAsync(MatchId matchId, CancellationToken cancellationToken) =>
            Task.FromResult<MatchReplaySeed?>(matchId.Value == Guid.Parse(projection.State.MatchId) ? seed : null);

        public Task<MatchReplayContext?> ReadReplayContextAsync(MatchId matchId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection selected,
            int selectedSeconds, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in replayEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
                await Task.Yield();
            }
        }

        public IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection selected, int selectedSeconds,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection selected,
            int selectedSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection selected,
            int selectedSeconds, long afterSequence, int limit, CancellationToken cancellationToken)
        {
            var selectedEvents = events.Where(item => item.Sequence > afterSequence && item.ClockSeconds <= selectedSeconds)
                .Take(limit + 1).ToArray();
            var hasMore = selectedEvents.Length > limit;
            if (hasMore) selectedEvents = selectedEvents[..limit];
            return Task.FromResult(new PersistedMatchEventPage(Array.AsReadOnly(selectedEvents),
                hasMore ? selectedEvents[^1].Sequence : null, hasMore));
        }
    }
}
