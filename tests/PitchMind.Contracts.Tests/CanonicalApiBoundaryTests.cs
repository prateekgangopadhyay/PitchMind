using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class CanonicalApiBoundaryTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public CanonicalApiBoundaryTests(WebApplicationFactory<Program> factory) =>
        this.factory = factory.WithWebHostBuilder(builder => ApiTestAuthentication.Configure(builder));

    [Theory]
    [InlineData("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", 120, null)]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 121, null)]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 120, 3)]
    public async Task Event_cursor_cannot_be_reused_for_another_match_time_or_revision(
        string cursorMatchId, int cursorTime, int? requestedRevision)
    {
        using var client = factory.CreateClient();
        AddMatchReaderClaims(client);
        var cursor = EncodeCursor(cursorMatchId, cursorTime, revision: 2, afterSequence: 10);
        var matchId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
        var revisionQuery = requestedRevision is null ? string.Empty : $"&revision={requestedRevision}";

        using var response = await client.GetAsync(
            $"/api/v1/canonical/matches/{matchId}/events?atSeconds=120{revisionQuery}&cursor={Uri.EscapeDataString(cursor)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("INVALID_REQUEST", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(StatusCodes.Status400BadRequest, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Request_cancellation_reaches_the_canonical_projection_reader()
    {
        var reader = new CancellationObservingProjectionReader();
        using var testFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMatchProjectionReader>();
            services.AddSingleton<IMatchProjectionReader>(reader);
        }));
        using var client = testFactory.CreateClient();
        AddMatchReaderClaims(client);
        using var cancellation = new CancellationTokenSource();
        var request = client.GetAsync(
            "/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/summary?atSeconds=120",
            cancellation.Token);

        var observedToken = await reader.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observedToken.CanBeCanceled);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await request);
        await reader.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Event_cursor_pins_revision_when_query_omits_revision()
    {
        var reader = new RevisionCapturingProjectionReader();
        using var testFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IMatchProjectionReader>();
            services.AddSingleton<IMatchProjectionReader>(reader);
        }));
        using var client = testFactory.CreateClient();
        AddMatchReaderClaims(client);
        var cursor = EncodeCursor("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", 120, revision: 2, afterSequence: 10);

        using var response = await client.GetAsync(
            $"/api/v1/canonical/matches/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/events?atSeconds=120&cursor={Uri.EscapeDataString(cursor)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(2, await reader.MaximumRevision.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private static string EncodeCursor(string matchId, int selectedTime, int revision, long afterSequence)
    {
        var json = JsonSerializer.Serialize(new
        {
            matchId,
            selectedTimeSeconds = selectedTime,
            canonicalRevision = revision,
            afterSequence
        });
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void AddMatchReaderClaims(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Test-Role", "analyst");
        client.DefaultRequestHeaders.Add("X-Test-Match", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    }

    private sealed class CancellationObservingProjectionReader : IMatchProjectionReader
    {
        public TaskCompletionSource<CancellationToken> RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(
            MatchId? afterMatchId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<SelectedMatchProjection?> GetAtAsync(
            MatchId matchId, int selectedSeconds, int? maximumRevision, CancellationToken cancellationToken)
        {
            RequestStarted.TrySetResult(cancellationToken);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }

        public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(
            SelectedMatchProjection projection, int selectedSeconds, long afterSequence, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(
            SelectedMatchProjection projection, int selectedSeconds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class RevisionCapturingProjectionReader : IMatchProjectionReader
    {
        public TaskCompletionSource<int?> MaximumRevision { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<PersistedMatchProjection>> ListLatestAsync(
            MatchId? afterMatchId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<SelectedMatchProjection?> GetAtAsync(
            MatchId matchId, int selectedSeconds, int? maximumRevision, CancellationToken cancellationToken)
        {
            MaximumRevision.TrySetResult(maximumRevision);
            return Task.FromResult<SelectedMatchProjection?>(null);
        }

        public Task<PersistedMatchEventPage> ReadEventsPageAtAsync(
            SelectedMatchProjection projection, int selectedSeconds, long afterSequence, int limit,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public async IAsyncEnumerable<MatchEvent> ReadEventsAtAsync(
            SelectedMatchProjection projection, int selectedSeconds,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }
}
