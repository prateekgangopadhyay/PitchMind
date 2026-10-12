using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PitchMind.Api;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class AuthorizationAbuseControlTests
{
    private const string MatchId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";

    [Fact]
    public async Task Canonical_routes_require_authentication_and_matching_role_and_entitlement()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => ApiTestAuthentication.Configure(builder));
        using var anonymous = factory.CreateClient();
        using var anonymousResponse = await anonymous.GetAsync($"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var noEntitlement = AuthorizedClient(factory, "analyst", null);
        using var denied = await noEntitlement.GetAsync($"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var wrongRole = AuthorizedClient(factory, "unconfigured-role", MatchId);
        using var roleDenied = await wrongRole.GetAsync($"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120");
        Assert.Equal(HttpStatusCode.Forbidden, roleDenied.StatusCode);

        using var entitled = AuthorizedClient(factory, "analyst", MatchId);
        using var allowed = await entitled.GetAsync($"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120");
        Assert.True(allowed.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"Expected auth to pass to unavailable canonical adapter; got {(int)allowed.StatusCode} {await allowed.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Unmapped_request_fields_are_rejected_before_realtime_negotiation()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApiTestAuthentication.Configure(builder);
            builder.UseEnvironment("Development");
            builder.UseSetting("Runtime:Profile", "local");
            builder.UseSetting("Realtime:Provider", "local");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Profile"] = "local",
                ["Realtime:Provider"] = "local"
            }));
        });
        using var client = AuthorizedClient(factory, "fan", MatchId);

        using var response = await client.PostAsJsonAsync("/api/v1/realtime/negotiate",
            new { matchId = MatchId, unexpected = "reject-me" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignalR_hub_accepts_browser_access_token_only_on_hub_path()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApiTestAuthentication.Configure(builder);
            builder.UseEnvironment("Development");
            builder.UseSetting("Runtime:Profile", "local");
            builder.UseSetting("Realtime:Provider", "local");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Profile"] = "local",
                ["Realtime:Provider"] = "local"
            }));
        });

        var jwtOptions = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var bearerScheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme,
            JwtBearerDefaults.AuthenticationScheme, typeof(JwtBearerHandler));
        var hubRequest = new DefaultHttpContext();
        hubRequest.Request.Path = "/hubs/matches";
        hubRequest.Request.QueryString = new QueryString("?access_token=browser-test-token");
        var hubMessage = new MessageReceivedContext(hubRequest, bearerScheme, jwtOptions);
        await jwtOptions.Events.OnMessageReceived(hubMessage);
        Assert.Equal("browser-test-token", hubMessage.Token);

        var apiRequest = new DefaultHttpContext();
        apiRequest.Request.Path = $"/api/v1/canonical/matches/{MatchId}/summary";
        apiRequest.Request.QueryString = new QueryString("?access_token=browser-test-token");
        var apiMessage = new MessageReceivedContext(apiRequest, bearerScheme, jwtOptions);
        await jwtOptions.Events.OnMessageReceived(apiMessage);
        Assert.Null(apiMessage.Token);

        using var browserClient = factory.CreateClient();
        using var hubResponse = await browserClient.PostAsync(
            "/hubs/matches/negotiate?negotiateVersion=1&access_token=browser-test-token", null);
        Assert.Equal(HttpStatusCode.OK, hubResponse.StatusCode);
        using var negotiateBody = JsonDocument.Parse(await hubResponse.Content.ReadAsStringAsync());
        Assert.True(negotiateBody.RootElement.TryGetProperty("connectionToken", out _));

        using var apiResponse = await browserClient.GetAsync(
            $"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120&access_token=browser-test-token");
        Assert.Equal(HttpStatusCode.Unauthorized, apiResponse.StatusCode);
    }

    [Fact]
    public async Task Canonical_read_rate_limit_returns_sanitized_429()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => ApiTestAuthentication.Configure(builder, canonicalReadLimit: 1));
        using var client = AuthorizedClient(factory, "analyst", MatchId);
        var path = $"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120";

        using var first = await client.GetAsync(path);
        using var second = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var problem = await second.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("RATE_LIMITED", problem?.Code);
    }

    [Fact]
    public async Task Authenticated_rate_limit_isolated_by_subject_and_follows_subject_across_addresses()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApiTestAuthentication.Configure(builder, canonicalReadLimit: 1);
            builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new TestRemoteIpStartupFilter()));
        });

        using var firstSubjectFirstAddress = AuthorizedClient(factory, "analyst", MatchId, "viewer-a", "192.0.2.10");
        using var secondSubjectSameAddress = AuthorizedClient(factory, "analyst", MatchId, "viewer-b", "192.0.2.10");
        using var firstSubjectSecondAddress = AuthorizedClient(factory, "analyst", MatchId, "viewer-a", "198.51.100.20");
        var path = $"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120";

        using var first = await firstSubjectFirstAddress.GetAsync(path);
        using var secondSubject = await secondSubjectSameAddress.GetAsync(path);
        using var secondAddress = await firstSubjectSecondAddress.GetAsync(path);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, secondSubject.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondAddress.StatusCode);
        Assert.Equal("application/problem+json", secondAddress.Content.Headers.ContentType?.MediaType);
        Assert.Equal("RATE_LIMITED", (await secondAddress.Content.ReadFromJsonAsync<ProblemBody>())?.Code);
    }

    [Fact]
    public async Task Realtime_negotiation_has_an_independent_rate_limit()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApiTestAuthentication.Configure(builder, realtimeLimit: 1);
            builder.UseEnvironment("Development");
            builder.UseSetting("Runtime:Profile", "local");
            builder.UseSetting("Realtime:Provider", "local");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Profile"] = "local",
                ["Realtime:Provider"] = "local"
            }));
        });
        using var client = AuthorizedClient(factory, "fan", MatchId);
        var request = new { matchId = MatchId, clientRevision = (int?)null };

        using var first = await client.PostAsJsonAsync("/api/v1/realtime/negotiate", request);
        using var second = await client.PostAsJsonAsync("/api/v1/realtime/negotiate", request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Direct_signalr_hub_negotiation_is_rate_limited_after_configured_limit()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApiTestAuthentication.Configure(builder, realtimeLimit: 1);
            builder.UseEnvironment("Development");
            builder.UseSetting("Runtime:Profile", "local");
            builder.UseSetting("Realtime:Provider", "local");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Profile"] = "local",
                ["Realtime:Provider"] = "local"
            }));
        });
        using var client = factory.CreateClient();
        const string negotiatePath = "/hubs/matches/negotiate?negotiateVersion=1&access_token=browser-test-token";

        using var first = await client.PostAsync(negotiatePath, null);
        using var second = await client.PostAsync(negotiatePath, null);
        using var third = await client.PostAsync(negotiatePath, null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var problem = await second.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal("RATE_LIMITED", problem?.Code);
    }

    [Fact]
    public async Task Repeated_hub_subscriptions_stop_before_rechecking_entitlement_after_subscription_cap()
    {
        var access = new CountingRealtimeAccessPolicy();
        var hub = new MatchUpdatesHub(access, new RealtimeConnectionGate(1), new RealtimeHubMethodLimiter(10),
            new RealtimeHubSubscriptionTracker(2));
        hub.Context = new TestHubCallerContext();
        hub.Groups = new RecordingGroupManager();

        await hub.SubscribeToMatch(Guid.Parse(MatchId));
        await hub.SubscribeToMatch(Guid.Parse("22222222-2222-4222-8222-222222222222"));
        var rejected = await Assert.ThrowsAsync<HubException>(() =>
            hub.SubscribeToMatch(Guid.Parse("33333333-3333-4333-8333-333333333333")));

        Assert.Equal("Realtime match subscription limit exceeded.", rejected.Message);
        Assert.Equal(2, access.CheckCount);
        Assert.Equal(2, ((RecordingGroupManager)hub.Groups).AddCount);
    }

    [Fact]
    public async Task Repeating_a_match_is_idempotent_for_the_distinct_subscription_cap()
    {
        var access = new CountingRealtimeAccessPolicy();
        var hub = new MatchUpdatesHub(access, new RealtimeConnectionGate(1), new RealtimeHubMethodLimiter(10),
            new RealtimeHubSubscriptionTracker(2));
        hub.Context = new TestHubCallerContext();
        hub.Groups = new RecordingGroupManager();
        var first = Guid.Parse(MatchId);

        await hub.SubscribeToMatch(first);
        await hub.SubscribeToMatch(first);
        await hub.SubscribeToMatch(Guid.Parse("22222222-2222-4222-8222-222222222222"));
        var rejected = await Assert.ThrowsAsync<HubException>(() =>
            hub.SubscribeToMatch(Guid.Parse("33333333-3333-4333-8333-333333333333")));

        Assert.Equal("Realtime match subscription limit exceeded.", rejected.Message);
        Assert.Equal(3, access.CheckCount);
        Assert.Equal(3, ((RecordingGroupManager)hub.Groups).AddCount);
    }

    [Fact]
    public async Task Unsubscribe_releases_a_distinct_subscription_slot()
    {
        var access = new CountingRealtimeAccessPolicy();
        var hub = new MatchUpdatesHub(access, new RealtimeConnectionGate(1), new RealtimeHubMethodLimiter(10),
            new RealtimeHubSubscriptionTracker(1));
        hub.Context = new TestHubCallerContext();
        var groups = new RecordingGroupManager();
        hub.Groups = groups;
        var first = Guid.Parse(MatchId);

        await hub.SubscribeToMatch(first);
        await hub.UnsubscribeFromMatch(first);
        await hub.SubscribeToMatch(Guid.Parse("22222222-2222-4222-8222-222222222222"));

        Assert.Equal(2, access.CheckCount);
        Assert.Equal(2, groups.AddCount);
        Assert.Equal(1, groups.RemoveCount);
    }

    [Fact]
    public async Task Unauthorized_match_ids_do_not_accumulate_and_full_cap_rejects_before_policy_check()
    {
        var allowedMatchId = Guid.Parse(MatchId);
        var access = new CountingRealtimeAccessPolicy(matchId => matchId == allowedMatchId);
        var hub = new MatchUpdatesHub(access, new RealtimeConnectionGate(1), new RealtimeHubMethodLimiter(10),
            new RealtimeHubSubscriptionTracker(1));
        hub.Context = new TestHubCallerContext();
        hub.Groups = new RecordingGroupManager();

        await Assert.ThrowsAsync<HubException>(() => hub.SubscribeToMatch(
            Guid.Parse("22222222-2222-4222-8222-222222222222")));
        await hub.SubscribeToMatch(allowedMatchId);
        var rejected = await Assert.ThrowsAsync<HubException>(() => hub.SubscribeToMatch(
            Guid.Parse("33333333-3333-4333-8333-333333333333")));

        Assert.Equal("Realtime match subscription limit exceeded.", rejected.Message);
        Assert.Equal(2, access.CheckCount);
        Assert.Equal(1, ((RecordingGroupManager)hub.Groups).AddCount);
    }

    [Fact]
    public async Task Disconnect_clears_connection_scoped_subscription_memberships()
    {
        var access = new CountingRealtimeAccessPolicy();
        var tracker = new RealtimeHubSubscriptionTracker(1);
        var hub = new MatchUpdatesHub(access, new RealtimeConnectionGate(1), new RealtimeHubMethodLimiter(10), tracker);
        var context = new TestHubCallerContext();
        hub.Context = context;
        hub.Groups = new RecordingGroupManager();

        await hub.SubscribeToMatch(Guid.Parse(MatchId));
        await hub.OnDisconnectedAsync(null);

        Assert.True(tracker.TryReserve(context.Items, Guid.Parse("22222222-2222-4222-8222-222222222222"), out var isNew));
        Assert.True(isNew);
    }

    [Fact]
    public async Task Partial_group_add_failure_removes_membership_and_releases_the_reserved_slot()
    {
        var access = new CountingRealtimeAccessPolicy();
        var context = new TestHubCallerContext();
        var groups = new RecordingGroupManager { FailAddAfterAdd = true };
        var hub = new MatchUpdatesHub(access, new RealtimeConnectionGate(1), new RealtimeHubMethodLimiter(10),
            new RealtimeHubSubscriptionTracker(1))
        {
            Context = context,
            Groups = groups,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.SubscribeToMatch(Guid.Parse(MatchId)));
        Assert.Equal(1, groups.RemoveCount);
        Assert.Empty(groups.Memberships);
        groups.FailAddAfterAdd = false;

        await hub.SubscribeToMatch(Guid.Parse("22222222-2222-4222-8222-222222222222"));

        Assert.Equal(2, groups.AddCount);
        Assert.Equal(2, access.CheckCount);
        Assert.Equal(0, context.AbortCount);
    }

    [Fact]
    public async Task Failed_group_cleanup_aborts_and_keeps_the_slot_until_disconnect()
    {
        var allowed = true;
        var access = new CountingRealtimeAccessPolicy(_ => allowed);
        var tracker = new RealtimeHubSubscriptionTracker(1);
        var context = new TestHubCallerContext();
        var groups = new RecordingGroupManager { FailRemove = true };
        var hub = new MatchUpdatesHub(access, new RealtimeConnectionGate(1), new RealtimeHubMethodLimiter(10), tracker)
        {
            Context = context,
            Groups = groups,
        };
        var first = Guid.Parse(MatchId);
        var second = Guid.Parse("22222222-2222-4222-8222-222222222222");

        await hub.SubscribeToMatch(first);
        allowed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => hub.SubscribeToMatch(first));

        Assert.Equal(1, context.AbortCount);
        Assert.Contains((context.ConnectionId, MatchUpdatesHub.GroupName(first)), groups.Memberships);
        Assert.False(tracker.TryReserve(context.Items, second, out _));
        await hub.OnDisconnectedAsync(null);
        groups.ClearConnection(context.ConnectionId);
        Assert.Empty(groups.Memberships);
        Assert.True(tracker.TryReserve(context.Items, second, out var isNew));
        Assert.True(isNew);
    }

    private sealed class CountingRealtimeAccessPolicy(Func<Guid, bool>? isAllowed = null) : IMatchRealtimeAccessPolicy
    {
        public int CheckCount { get; private set; }

        public Task<MatchRealtimeAccessDecision> CheckAsync(ClaimsPrincipal principal, Guid matchId,
            CancellationToken cancellationToken)
        {
            CheckCount++;
            return Task.FromResult(new MatchRealtimeAccessDecision(isAllowed?.Invoke(matchId) == false
                ? MatchRealtimeAccessStatus.Forbidden
                : MatchRealtimeAccessStatus.Allowed));
        }
    }

    private sealed class TestHubCallerContext : HubCallerContext
    {
        public int AbortCount { get; private set; }
        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => "test-user";
        public override ClaimsPrincipal? User { get; } = new(new ClaimsIdentity("test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() => AbortCount++;
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        public HashSet<(string ConnectionId, string GroupName)> Memberships { get; } = [];
        public int AddCount { get; private set; }
        public int RemoveCount { get; private set; }
        public bool FailAddAfterAdd { get; set; }
        public bool FailRemove { get; set; }
        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            AddCount++;
            Memberships.Add((connectionId, groupName));
            if (FailAddAfterAdd) throw new InvalidOperationException("Simulated partial group add failure.");
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            RemoveCount++;
            if (FailRemove) throw new InvalidOperationException("Simulated group removal failure.");
            Memberships.Remove((connectionId, groupName));
            return Task.CompletedTask;
        }

        public void ClearConnection(string connectionId) => Memberships.RemoveWhere(item => item.ConnectionId == connectionId);
    }

    [Fact]
    public async Task Oversized_request_is_rejected_with_413_before_authentication_or_json_binding()
    {
        using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"payload\":\"" + new string('x', 5000) + "\"}", System.Text.Encoding.UTF8, "application/json");

        using var response = await client.PostAsync("/api/v1/realtime/negotiate", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://identity.test.invalid")]
    public async Task Missing_or_non_https_issuer_fails_closed(string? authority)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (authority is not null) builder.UseSetting("Authentication:Authority", authority);
            builder.UseSetting("Authentication:Audience", "test-audience");
            builder.UseSetting("Authentication:RoleClaimType", "test-role");
            builder.UseSetting("Authentication:MatchEntitlementClaimType", "test-matches");
            builder.UseSetting("Authorization:ReadRoles", "test-reader");
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "malformed");

        using var response = await client.GetAsync($"/api/v1/canonical/matches/{MatchId}/summary?atSeconds=120");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Synthetic_demo_routes_are_local_only_and_mark_responses_as_synthetic()
    {
        using var production = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => ApiTestAuthentication.Configure(builder));
        using var productionClient = production.CreateClient();
        using var hidden = await productionClient.GetAsync("/api/v1/matches");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        using var local = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ApiTestAuthentication.Configure(builder);
            builder.UseEnvironment("Development");
            builder.UseSetting("Runtime:Profile", "local");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Runtime:Profile"] = "local"
            }));
        });
        using var localClient = local.CreateClient();
        using var demo = await localClient.GetAsync("/api/v1/matches");
        Assert.Equal(HttpStatusCode.OK, demo.StatusCode);
        Assert.Equal("synthetic-demo", demo.Headers.GetValues("X-Data-Class").Single());
    }

    [Fact]
    public async Task Cross_match_fixture_catalog_requires_a_separate_catalog_role()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => ApiTestAuthentication.Configure(builder));
        using var ordinaryReader = AuthorizedClient(factory, "analyst", null);
        using var denied = await ordinaryReader.GetAsync("/api/v1/canonical/fixtures");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var catalogReader = AuthorizedClient(factory, "catalog-reader", null);
        using var allowed = await catalogReader.GetAsync("/api/v1/canonical/fixtures");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, allowed.StatusCode);
    }

    [Fact]
    public void Realtime_connection_gate_enforces_the_configured_cap_and_releases_capacity()
    {
        var gate = new PitchMind.Api.RealtimeConnectionGate(1);
        Assert.True(gate.TryAcquire());
        Assert.False(gate.TryAcquire());
        gate.Release();
        Assert.True(gate.TryAcquire());
        Assert.False(new PitchMind.Api.RealtimeConnectionGate(0).TryAcquire());
    }

    private static HttpClient AuthorizedClient(WebApplicationFactory<Program> factory, string role, string? matchId,
        string? subject = null, string? remoteIp = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        if (matchId is not null) client.DefaultRequestHeaders.Add("X-Test-Match", matchId);
        if (subject is not null) client.DefaultRequestHeaders.Add("X-Test-Subject", subject);
        if (remoteIp is not null) client.DefaultRequestHeaders.Add("X-Test-Remote-IP", remoteIp);
        return client;
    }

    private sealed class TestRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers["X-Test-Remote-IP"].ToString(), out var address))
                    context.Connection.RemoteIpAddress = address;
                await nextMiddleware();
            });
            next(app);
        };
    }

    private sealed record ProblemBody(string Code);
}
