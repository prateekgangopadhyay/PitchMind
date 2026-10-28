using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Core;
using Azure.Identity;
using PitchMind.Infrastructure.Feeds;

namespace PitchMind.Api;

public sealed record CandidateAnalystQuestion(string MatchId, decimal SelectedSourceClockValue,
    string Audience, string Question, string AgentName);
public sealed record CandidateAnalystClaim(string Text, IReadOnlyList<string> EvidenceIds);
public sealed record CandidateAnalystAnswer(string Status, string Answer, IReadOnlyList<CandidateAnalystClaim> Claims,
    string MatchId, decimal SelectedSourceClockValue, string SourceSha256, string AgentName, string TruthClass,
    IReadOnlyList<string> Limitations);

public static class CandidateAnalystAgentCatalog
{
    public const string DefaultAgentName = "TacticalAnalyst";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "CoachFrameworkAnalyst", "ChanceCreationAnalyst", "GoalkeeperAnalyst", "MatchStateStrategist",
        "SetPieceStrategist", "TransitionStrategist", "DefensiveStructureAnalyst", "HighPressAnalyst",
        "PositionalPlayArchitect", DefaultAgentName
    });

    public static bool IsAllowed(string? name) => name is not null && All.Contains(name, StringComparer.Ordinal);
}

public interface ICandidateMatchAnswerGenerator
{
    Task<(string Status, string Answer, IReadOnlyList<CandidateAnalystClaim> Claims)> GenerateAsync(
        CandidateAnalystQuestion question, string sourceHash, IReadOnlyList<CandidateKafkaMessage> evidence,
        CancellationToken cancellationToken);
}

