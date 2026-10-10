using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PitchMind.AgentWorker.Foundry;
using PitchMind.Application.MatchIntelligence;
using Xunit;

namespace PitchMind.AgentWorker.Tests;

public sealed class FoundryExplanationAdapterTests
{
    [Fact]
    public async Task GenerateAsync_UsesManagedTokenAndReturnsCandidateCitingOnlyPacketEvidence()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(Completion("The press led to a regain.", "event-1"))));
        using var http = new HttpClient(handler);
        using var adapter = Create(http, new StubTokenProvider());

        var result = await adapter.GenerateAsync(Request(), CancellationToken.None);

        Assert.True(result.Succeeded);
        var candidate = Assert.IsType<CandidateExplanation>(result.Candidate);
        Assert.Equal("The press led to a regain.", candidate.Text);
        Assert.Equal(new[] { "event-1" }, Assert.Single(candidate.Claims).EvidenceIds);
        Assert.Equal("deployment-model", candidate.ModelVersion);
        Assert.Equal("match-1", candidate.MatchId);
        Assert.Equal(3, candidate.CanonicalRevision);
        Assert.Equal(120, candidate.SelectedTimeSeconds);
        Assert.Equal(12, candidate.InputTokens);
        Assert.Equal(8, candidate.OutputTokens);
        Assert.Equal("Bearer test-token", handler.Authorization);
        Assert.EndsWith("/openai/v1/chat/completions", handler.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("test-deployment", body.RootElement.GetProperty("model").GetString());
        Assert.Equal("json_schema", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Contains("untrusted data", body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task GenerateAsync_PreservesSelectedAddedTimeBeyondNinetyMinutes()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(Completion("The pattern is visible.", "event-1"))));
        using var http = new HttpClient(handler);
        using var adapter = Create(http, new StubTokenProvider());
        var context = Packet() with
        {
            SelectedTimeSeconds = 5_761,
            ProjectionAsOfTimeSeconds = 5_761,
            Evidence = Array.AsReadOnly(new[] { Packet().Evidence[0] with { ClockSeconds = 5_761 } })
        };

        var result = await adapter.GenerateAsync(new ExplanationGenerationRequest(context, ExplanationAudience.Fan,
            "Explain this"), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(5_761, result.Candidate!.SelectedTimeSeconds);
    }

    [Theory]
    [InlineData(ExplanationAudience.Fan, "fan")]
    [InlineData(ExplanationAudience.Analyst, "analyst")]
    public async Task GenerateAsync_UsesAudienceOnlyAsPresentationContext(ExplanationAudience audience, string expectedAudience)
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(Completion("The press led to a regain.", "event-1"))));
        using var http = new HttpClient(handler);
        using var adapter = Create(http, new StubTokenProvider());

        var result = await adapter.GenerateAsync(Request(audience), CancellationToken.None);

        Assert.True(result.Succeeded);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        var prompt = body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        using var userContext = JsonDocument.Parse(prompt!);
        Assert.Equal(expectedAudience, userContext.RootElement.GetProperty("audience").GetString());
        Assert.Equal(120, userContext.RootElement.GetProperty("context").GetProperty("selectedTimeSeconds").GetInt32());
        Assert.Equal(3, userContext.RootElement.GetProperty("context").GetProperty("canonicalRevision").GetInt32());
    }

    [Fact]
    public async Task GenerateAsync_RejectsModelCitationOutsideSelectedContext()
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(Completion("A goal was scored.", "future-event"))));
        using var http = new HttpClient(handler);
        using var adapter = Create(http, new StubTokenProvider());

        var result = await adapter.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ExplanationGenerationFailure.InvalidCandidate, result.Failure);
    }

    [Fact]
    public async Task GenerateAsync_ReturnsUnavailableForProviderErrorWithoutLeakingBody()
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("provider diagnostic secret")
        }));
        using var http = new HttpClient(handler);
        using var adapter = Create(http, new StubTokenProvider());

        var result = await adapter.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ExplanationGenerationFailure.Unavailable, result.Failure);
    }

    [Fact]
    public async Task GenerateAsync_RejectsOversizedResponseAndNonHttpsEndpoint()
    {
        var oversized = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 2_000), Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(oversized);
        using var adapter = Create(http, new StubTokenProvider(), maximumResponseBytes: 1_024);

        var result = await adapter.GenerateAsync(Request(), CancellationToken.None);

        Assert.Equal(ExplanationGenerationFailure.InvalidCandidate, result.Failure);
        Assert.Throws<InvalidOperationException>(() => new FoundryAdapterOptions(new Uri("http://example.test"), "model").Validate());
        Assert.Throws<InvalidOperationException>(() => new FoundryAdapterOptions(new Uri("https://attacker.example"), "model").Validate());
        Assert.Throws<InvalidOperationException>(() => new FoundryAdapterOptions(new Uri("https://127.0.0.1"), "model").Validate());
        using var redirectHandler = FoundryHttpClientFactory.CreateHandler();
        Assert.False(redirectHandler.AllowAutoRedirect);
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnboundedPacketBeforeRequestingIdentity()
    {
        var tokenProvider = new StubTokenProvider();
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(Completion("A safe answer.", "event-1"))));
        using var http = new HttpClient(handler);
        using var adapter = Create(http, tokenProvider);
        var oversizedContext = Packet() with
        {
            Claim = Packet().Claim with { PermittedInterpretation = new string('x', 100_000) }
        };

        var result = await adapter.GenerateAsync(new ExplanationGenerationRequest(oversizedContext, ExplanationAudience.Fan,
            "Why did the press work?"), CancellationToken.None);

        Assert.Equal(ExplanationGenerationFailure.InvalidCandidate, result.Failure);
        Assert.Equal(0, tokenProvider.Calls);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task GenerateAsync_RejectsMalformedEvidenceWithoutThrowingOrRequestingIdentity()
    {
        var tokenProvider = new StubTokenProvider();
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(Completion("A safe answer.", "event-1"))));
        using var http = new HttpClient(handler);
        using var adapter = Create(http, tokenProvider);
        var malformedContext = Packet() with
        {
            Evidence = Array.AsReadOnly(new[] { Packet().Evidence[0] with { PayloadJson = null! } })
        };

        var result = await adapter.GenerateAsync(new ExplanationGenerationRequest(malformedContext, ExplanationAudience.Fan,
            "Why did the press work?"), CancellationToken.None);

        Assert.Equal(ExplanationGenerationFailure.InvalidCandidate, result.Failure);
        Assert.Equal(0, tokenProvider.Calls);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task GenerateAsync_RejectsEvidenceNewerThanProjectionWatermark()
    {
        var tokenProvider = new StubTokenProvider();
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(Completion("A safe answer.", "event-1"))));
        using var http = new HttpClient(handler);
        using var adapter = Create(http, tokenProvider);
        var futureSequenceContext = Packet() with
        {
            Evidence = Array.AsReadOnly(new[] { Packet().Evidence[0] with { Sequence = 9 } })
        };

        var result = await adapter.GenerateAsync(new ExplanationGenerationRequest(futureSequenceContext, ExplanationAudience.Fan,
            "Why did the press work?"), CancellationToken.None);

        Assert.Equal(ExplanationGenerationFailure.InvalidCandidate, result.Failure);
        Assert.Equal(0, tokenProvider.Calls);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task GenerateAsync_PropagatesCallerCancellation()
    {
        var handler = new StubHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler);
        using var adapter = Create(http, new StubTokenProvider());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => adapter.GenerateAsync(Request(), cancellation.Token));
    }

    private static FoundryExplanationAdapter Create(HttpClient http, IFoundryAccessTokenProvider tokenProvider, int maximumResponseBytes = 16_384) =>
        new(http, tokenProvider,
            new FoundryAdapterOptions(new Uri("http://127.0.0.1:1234/openai/v1"), "test-deployment",
                MaximumResponseBytes: maximumResponseBytes, AllowInsecureHttpForLocalTesting: true),
            NullLogger<FoundryExplanationAdapter>.Instance);

    private static ExplanationGenerationRequest Request(ExplanationAudience audience = ExplanationAudience.Fan) =>
        new(Packet(), audience, "Why did the press work?");

    private static MatchIntelligenceContextPacket Packet() => new(
        "match-1", 3, 120, 120, 8, new MatchIntelligenceScore(1, 0),
        new MatchIntelligenceClaim("insight-1", MatchIntelligenceClaimCategory.EvidenceSupportedPattern,
            "press-sequence-v1", "press followed by regain", "do not claim causation", "untrusted data"),
        "synthetic-v1", "approved-synthetic-feed", "untrusted-source-metadata",
        new string('a', 64), new string('b', 64),
        Array.AsReadOnly(new[] { new MatchIntelligenceEvidence("event", "event-1", "supports", 8, 110, 1,
            "{\"type\":\"pass\"}", "untrusted data") }),
        Array.AsReadOnly(new[] { "no intent inference" }), 1_024, "match-intelligence-context-v1");

    private static string Completion(string text, string evidenceId) => JsonSerializer.Serialize(new
    {
        model = "deployment-model",
        choices = new[] { new { finish_reason = "stop", message = new { content = JsonSerializer.Serialize(new { text, claims = new[] { new { text, evidenceIds = new[] { evidenceId } } } }) } } },
        usage = new { prompt_tokens = 12, completion_tokens = 8 }
    });

    private static HttpResponseMessage JsonResponse(string content)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
        response.Headers.Add("x-request-id", "trace-test");
        return response;
    }

    private sealed class StubTokenProvider : IFoundryAccessTokenProvider
    {
        public int Calls { get; private set; }
        public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult("test-token");
        }
    }

    private sealed class StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await responseFactory(cancellationToken);
        }
    }
}
