using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;

namespace PitchMind.AgentWorker.Agents;

public sealed record TacticalKnowledgeSource(string Id, string TopicId, string Title, string Content,
    string? SourceUri, string SourceType, string Version);

public sealed record TacticalKnowledgeSearchResult(IReadOnlyList<TacticalKnowledgeSource> Sources)
{
    public static TacticalKnowledgeSearchResult Empty { get; } = new(Array.Empty<TacticalKnowledgeSource>());
}

/// <summary>Read-only retrieval over approved tactical learning material; never a canonical match evidence source.</summary>
public interface ITacticalKnowledgeRetriever
{
    Task<TacticalKnowledgeSearchResult> SearchAsync(string query, IReadOnlyCollection<string> allowedTopicIds,
        CancellationToken cancellationToken);
}

/// <summary>Bounded local fallback for Development. Azure never silently falls back to this corpus.</summary>
public sealed class LocalTacticalKnowledgeRetriever : ITacticalKnowledgeRetriever
{
    private readonly IReadOnlyDictionary<string, TacticalKnowledgeCard> _cards =
        TacticalAgentCatalog.GetKnowledgeCards(TacticalAgentCatalog.All.SelectMany(p => p.KnowledgeTopics)
            .Distinct(StringComparer.Ordinal).ToArray()).ToDictionary(card => card.Id, StringComparer.Ordinal);

    public Task<TacticalKnowledgeSearchResult> SearchAsync(string query, IReadOnlyCollection<string> allowedTopicIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query) || query.Length > 1_000 || allowedTopicIds.Count is < 1 or > 12)
            return Task.FromResult(TacticalKnowledgeSearchResult.Empty);

        var terms = Tokenize(query);
        var matches = allowedTopicIds.Distinct(StringComparer.Ordinal)
            .Where(_cards.ContainsKey)
            .Select(id => _cards[id])
            .SelectMany(card => BuildSources(card).Select(source => (Source: source, Score: Score(source.Title + " " + source.Content, terms))))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score).ThenBy(item => item.Source.Id, StringComparer.Ordinal)
            .Take(3)
            .Select(item => item.Source)
            .ToArray();
        return Task.FromResult(new TacticalKnowledgeSearchResult(Array.AsReadOnly(matches)));
    }

    private static int Score(string content, IReadOnlySet<string> terms)
    {
        var words = Tokenize(content);
        return terms.Sum(term => words.Contains(term) ? 1 : 0);
    }

    private static IEnumerable<TacticalKnowledgeSource> BuildSources(TacticalKnowledgeCard card)
    {
        var sections = new[] { card.PromptSummary }
            .Concat(card.Mechanisms.Select(value => "Mechanism: " + value))
            .Concat(card.RequiredEvidence.Select(value => "Required evidence: " + value))
            .Concat(card.AnalysisSteps.Select(value => "Analysis: " + value))
            .Concat(card.Limitations.Select(value => "Limitation: " + value));
        var chunks = new List<string>();
        var current = string.Empty;
        foreach (var section in sections)
        foreach (var sentence in section.Split(". ", StringSplitOptions.RemoveEmptyEntries))
        {
            var value = sentence.Trim();
            var candidate = current.Length == 0 ? value : current + ". " + value;
            if (candidate.Length > 4_000 && current.Length > 0)
            {
                chunks.Add(current);
                current = value;
            }
            else current = candidate;
            if (current.Length > 4_000)
                throw new InvalidDataException("Tactical knowledge paragraph exceeds the local retrieval budget.");
        }
        if (current.Length > 0) chunks.Add(current);
        return chunks.Select((content, index) => new TacticalKnowledgeSource(
            $"{card.Id}-part-{index + 1:000}", card.Id, card.Topic, content, null, "embedded-curated-card", "1.0.0"));
    }

    private static HashSet<string> Tokenize(string value) => value
        .Split(new[] { ' ', '-', '/', ',', '.', ':', ';', '?', '!', '\'', '"', '(', ')', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries)
        .Where(word => word.Length > 2)
        .Select(word => word.ToLowerInvariant())
        .ToHashSet(StringComparer.Ordinal);
}

public sealed record AzureAiSearchTacticalKnowledgeOptions(Uri Endpoint, string IndexName,
    string SemanticConfiguration, int Top = 3, int TimeoutSeconds = 3, int MaximumResponseBytes = 65_536)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        if (Endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(Endpoint.UserInfo) ||
            !string.IsNullOrEmpty(Endpoint.Query) || !string.IsNullOrEmpty(Endpoint.Fragment) ||
            !Endpoint.IdnHost.EndsWith(".search.windows.net", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Knowledge Search endpoint must be an HTTPS Azure AI Search service endpoint.");
        if (!SafeName(IndexName) || !SafeName(SemanticConfiguration) || Top is < 1 or > 5 ||
            TimeoutSeconds is < 1 or > 10 || MaximumResponseBytes is < 1_024 or > 262_144)
            throw new InvalidOperationException("Knowledge Search settings are invalid or outside their bounds.");
    }

    private static bool SafeName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

public interface IAzureAiSearchTokenProvider
{
    ValueTask<string> GetTokenAsync(CancellationToken cancellationToken);
}

public sealed class ManagedIdentityAzureAiSearchTokenProvider(TokenCredential credential) : IAzureAiSearchTokenProvider
{
    private static readonly TokenRequestContext TokenContext = new(["https://search.azure.com/.default"]);
    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken) =>
        (await credential.GetTokenAsync(TokenContext, cancellationToken)).Token;
}

