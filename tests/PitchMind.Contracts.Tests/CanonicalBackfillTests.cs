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
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using PitchMind.Domain.Simulation;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CanonicalBackfillTests
{
    private static readonly Guid MatchGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly MatchId Match = new(MatchGuid);
    private static readonly MatchEvent[] Events =
    [
        MatchEvent.Create(Guid.Parse("00000000-0000-0000-0000-000000000001"), Match, 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch),
        MatchEvent.Create(Guid.Parse("00000000-0000-0000-0000-000000000002"), Match, 1, MatchEventType.Pass, 60, 1, DateTimeOffset.UnixEpoch.AddSeconds(60)),
        MatchEvent.Create(Guid.Parse("00000000-0000-0000-0000-000000000003"), Match, 2, MatchEventType.Shot, 120, 1, DateTimeOffset.UnixEpoch.AddSeconds(120))
    ];

    [Fact]
    public async Task Revision_change_refreshes_snapshot_and_replays_from_sequence_zero()
    {
        var reader = new ProjectionReader(revision: 5);
        using var factory = CreateFactory(reader);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/timeline/backfill?afterSequence=1&revision=4&atSeconds=120&limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("revisionChanged").GetBoolean());
        Assert.True(root.GetProperty("replayRequired").GetBoolean());
        Assert.Equal(1, root.GetProperty("requestedAfterSequence").GetInt64());
        Assert.Equal(100L, root.GetProperty("snapshot").GetProperty("canonicalRevision").GetInt64());
        Assert.Equal(5, root.GetProperty("snapshot").GetProperty("projectionRevision").GetInt32());
        Assert.Equal(0, root.GetProperty("events").GetProperty("items")[0].GetProperty("sequence").GetInt64());
        Assert.True(root.GetProperty("events").GetProperty("hasMore").GetBoolean());
        Assert.Equal(-1, reader.LastAfterSequence);
    }

    [Fact]
    public async Task Same_revision_backfill_continues_after_the_last_seen_sequence()
    {
        var reader = new ProjectionReader(revision: 5);
        using var factory = CreateFactory(reader);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/timeline/backfill?afterSequence=0&revision=5&atSeconds=120&limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(root.GetProperty("revisionChanged").GetBoolean());
        Assert.False(root.GetProperty("replayRequired").GetBoolean());
        Assert.Equal(1, root.GetProperty("events").GetProperty("items")[0].GetProperty("sequence").GetInt64());
        Assert.Equal(2, root.GetProperty("events").GetProperty("items")[1].GetProperty("sequence").GetInt64());
        Assert.False(root.GetProperty("events").GetProperty("hasMore").GetBoolean());
        Assert.Equal(0, reader.LastAfterSequence);
    }

    [Fact]
    public async Task Cursor_ahead_of_durable_head_requests_full_replay_instead_of_skipping()
    {
        var reader = new ProjectionReader(revision: 5);
        using var factory = CreateFactory(reader);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/timeline/backfill?afterSequence=7&revision=5&atSeconds=120&limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.True(root.GetProperty("replayRequired").GetBoolean());
        Assert.Equal(0, root.GetProperty("events").GetProperty("items")[0].GetProperty("sequence").GetInt64());
        Assert.Equal(-1, reader.LastAfterSequence);
    }

    [Fact]
    public async Task Resume_without_a_prior_revision_requests_full_replay()
    {
        var reader = new ProjectionReader(revision: 5);
        using var factory = CreateFactory(reader);
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/timeline/backfill?afterSequence=1&atSeconds=120&limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.False(root.GetProperty("revisionChanged").GetBoolean());
        Assert.True(root.GetProperty("replayRequired").GetBoolean());
        Assert.Equal(1, root.GetProperty("requestedAfterSequence").GetInt64());
        Assert.Equal(0, root.GetProperty("events").GetProperty("items")[0].GetProperty("sequence").GetInt64());
        Assert.Equal(-1, reader.LastAfterSequence);
    }

    [Fact]
    public async Task Backfill_requires_match_read_authorization()
    {
        using var factory = CreateFactory(new ProjectionReader(revision: 5));

        using var unauthenticated = factory.CreateClient();
        using var unauthorizedResponse = await unauthenticated.GetAsync(
            $"/api/v1/canonical/matches/{MatchGuid:D}/timeline/backfill?afterSequence=-1&atSeconds=120");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);

        using var forbidden = factory.CreateClient();
        forbidden.DefaultRequestHeaders.Add("X-Test-Role", "coach");
        using var forbiddenResponse = await forbidden.GetAsync(
            $"/api/v1/canonical/matches/{MatchGuid:D}/timeline/backfill?afterSequence=-1&atSeconds=120");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);
    }

    [Theory]
    [InlineData(-2, 120, 5, 50)]
    [InlineData(0, 5401, 5, 50)]
    [InlineData(0, 120, 0, 50)]
    [InlineData(0, 120, 5, 101)]
    public async Task Backfill_rejects_unbounded_or_invalid_cursor_inputs(long afterSequence, int atSeconds,
        int revision, int limit)
    {
        using var factory = CreateFactory(new ProjectionReader(revision: 5));
        using var client = CreateClient(factory);

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/timeline/backfill?afterSequence={afterSequence}&revision={revision}&atSeconds={atSeconds}&limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static WebApplicationFactory<Program> CreateFactory(ProjectionReader reader) =>
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
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMatchProjectionReader>();
                services.AddSingleton<IMatchProjectionReader>(reader);
            });
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "backfill-test";
                    options.DefaultChallengeScheme = "backfill-test";
                    options.DefaultForbidScheme = "backfill-test";
                })
                .AddScheme<AuthenticationSchemeOptions, BackfillTestAuthenticationHandler>("backfill-test", _ => { }));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Subject", "backfill-test");
        client.DefaultRequestHeaders.Add("X-Dev-Roles", "fan");
        return client;
    }

    private sealed class ProjectionReader(int revision) : IMatchProjectionReader
    {
        public long? LastAfterSequence { get; private set; }
        private readonly SelectedMatchProjection projection = new(
            new MatchState(MatchGuid.ToString("D"), 120, 1, MatchPhase.FirstHalf, 0, 0, null,
                11, 11, Array.Empty<MatchPlayerState>(), 2, "synthetic-test"),
            100, revision, 2, 120, "rules-v2", new string('a', 64), new string('b', 64));

        public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(MatchId? afterMatchId, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SelectedMatchProjection?> GetForApiAtAsync(MatchId matchId, int selectedSeconds,
            long? maximumCanonicalRevision, int? maximumProjectionRevision, CancellationToken cancellationToken) =>
            Task.FromResult<SelectedMatchProjection?>(matchId == Match && selectedSeconds >= 120 ? projection : null);

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

        public IAsyncEnumerable<SimulationEvent> ReadReplayEventsAtAsync(SelectedMatchProjection selected,
            int selectedSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<MatchReplayFrame> ReadReplayFramesAtAsync(SelectedMatchProjection selected,
            int selectedSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(SelectedMatchProjection selected, int selectedSeconds,
            long afterSequence, int limit, CancellationToken cancellationToken)
        {
            LastAfterSequence = afterSequence;
            var items = Events.Where(e => e.Sequence > afterSequence && e.Sequence <= selected.AsOfSequence && e.ClockSeconds <= selectedSeconds)
                .Take(limit + 1).ToArray();
            var hasMore = items.Length > limit;
            if (hasMore) items = items[..limit];
            return Task.FromResult(new PersistedMatchEventPage(items, hasMore ? items[^1].Sequence : null, hasMore));
        }

        public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(SelectedMatchProjection selected, int selectedSeconds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class BackfillTestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Test-Role"].FirstOrDefault() ??
                Request.Headers["X-Dev-Roles"].ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (role is null) return Task.FromResult(AuthenticateResult.NoResult());

            var subject = Request.Headers["X-Dev-Subject"].FirstOrDefault() ?? "backfill-contract-test";
            var identity = new ClaimsIdentity([new Claim("sub", subject), new Claim("roles", role)], Scheme.Name, "sub", "roles");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
