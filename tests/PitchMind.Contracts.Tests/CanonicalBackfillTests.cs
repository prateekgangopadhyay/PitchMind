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
using PitchMind.Contracts.Telemetry.V1;
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
    public async Task Tracking_frame_cursor_pages_by_sequence_and_frame_id_with_both_revisions_bound()
    {
        var tracking = new TrackingReader();
        using var factory = CreateFactory(new ProjectionReader(revision: 5), tracking);
        using var client = CreateClient(factory, "analyst");

        using var first = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/frames?atSeconds=120&revision=5&limit=1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstRoot = firstJson.RootElement;
        Assert.Equal(100, firstRoot.GetProperty("canonicalRevision").GetInt64());
        Assert.Equal(5, firstRoot.GetProperty("projectionRevision").GetInt32());
        Assert.Equal("frame-a", firstRoot.GetProperty("items")[0].GetProperty("frameId").GetString());
        var cursor = firstRoot.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        using var second = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/frames?atSeconds=120&revision=5&cursor={Uri.EscapeDataString(cursor!)}&limit=1");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("frame-b", secondJson.RootElement.GetProperty("items")[0].GetProperty("frameId").GetString());
        Assert.True(secondJson.RootElement.GetProperty("hasMore").GetBoolean());
        Assert.Equal("frame-a", tracking.LastAfterFrameId);

        var nextCursor = secondJson.RootElement.GetProperty("nextCursor").GetString();
        using var third = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/frames?atSeconds=120&revision=5&cursor={Uri.EscapeDataString(nextCursor!)}&limit=1");
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        using var thirdJson = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        Assert.Equal("frame-c", thirdJson.RootElement.GetProperty("items")[0].GetProperty("frameId").GetString());
        Assert.False(thirdJson.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Tracking_frame_lookup_returns_exact_frame_id_without_pagination()
    {
        var tracking = new TrackingReader();
        using var factory = CreateFactory(new ProjectionReader(revision: 5), tracking);
        using var client = CreateClient(factory, "analyst");

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/frames?atSeconds=120&revision=5&frameId=frame-b");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal("frame-b", root.GetProperty("items")[0].GetProperty("frameId").GetString());
        Assert.Null(root.GetProperty("nextCursor").GetString());
        Assert.False(root.GetProperty("hasMore").GetBoolean());
        Assert.Equal("frame-b", tracking.LastRequestedFrameId);
    }

    [Fact]
    public async Task Tracking_frame_lookup_rejects_cursor_combination()
    {
        using var factory = CreateFactory(new ProjectionReader(revision: 5));
        using var client = CreateClient(factory, "analyst");

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/frames?atSeconds=120&revision=5&frameId=frame-b&cursor=opaque");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_CURSOR", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Tracking_frames_require_analyst_or_admin_role()
    {
        using var factory = CreateFactory(new ProjectionReader(revision: 5));
        using var fan = CreateClient(factory, "fan");
        using var response = await fan.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/frames?atSeconds=120");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_persisted_frame_returns_a_typed_integrity_failure()
    {
        using var factory = CreateFactory(new ProjectionReader(revision: 5), new TrackingReader(malformedFramePayload: true));
        using var client = CreateClient(factory, "analyst");

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/frames?atSeconds=120&revision=5");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("CANONICAL_DATA_INVALID", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Semantically_invalid_frame_coordinates_are_not_returned_as_canonical_facts()
    {
        using var factory = CreateFactory(new ProjectionReader(revision: 5), new TrackingReader(invalidCoordinates: true));
        using var client = CreateClient(factory, "analyst");

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchGuid:D}/frames?atSeconds=120&revision=5");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CANONICAL_DATA_INVALID", body.RootElement.GetProperty("code").GetString());
    }

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

    private static WebApplicationFactory<Program> CreateFactory(ProjectionReader reader, IMatchTrackingReader? trackingReader = null) =>
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
                services.RemoveAll<IMatchTrackingReader>();
                services.AddSingleton<IMatchTrackingReader>(trackingReader ?? new TrackingReader());
            });
            builder.ConfigureTestServices(services => services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = "backfill-test";
                    options.DefaultChallengeScheme = "backfill-test";
                    options.DefaultForbidScheme = "backfill-test";
                })
                .AddScheme<AuthenticationSchemeOptions, BackfillTestAuthenticationHandler>("backfill-test", _ => { }));
        });

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string role = "fan")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Subject", "backfill-test");
        client.DefaultRequestHeaders.Add("X-Dev-Roles", role);
        return client;
    }

    private sealed class TrackingReader : IMatchTrackingReader
    {
        private const string SourceHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private readonly PersistedTrackingFrame[] frames;
        public string? LastAfterFrameId { get; private set; }
        public string? LastRequestedFrameId { get; private set; }

        public TrackingReader(bool malformedFramePayload = false, bool invalidCoordinates = false)
        {
            frames = [Frame("frame-a", 0, invalidCoordinates), Frame("frame-b", 0), Frame("frame-c", 1)];
            if (malformedFramePayload) frames[0] = frames[0] with { PayloadJson = "{" };
        }

        public Task<PersistedTrackingFramePage> ReadAtAsync(SelectedMatchProjection projection, int selectedSeconds,
            long? afterSequence, string? afterFrameId, int limit, CancellationToken cancellationToken)
        {
            LastAfterFrameId = afterFrameId;
            var page = frames.Where(frame => afterSequence is null || frame.Sequence > afterSequence ||
                    frame.Sequence == afterSequence && string.CompareOrdinal(frame.FrameId, afterFrameId) > 0)
                .Take(limit + 1).ToArray();
            var hasMore = page.Length > limit;
            if (hasMore) page = page[..limit];
            var last = hasMore ? page[^1] : null;
            return Task.FromResult(new PersistedTrackingFramePage(Array.AsReadOnly(page), last?.Sequence, last?.FrameId, hasMore));
        }

        public Task<PersistedTrackingFrame?> ReadByIdAtAsync(SelectedMatchProjection projection, int selectedSeconds,
            string frameId, CancellationToken cancellationToken)
        {
            LastRequestedFrameId = frameId;
            return Task.FromResult(frames.LastOrDefault(frame => frame.FrameId == frameId));
        }

        private static PersistedTrackingFrame Frame(string id, long sequence, bool invalidCoordinates = false)
        {
            var frame = new TrackingFrameV1
            {
                SchemaVersion = 1,
                MatchId = MatchGuid.ToString("D"),
                CanonicalRevision = 100,
                FrameId = id,
                Sequence = sequence,
                MatchTime = new MatchTimeV1
                {
                    Period = 1, ElapsedSeconds = 120, SourceClockValue = 120,
                    SourceClockUnit = "seconds", SourceClockBasis = "match_elapsed"
                },
                CoordinateSystem = new PitchCoordinateSystemV1
                {
                    LengthMetres = 105, WidthMetres = 68, Unit = "metres", AxisOrigin = "bottom_left",
                    TeamDirections = [new TeamAttackingDirectionV1 { TeamId = "team-a", Direction = "right" }]
                },
                Ball = new PitchPointV1 { X = invalidCoordinates ? 106m : 52.5m, Y = 34m },
                Players = [new PlayerPositionV1
                {
                    PlayerId = "player-a", TeamId = "team-a", Position = new PitchPointV1 { X = 50m, Y = 34m }
                }],
                Source = new SourceReferenceV1
                {
                    ProviderId = "synthetic-test", SourceMatchId = MatchGuid.ToString("D"), SourceRecordId = id,
                    SourceSequence = sequence, SourceSchemaVersion = "v1", SourceSha256 = SourceHash,
                    ProvenanceStatus = "synthetic", RightsReviewStatus = "reviewed"
                }
            };
            return new PersistedTrackingFrame(id, sequence, 100, 1, 120, "pitch-v1", "{}", SourceHash,
                JsonSerializer.Serialize(frame, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
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
