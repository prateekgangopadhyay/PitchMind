using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using PitchMind.Api;
using Xunit;

namespace PitchMind.Contracts.Tests;

[Collection("Api telemetry listeners")]
public sealed class ApiReadinessAndTelemetryTests
{
    [Fact]
    public async Task Local_synthetic_profile_is_ready_and_liveness_is_independent()
    {
        using var factory = CreateFactory("Development", new Dictionary<string, string?>
        {
            ["Runtime:Profile"] = "local"
        });
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        using var legacyLive = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal(HttpStatusCode.OK, legacyLive.StatusCode);
        Assert.Empty(await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_canonical_dependency_is_not_ready_outside_local_synthetic_profile()
    {
        using var factory = CreateFactory("Production", new Dictionary<string, string?>
        {
            ["Runtime:Profile"] = "azure",
            ["Identity:Authority"] = "https://login.microsoftonline.com/00000000-0000-0000-0000-000000000001/v2.0",
            ["Identity:Audience"] = "api://pitchmind-readiness-test",
            ["Identity:DevelopmentHeaderAuthEnabled"] = "false"
        });
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var body = await ready.Content.ReadAsStringAsync();
        Assert.DoesNotContain("connection", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Configured_but_unreachable_postgres_is_not_ready_and_probe_is_bounded()
    {
        using var factory = CreateFactory("Development", new Dictionary<string, string?>
        {
            ["Runtime:Profile"] = "local",
            ["ConnectionStrings:PitchMind"] = "Host=127.0.0.1;Port=1;Database=pitchmind;Username=test;Password=secret;Timeout=20;Command Timeout=20"
        });
        using var client = factory.CreateClient();
        var started = System.Diagnostics.Stopwatch.StartNew();

        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(5), $"Probe took {started.Elapsed}.");
        var body = await ready.Content.ReadAsStringAsync();
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api_metrics_use_route_templates_and_never_add_match_identifiers_as_dimensions()
    {
        var observations = new List<(string Name, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (instrument.Meter.Name == "PitchMind.Api") activeListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            observations.Add((instrument.Name, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
            observations.Add((instrument.Name, tags.ToArray())));
        listener.Start();

        using var factory = CreateFactory("Development", new Dictionary<string, string?> { ["Runtime:Profile"] = "local" });
        using var client = factory.CreateClient();
        var matchId = Guid.NewGuid().ToString();
        using var response = await client.GetAsync($"/api/v1/canonical/matches/{matchId}/summary?atSeconds=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var request = Assert.Single(observations, item => item.Name == "pitchmind.api.requests" &&
            item.Tags.Any(tag => tag.Key == "http.route" && tag.Value?.ToString() == "/api/v1/canonical/matches/{matchId}/summary"));
        Assert.Contains(request.Tags, tag => tag.Key == "http.route" && tag.Value?.ToString() == "/api/v1/canonical/matches/{matchId}/summary");
        Assert.DoesNotContain(request.Tags, tag => tag.Key.Contains("match", StringComparison.OrdinalIgnoreCase) && tag.Key != "http.route");
        Assert.DoesNotContain(request.Tags, tag => tag.Value?.ToString()?.Contains(matchId!, StringComparison.Ordinal) == true);
        Assert.Contains(observations, item => item.Name == "pitchmind.api.request.duration");

        var error = Assert.Single(observations, item => item.Name == "pitchmind.api.errors" &&
            item.Tags.Any(tag => tag.Key == "http.route" && tag.Value?.ToString() == "/api/v1/canonical/matches/{matchId}/summary"));
        Assert.Contains(error.Tags, tag => tag.Key == "http.response.status_class" && tag.Value?.ToString() == "4xx");
        Assert.DoesNotContain(error.Tags, tag => tag.Value?.ToString()?.Contains(matchId, StringComparison.Ordinal) == true);

        using var customMethodOne = await client.SendAsync(new HttpRequestMessage(new HttpMethod("X-PITCHMIND-A"), "/api/health"));
        using var customMethodTwo = await client.SendAsync(new HttpRequestMessage(new HttpMethod("X-PITCHMIND-B"), "/api/health"));
        var otherMethodRequests = observations.Where(item => item.Name == "pitchmind.api.requests" &&
            item.Tags.Any(tag => tag.Key == "http.request.method" && tag.Value?.ToString() == "_OTHER")).ToArray();
        Assert.Equal(2, otherMethodRequests.Length);
        Assert.All(otherMethodRequests, item => Assert.Contains(item.Tags,
            tag => tag.Key == "http.response.status_class" && tag.Value?.ToString() == "4xx"));
    }

    [Fact]
    public async Task Current_activity_receives_correlation_and_domain_route_ids()
    {
        Activity? stopped = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "PitchMind.Api.Tests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => stopped = activity
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("PitchMind.Api.Tests");
        using var activity = source.StartActivity("api.request");
        Assert.NotNull(activity);

        var context = new DefaultHttpContext();
        context.TraceIdentifier = "correlation-test-42";
        context.Request.Method = "GET";
        context.Request.RouteValues["matchId"] = "match-123";
        context.Request.RouteValues["eventId"] = "event-456";
        context.Request.RouteValues["insightId"] = "insight-789";
        var endpointBuilder = new RouteEndpointBuilder(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("/api/v1/matches/{matchId}/events/{eventId}/insights/{insightId}"), 0);
        context.SetEndpoint(endpointBuilder.Build());

        await ApiRequestTelemetry.RecordRequestAsync(context, _ => Task.CompletedTask);
        await context.Response.CompleteAsync();

        Assert.Equal("correlation-test-42", activity!.GetTagItem("pitchmind.correlation_id"));
        Assert.Equal("match-123", activity.GetTagItem("pitchmind.match.id"));
        Assert.Equal("event-456", activity.GetTagItem("pitchmind.event.id"));
        Assert.Equal("insight-789", activity.GetTagItem("pitchmind.insight.id"));
        activity.Stop();
        Assert.NotNull(stopped);
    }

    private static WebApplicationFactory<Program> CreateFactory(string environment, IDictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting(Microsoft.AspNetCore.Hosting.WebHostDefaults.EnvironmentKey, environment);
            foreach (var setting in settings)
                builder.UseSetting(setting.Key, setting.Value ?? string.Empty);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        });
}

[CollectionDefinition("Api telemetry listeners", DisableParallelization = true)]
public sealed class ApiTelemetryListenerCollection { }