public sealed class CandidateAnalystUnavailableException : Exception
{
    public CandidateAnalystUnavailableException() : base("The configured match analyst is unavailable.") { }
    public CandidateAnalystUnavailableException(string message) : base(message) { }
    public CandidateAnalystUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class CandidateMatchNotFoundException : Exception
{
    public CandidateMatchNotFoundException() : base("The requested candidate match was not found.") { }
    public CandidateMatchNotFoundException(string message) : base(message) { }
    public CandidateMatchNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class CandidateEvidenceUnavailableException : Exception
{
    public CandidateEvidenceUnavailableException() : base("No bounded candidate evidence is available.") { }
    public CandidateEvidenceUnavailableException(string message) : base(message) { }
    public CandidateEvidenceUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Builds a bounded, source-clock-scoped hypothesis request from persisted candidate events.</summary>
public sealed class CandidateMatchAnalystService(CandidateFeedSourceCatalog catalog,
    CandidateFeedPersistence persistence, ICandidateMatchAnswerGenerator generator,
    ILogger<CandidateMatchAnalystService>? logger = null)
{
    private const int MaximumEvidenceItems = 8;
    private const int MaximumEvidencePayloadBytes = 4_096;
    private const int MaximumContextBytes = 32_768;
    private static readonly string[] EvidenceFields =
    [
        "eventId", "matchId", "sequence", "clockSeconds", "period", "eventType", "teamId", "teamName",
        "playerName", "receiverName", "pitchX", "pitchY", "endX", "endY", "outcome", "ballX", "ballY"
    ];
    private static readonly string[] PlayerPositionFields =
    ["playerName", "teamName", "teamId", "jerseyNumber", "position", "x", "y"];
    private static readonly string[] Limitations =
    [
        "This is an AI-generated hypothesis from a synthetic candidate feed, not a canonical match fact.",
        "Event meanings, source-clock semantics, coordinate units and player metrics have not been validated.",
        "Evidence IDs identify supplied records; they do not prove causation, intent or tactical correctness."
    ];

    public async Task<CandidateAnalystAnswer> AskAsync(CandidateAnalystQuestion request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MatchId) || request.MatchId.Length > 128 ||
            request.SelectedSourceClockValue is < 0 or > 10_000 ||
            request.Audience is not ("fan" or "analyst") || string.IsNullOrWhiteSpace(request.Question) ||
            request.Question.Length > 1_000 || request.Question.Any(char.IsControl) ||
            !CandidateAnalystAgentCatalog.IsAllowed(request.AgentName))
            throw new ArgumentException("The match analyst request is invalid.");

        var source = await catalog.GetAsync(cancellationToken).ConfigureAwait(false);
        if (source.Events.Count == 0 || !string.Equals(source.Events[0].SourceMatchId, request.MatchId, StringComparison.Ordinal))
            throw new CandidateMatchNotFoundException();

        var selected = new List<CandidateKafkaMessage>(MaximumEvidenceItems);
        CandidateKafkaMessage? latestWithPositions = null;
        long afterSequence = -1;
        const int pageSize = 499;
        // The approved candidate currently contains fewer than 10,000 source records.
        for (var page = 0; page < 21; page++)
        {
            var items = await persistence.ReadEventsAsync(request.MatchId, source.SourceSha256,
                afterSequence, pageSize, cancellationToken).ConfigureAwait(false);
            if (items.Count == 0) break;
            foreach (var item in items)
            {
                if (item.RawClockValue > request.SelectedSourceClockValue) continue;
                var evidencePayload = ProjectEvidencePayload(item.RawPayload, includePlayerPositions: true);
                var payloadBytes = Encoding.UTF8.GetByteCount(evidencePayload.GetRawText());
                if (payloadBytes > MaximumEvidencePayloadBytes) continue;
                selected.Add(item with
                {
                    RawPayload = ProjectEvidencePayload(item.RawPayload, includePlayerPositions: false)
                });
                latestWithPositions = item with { RawPayload = evidencePayload };
                if (selected.Count > MaximumEvidenceItems) selected.RemoveAt(0);
            }
            afterSequence = items[^1].SourceSequence;
            if (items.Count < pageSize) break;
        }

        if (selected.Count == 0) throw new CandidateEvidenceUnavailableException();
        // Keep a single full positional snapshot at the selected source time; prior
        // records provide event context without repeating the same 22-player frame.
        selected[^1] = latestWithPositions ?? throw new CandidateEvidenceUnavailableException();
        var contextBytes = selected.Sum(item => Encoding.UTF8.GetByteCount(item.RawPayload.GetRawText()) +
            Encoding.UTF8.GetByteCount(item.SourceEventId) + 192);
        if (contextBytes > MaximumContextBytes) throw new CandidateEvidenceUnavailableException();

        var allowedIds = selected.Select(item => item.SourceEventId).ToHashSet(StringComparer.Ordinal);
        (string Status, string Answer, IReadOnlyList<CandidateAnalystClaim> Claims) validated;
        for (var attempt = 1; ; attempt++)
        {
            var result = await generator.GenerateAsync(request, source.SourceSha256, selected, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                validated = NormalizeGeneratedAnswer(result.Status, result.Answer, result.Claims, allowedIds);
                break;
            }
            catch (CandidateAnalystUnavailableException) when (attempt == 1)
            {
                // Agent instructions are advisory. One bounded regeneration
                // handles occasional unsupported or malformed citations while
                // keeping local evidence validation authoritative.
                logger?.LogWarning("Foundry analyst answer failed evidence validation; retrying once.");
            }
        }

        return new(validated.Status, validated.Answer, validated.Claims, request.MatchId,
            request.SelectedSourceClockValue, source.SourceSha256, request.AgentName, "hypothesis", Limitations);
    }

    internal static (string Status, string Answer, IReadOnlyList<CandidateAnalystClaim> Claims) NormalizeGeneratedAnswer(
        string status, string answer, IReadOnlyList<CandidateAnalystClaim> claims, IReadOnlySet<string> allowedEvidenceIds)
    {
        if (status == "answered")
        {
            if (claims.Count is < 1 or > MaximumEvidenceItems)
                throw new CandidateAnalystUnavailableException();

            var normalizedClaims = new List<CandidateAnalystClaim>(claims.Count);
            foreach (var claim in claims)
            {
                var evidenceIds = claim.EvidenceIds.Distinct(StringComparer.Ordinal).ToArray();
                if (string.IsNullOrWhiteSpace(claim.Text) || claim.Text.Length > 1_000 || claim.Text.Any(char.IsControl) ||
                    evidenceIds.Length is < 1 or > MaximumEvidenceItems || evidenceIds.Any(id => !allowedEvidenceIds.Contains(id)))
                    throw new CandidateAnalystUnavailableException();
                normalizedClaims.Add(new CandidateAnalystClaim(claim.Text, Array.AsReadOnly(evidenceIds)));
            }

            // The agent's separate summary can drift from its cited claims. Only
            // expose text that is itself attached to validated evidence IDs.
            var validatedAnswer = string.Join(" ", normalizedClaims.Select(claim => claim.Text));
            if (validatedAnswer.Length > 2_500)
                throw new CandidateAnalystUnavailableException();
            return (status, validatedAnswer, Array.AsReadOnly(normalizedClaims.ToArray()));
        }

        if (status != "insufficientEvidence" || claims.Count != 0 ||
            string.IsNullOrWhiteSpace(answer) || answer.Length > 1_000 || answer.Any(char.IsControl))
            throw new CandidateAnalystUnavailableException();
        return (status, answer, claims);
    }

    internal static JsonElement ProjectEvidencePayload(JsonElement source, bool includePlayerPositions = true)
    {
        if (source.ValueKind != JsonValueKind.Object)
            throw new CandidateEvidenceUnavailableException();

        var projected = new JsonObject();
        foreach (var field in EvidenceFields)
        {
            if (source.TryGetProperty(field, out var value))
                projected[field] = JsonNode.Parse(value.GetRawText());
        }

        if (includePlayerPositions && source.TryGetProperty("playerPositions", out var positions))
        {
            if (positions.ValueKind != JsonValueKind.Array || positions.GetArrayLength() > 32)
                throw new CandidateEvidenceUnavailableException();

            var projectedPositions = new JsonArray();
            foreach (var position in positions.EnumerateArray())
            {
                if (position.ValueKind != JsonValueKind.Object)
                    throw new CandidateEvidenceUnavailableException();
                var projectedPosition = new JsonObject();
                foreach (var field in PlayerPositionFields)
                {
                    if (position.TryGetProperty(field, out var value))
                        projectedPosition[field] = JsonNode.Parse(value.GetRawText());
                }
                projectedPositions.Add(projectedPosition);
            }
            projected["playerPositions"] = projectedPositions;
        }

        return JsonSerializer.SerializeToElement(projected);
    }
}

/// <summary>Server-side Foundry project-agent transport. Model output remains an untrusted hypothesis.</summary>
public sealed class FoundryCandidateMatchAnswerGenerator : ICandidateMatchAnswerGenerator, IDisposable
{
    private const int MaximumRequestBytes = 40_960;
    private const int MaximumResponseBytes = 16_384;
    // Foundry specialist agents need enough room to finish the required JSON
    // envelope. At 512 tokens, TacticalAnalyst regularly returned an
    // `incomplete` response with its claims JSON cut off.
    private const int MaximumOutputTokens = 2_048;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ResponseContract = """
        RESPONSE CONTRACT: Return only one valid JSON object with exactly these properties and types:
        {"status":"answered","answer":"string","claims":[{"text":"string","evidenceIds":["exact records[].evidenceId"]}]}
        The only allowed status values are "answered" and "insufficientEvidence". For "answered", write a concise report of at most 3 short sentences and include 1-4 concise claims; each factual sentence must appear in a claim, and every claim must cite one or more exact records[].evidenceId values copied from the supplied packet. Cite only IDs present in the supplied records array. If the event or fact asked about is not present in those records, return "insufficientEvidence" with claims []. Never invent IDs or repeat an ID within one claim. For "insufficientEvidence", set answer to a brief reason and claims to []. Do not include Markdown fences, extra properties, or text outside the JSON object. The application builds the displayed answer from validated claim text; an uncited summary is ignored. Treat all evidence as unverified candidate-source data; do not infer intent, causation, score changes, or facts not present in the packet.
        """;