/// <summary>
/// Hybrid semantic/vector retrieval from an Azure AI Search index populated from the curated KB corpus.
/// Search results are untrusted explanatory guidance and are not match facts or claim citations.
/// </summary>
public sealed class AzureAiSearchTacticalKnowledgeRetriever : ITacticalKnowledgeRetriever
{
    private const string ApiVersion = "2025-09-01";
    private readonly HttpClient _http;
    private readonly IAzureAiSearchTokenProvider _tokens;
    private readonly AzureAiSearchTacticalKnowledgeOptions _options;
    private readonly SemaphoreSlim _concurrency = new(2, 2);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AzureAiSearchTacticalKnowledgeRetriever(HttpClient http, IAzureAiSearchTokenProvider tokens,
        AzureAiSearchTacticalKnowledgeOptions options)
    {
        _http = http;
        _tokens = tokens;
        _options = options;
        _options.Validate();
    }

    public async Task<TacticalKnowledgeSearchResult> SearchAsync(string query, IReadOnlyCollection<string> allowedTopicIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 1_000 || query.Any(char.IsControl) ||
            allowedTopicIds.Count is < 1 or > 12 || allowedTopicIds.Any(id => !SafeTopicId(id)))
            return TacticalKnowledgeSearchResult.Empty;
        if (!await _concurrency.WaitAsync(0, cancellationToken))
            throw new HttpRequestException("Knowledge retrieval capacity is full.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
            var token = await _tokens.GetTokenAsync(timeout.Token);
            var endpoint = new Uri(_options.Endpoint,
                $"indexes/{Uri.EscapeDataString(_options.IndexName)}/docs/search?api-version={ApiVersion}");
            var requestBody = JsonSerializer.SerializeToUtf8Bytes(new
            {
                search = query,
                searchFields = "title,content",
                queryType = "semantic",
                semanticConfiguration = _options.SemanticConfiguration,
                vectorQueries = new[] { new { kind = "text", text = query, fields = "contentVector", k = _options.Top } },
                filter = $"search.in(topicId, '{string.Join(',', allowedTopicIds.Distinct(StringComparer.Ordinal))}', ',')",
                select = "id,topicId,title,content,sourceUri,sourceType,version",
                top = _options.Top
            }, JsonOptions);
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            message.Content = new ByteArrayContent(requestBody);
            message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("Azure AI Search tactical knowledge query failed.", null, response.StatusCode);
            var bytes = await ReadBoundedAsync(response.Content, _options.MaximumResponseBytes, timeout.Token);
            return ParseResults(bytes, allowedTopicIds, _options.Top);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    private static TacticalKnowledgeSearchResult ParseResults(byte[]? bytes, IReadOnlyCollection<string> allowedTopicIds, int maximum)
    {
        if (bytes is null) throw new InvalidDataException("Knowledge Search response exceeded its configured byte limit.");
        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (JsonException exception) { throw new InvalidDataException("Knowledge Search response is not valid JSON.", exception); }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array ||
                values.GetArrayLength() > maximum)
                throw new InvalidDataException("Knowledge Search response has an invalid result collection.");

            var allowed = allowedTopicIds.ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<TacticalKnowledgeSource>(maximum);
            foreach (var item in values.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !TryString(item, "id", 128, out var id) || !SafeDocumentId(id) ||
                    !TryString(item, "topicId", 64, out var topicId) || !allowed.Contains(topicId) ||
                    !TryString(item, "title", 256, out var title) || !TryString(item, "content", 4_000, out var content) ||
                    !TryString(item, "sourceType", 64, out var sourceType) || !TryString(item, "version", 64, out var version) ||
                    !seen.Add(id))
                    continue;
                string? sourceUri = null;
                if (item.TryGetProperty("sourceUri", out var uriValue) && uriValue.ValueKind == JsonValueKind.String)
                {
                    var uriText = uriValue.GetString();
                    if (uriText is { Length: <= 2_048 } && Uri.TryCreate(uriText, UriKind.Absolute, out var uri) &&
                        uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)) sourceUri = uri.AbsoluteUri;
                }
                results.Add(new TacticalKnowledgeSource(id, topicId, title, content, sourceUri, sourceType, version));
            }
            return new TacticalKnowledgeSearchResult(Array.AsReadOnly(results.ToArray()));
        }
    }

    private static bool TryString(JsonElement item, string name, int maxLength, out string value)
    {
        value = string.Empty;
        return item.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
            (value = property.GetString() ?? string.Empty) is { Length: > 0 } && value.Length <= maxLength &&
            !value.Any(char.IsControl);
    }

    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maximumBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[4_096];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > maximumBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool SafeTopicId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');

    private static bool SafeDocumentId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
        value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}
