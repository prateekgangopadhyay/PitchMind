using System.Net;
using System.Text;
using PitchMind.AgentWorker.Agents;
using Xunit;

namespace PitchMind.AgentWorker.Tests;

public sealed class TacticalKnowledgeRetrievalTests
{
    [Fact]
    public async Task LocalRetriever_ReturnsOnlyAllowedRelevantCards()
    {
        var retriever = new LocalTacticalKnowledgeRetriever();
        var result = await retriever.SearchAsync("formation matchup and pressing block", new[] { "press-and-block-dynamics", "set-piece-restarts" },
            CancellationToken.None);

        Assert.NotEmpty(result.Sources);
        Assert.All(result.Sources, source => Assert.Contains(source.TopicId,
            new[] { "press-and-block-dynamics", "set-piece-restarts" }));
        Assert.DoesNotContain(result.Sources, source => source.TopicId == "team-movement");
    }

    [Fact]
    public async Task AzureRetriever_UsesManagedIdentityHybridSearchAndProfileTopicFilter()
    {
        var handler = new SearchHandler();
        using var http = new HttpClient(handler);
        var retriever = Create(http);

        var result = await retriever.SearchAsync("how does a high press expose the weak side?",
            new[] { "press-and-block-dynamics" }, CancellationToken.None);

        Assert.Single(result.Sources);
        Assert.Equal("press-and-block-dynamics", result.Sources[0].TopicId);
        Assert.Equal("Bearer search-token", handler.Authorization);
        Assert.Contains("search.in(topicId", handler.RequestBody);
        Assert.Contains("vectorQueries", handler.RequestBody);
        Assert.Contains("contentVector", handler.RequestBody);
    }

    [Fact]
    public async Task AzureRetriever_DropsDocumentsOutsideProfileAllowlist()
    {
        var handler = new SearchHandler(returnDisallowedTopic: true);
        using var http = new HttpClient(handler);
        var result = await Create(http).SearchAsync("press", new[] { "press-and-block-dynamics" }, CancellationToken.None);

        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task AzureRetriever_RejectsMalformedSearchResponse()
    {
        var handler = new SearchHandler(malformedResponse: true);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(http).SearchAsync(
            "press", new[] { "press-and-block-dynamics" }, CancellationToken.None));
    }

    [Fact]
    public void Options_RejectNonAzureSearchEndpoints()
    {
        Assert.Throws<InvalidOperationException>(() => new AzureAiSearchTacticalKnowledgeOptions(
            new Uri("https://search.example.test"), "football-knowledge", "football-semantic").Validate());
    }

    private static AzureAiSearchTacticalKnowledgeRetriever Create(HttpClient http) => new(http,
        new StubTokenProvider(), new AzureAiSearchTacticalKnowledgeOptions(
            new Uri("https://pitchmind.search.windows.net"), "football-knowledge", "football-semantic"));

    private sealed class StubTokenProvider : IAzureAiSearchTokenProvider
    {
        public ValueTask<string> GetTokenAsync(CancellationToken cancellationToken) => ValueTask.FromResult("search-token");
    }

    private sealed class SearchHandler(bool returnDisallowedTopic = false, bool malformedResponse = false) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Equal("2025-09-01", System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["api-version"]);
            if (malformedResponse)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not-json") };
            var topic = returnDisallowedTopic ? "set-piece-restarts" : "press-and-block-dynamics";
            var payload = $$"""
                {"value":[{"id":"{{topic}}-001","topicId":"{{topic}}","title":"Pressing block","content":"A coordinated press can expose the far side if the shift is late.","sourceUri":"https://example.org/football/pressing","sourceType":"curated-guide","version":"1.0.0"}]}
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }
    }
}