    private readonly HttpClient _client;
    private readonly TokenCredential _credential;
    private readonly Uri _projectEndpoint;
    private readonly ILogger<FoundryCandidateMatchAnswerGenerator> _logger;
    public FoundryCandidateMatchAnswerGenerator(IConfiguration configuration, TokenCredential credential,
        ILogger<FoundryCandidateMatchAnswerGenerator> logger)
    {
        _credential = credential;
        _logger = logger;
        var defaultAgentName = configuration["Foundry:AgentName"] ?? CandidateAnalystAgentCatalog.DefaultAgentName;
        var endpointValue = configuration["Foundry:Endpoint"];
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps ||
            !endpoint.IdnHost.EndsWith(".services.ai.azure.com", StringComparison.OrdinalIgnoreCase) ||
            endpoint.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is not ["api", "projects", var project] ||
            string.IsNullOrWhiteSpace(project) || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.UserInfo.Length != 0 || !CandidateAnalystAgentCatalog.IsAllowed(defaultAgentName))
            throw new InvalidOperationException("Foundry project-agent configuration is invalid.");
        _projectEndpoint = endpoint;
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }, disposeHandler: true)
        { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<(string Status, string Answer, IReadOnlyList<CandidateAnalystClaim> Claims)> GenerateAsync(
        CandidateAnalystQuestion question, string sourceHash, IReadOnlyList<CandidateKafkaMessage> evidence,
        CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Serialize(new
        {
            matchId = question.MatchId,
            selectedSourceClockValue = question.SelectedSourceClockValue,
            sourceSha256 = sourceHash,
            audience = question.Audience,
            agentName = question.AgentName,
            question = question.Question,
            records = evidence.Select(item => new
            {
                evidenceId = item.SourceEventId, sequence = item.SourceSequence,
                sourceClockValue = item.RawClockValue, sourcePeriod = item.RawPeriod,
                sourceEventType = item.RawEventType, rawPayload = item.RawPayload
            })
        }, JsonOptions);
        var body = CreateResponseRequest(input);
        if (body.Length > MaximumRequestBytes) throw new CandidateAnalystUnavailableException();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            AccessToken token;
            try
            {
                token = await _credential.GetTokenAsync(new TokenRequestContext(["https://ai.azure.com/.default"]), timeout.Token)
                    .ConfigureAwait(false);
            }
            catch (AuthenticationFailedException exception)
            {
                _logger.LogWarning("Foundry agent token acquisition failed ({FailureType}).", exception.GetType().Name);
                throw new CandidateAnalystUnavailableException();
            }
            var endpoint = new Uri(_projectEndpoint.AbsoluteUri.TrimEnd('/') + "/agents/" +
                Uri.EscapeDataString(question.AgentName) + "/endpoint/protocols/openai/responses?api-version=v1", UriKind.Absolute);
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                    message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    message.Content = new ByteArrayContent(body);
                    message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                        .ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var requestId = GetSafeRequestId(response);
                        if (attempt == 1 && IsTransient(response.StatusCode))
                        {
                            await Task.Delay(GetRetryDelay(response), timeout.Token).ConfigureAwait(false);
                            continue;
                        }
                        _logger.LogWarning(
                            "Foundry agent request failed with HTTP {StatusCode} on attempt {Attempt}. Upstream request ID: {RequestId}.",
                            (int)response.StatusCode, attempt, requestId);
                        throw new CandidateAnalystUnavailableException();
                    }
                    var bytes = await ReadBoundedAsync(response.Content, MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
                    try
                    {
                        return ParseResponse(bytes);
                    }
                    catch (CandidateAnalystUnavailableException)
                    {
                        _logger.LogWarning("Foundry agent returned a response that did not satisfy the application response contract.");
                        throw;
                    }
                }
                catch (HttpRequestException exception) when (attempt == 1)
                {
                    _logger.LogWarning("Foundry agent transport failed ({FailureType}); retrying once.", exception.GetType().Name);
                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token).ConfigureAwait(false);
                }
                catch (HttpRequestException exception)
                {
                    _logger.LogWarning("Foundry agent transport failed ({FailureType}) after retry.", exception.GetType().Name);
                    throw new CandidateAnalystUnavailableException();
                }
            }
            throw new CandidateAnalystUnavailableException();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Foundry agent request timed out.");
            throw new CandidateAnalystUnavailableException();
        }
        catch (AuthenticationFailedException)
        {
            throw new CandidateAnalystUnavailableException();
        }
    }

    private static bool IsTransient(System.Net.HttpStatusCode statusCode) =>
        statusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.BadGateway or
            System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? (retryAfter?.Date is DateTimeOffset date
            ? date - DateTimeOffset.UtcNow
            : TimeSpan.FromMilliseconds(500));
        return TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 100, 1_000));
    }

    private static string GetSafeRequestId(HttpResponseMessage response)
    {
        foreach (var header in new[] { "x-request-id", "apim-request-id" })
            if (response.Headers.TryGetValues(header, out var values))
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value.All(character =>
                        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.'))
                    return value;
            }
        return "unavailable";
    }

    internal static byte[] CreateResponseRequest(string evidencePacket)
    {
        // Agent-specific Responses endpoints do not accept the `text.format`
        // override. Send the strict application contract as a separate
        // developer message so it is not blended with the user evidence packet
        // or overridden by the selected agent's default prose style. Returned
        // claims and evidence IDs are still validated locally below.
        var input = new object[]
        {
            new
            {
                type = "message",
                role = "developer",
                content = new[] { new { type = "input_text", text = ResponseContract } }
            },
            new
            {
                type = "message",
                role = "user",
                content = new[] { new { type = "input_text", text = $"MATCH EVIDENCE PACKET (JSON):\n{evidencePacket}" } }
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(new { input, max_output_tokens = MaximumOutputTokens }, JsonOptions);
    }

    private static (string Status, string Answer, IReadOnlyList<CandidateAnalystClaim> Claims) ParseResponse(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.TryGetProperty("status", out var responseStatus) &&
                !string.Equals(responseStatus.GetString(), "completed", StringComparison.Ordinal))
                throw new CandidateAnalystUnavailableException();
            string? content = root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String
                ? outputText.GetString() : null;
            if (string.IsNullOrWhiteSpace(content) && root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                content = string.Join("", output.EnumerateArray()
                    .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "message" && item.TryGetProperty("content", out _))
                    .SelectMany(item => item.GetProperty("content").EnumerateArray())
                    .Where(item => item.TryGetProperty("type", out var type) && type.GetString() == "output_text" && item.TryGetProperty("text", out _))
                    .Select(item => item.GetProperty("text").GetString()).Where(text => !string.IsNullOrWhiteSpace(text)));
            }
            if (string.IsNullOrWhiteSpace(content)) throw new CandidateAnalystUnavailableException();
            using var answerDocument = JsonDocument.Parse(ExtractJsonObject(content));
            var answerRoot = answerDocument.RootElement;
            if (!HasRequiredProperties(answerRoot, "status", "answer", "claims")) throw new CandidateAnalystUnavailableException();
            var status = answerRoot.GetProperty("status").GetString();
            var answer = answerRoot.GetProperty("answer").GetString();
            var claimItems = answerRoot.GetProperty("claims");
            if (string.IsNullOrWhiteSpace(answer) || answer.Length > 2_500 || answer.Any(char.IsControl) ||
                claimItems.ValueKind != JsonValueKind.Array || claimItems.GetArrayLength() > 12)
                throw new CandidateAnalystUnavailableException();
            var claims = new List<CandidateAnalystClaim>();
            foreach (var item in claimItems.EnumerateArray())
            {
                if (!HasRequiredProperties(item, "text", "evidenceIds")) throw new CandidateAnalystUnavailableException();
                var text = item.GetProperty("text").GetString();
                var idsElement = item.GetProperty("evidenceIds");
                if (string.IsNullOrWhiteSpace(text) || text.Length > 1_000 || text.Any(char.IsControl) || idsElement.ValueKind != JsonValueKind.Array)
                    throw new CandidateAnalystUnavailableException();
                var ids = idsElement.EnumerateArray().Select(value => value.GetString()).ToArray();
                if (ids.Length is < 1 or > 12 || ids.Any(string.IsNullOrWhiteSpace)) throw new CandidateAnalystUnavailableException();
                claims.Add(new CandidateAnalystClaim(text, Array.AsReadOnly(ids.Select(value => value!).ToArray())));
            }
            return (status ?? string.Empty, answer, Array.AsReadOnly(claims.ToArray()));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new CandidateAnalystUnavailableException();
        }
    }

    private static string ExtractJsonObject(string content)
    {
        var candidate = content.Trim();
        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = candidate.IndexOf('\n');
            if (firstLine >= 0) candidate = candidate[(firstLine + 1)..];
            var closingFence = candidate.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0) candidate = candidate[..closingFence];
            candidate = candidate.Trim();
        }

        if (!candidate.StartsWith('{'))
        {
            var objectStart = candidate.IndexOf('{');
            var objectEnd = candidate.LastIndexOf('}');
            if (objectStart < 0 || objectEnd <= objectStart) throw new CandidateAnalystUnavailableException();
            candidate = candidate[objectStart..(objectEnd + 1)];
        }
        return candidate;
    }

    private static bool HasRequiredProperties(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object) return false;
        var properties = element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        return names.All(properties.Contains);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length && length > maximumBytes)
            throw new CandidateAnalystUnavailableException();
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new CandidateAnalystUnavailableException();
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public void Dispose() => _client.Dispose();
}

public sealed class DisabledCandidateMatchAnswerGenerator : ICandidateMatchAnswerGenerator
{
    public Task<(string Status, string Answer, IReadOnlyList<CandidateAnalystClaim> Claims)> GenerateAsync(
        CandidateAnalystQuestion question, string sourceHash, IReadOnlyList<CandidateKafkaMessage> evidence,
        CancellationToken cancellationToken) => throw new CandidateAnalystUnavailableException();
}
