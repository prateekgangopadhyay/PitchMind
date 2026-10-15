using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PitchMind.Application.MatchIntelligence;

public enum ExplanationExecutionSource
{
    VerifiedCandidate,
    CachedVerifiedCandidate,
    DeterministicFallback
}

public enum ExplanationFallbackReason
{
    CandidateUnavailable,
    CandidateRejected,
    CallTimedOut,
    CircuitOpen,
    RequestBudgetExhausted,
    ConcurrencyLimited,
    RequestTooLarge,
    InvalidRequest
}

public sealed record ExplanationExecutionResult(
    VerifiedExplanation? Explanation,
    ExplanationExecutionSource Source,
    ExplanationFallbackReason? FallbackReason,
    int? InputTokens,
    int? OutputTokens)
{
    public bool Succeeded => Explanation is not null;
}

public sealed record ExplanationGovernanceOptions(
    int MaximumQuestionCharacters = 2_000,
    int MaximumInputBytes = 40_000,
    int MaximumInputTokens = 16_384,
    int MaximumOutputTokens = 512,
    int MaximumConcurrentCalls = 4,
    int MaximumCallsPerWindow = 12,
    int WindowSeconds = 60,
    int CallTimeoutSeconds = 5,
    int CircuitFailureThreshold = 3,
    int CircuitOpenSeconds = 30,
    int CacheCapacity = 256,
    int CacheTtlSeconds = 30)
{
    public void Validate()
    {
        if (MaximumQuestionCharacters is < 1 or > 8_000 || MaximumInputBytes is < 1_024 or > 40_960 ||
            MaximumInputTokens is < 1 or > 32_768 || MaximumOutputTokens is < 1 or > 2_048 ||
            MaximumConcurrentCalls is < 1 or > 32 ||
            MaximumCallsPerWindow is < 1 or > 120 || WindowSeconds is < 1 or > 3_600 ||
            CallTimeoutSeconds is < 1 or > 60 || CircuitFailureThreshold is < 1 or > 20 ||
            CircuitOpenSeconds is < 1 or > 600 || CacheCapacity is < 1 or > 10_000 ||
            CacheTtlSeconds is < 1 or > 3_600)
            throw new ArgumentOutOfRangeException(nameof(ExplanationGovernanceOptions), "Explanation governance limits are outside supported bounds.");
    }
}

public interface IExplanationResponseCache
{
    bool TryGet(string key, DateTimeOffset now, out VerifiedExplanation? explanation);
    void Set(string key, VerifiedExplanation explanation, DateTimeOffset now, TimeSpan lifetime);
}

/// <summary>A bounded process-local cache; keys include the authorized scope and exact selected packet identity.</summary>
public sealed class BoundedExplanationResponseCache(int capacity) : IExplanationResponseCache
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public bool TryGet(string key, DateTimeOffset now, out VerifiedExplanation? explanation)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
            {
                explanation = entry.Explanation;
                return true;
            }

            _entries.Remove(key);
            explanation = null;
            return false;
        }
    }

    public void Set(string key, VerifiedExplanation explanation, DateTimeOffset now, TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(explanation);
        if (lifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(lifetime));
        lock (_gate)
        {
            foreach (var expired in _entries.Where(pair => pair.Value.ExpiresAt <= now).Select(pair => pair.Key).ToArray())
                _entries.Remove(expired);

            if (!_entries.ContainsKey(key) && _entries.Count >= capacity)
            {
                var oldest = _entries.MinBy(pair => pair.Value.ExpiresAt).Key;
                _entries.Remove(oldest);
            }

            _entries[key] = new(explanation, now.Add(lifetime));
        }
    }

    private sealed record CacheEntry(VerifiedExplanation Explanation, DateTimeOffset ExpiresAt);
}

/// <summary>Sliding process-local request budget. A failed provider attempt still consumes budget.</summary>
public sealed class SlidingExplanationCallBudget
{
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _calls = new();

    public bool TryAcquire(DateTimeOffset now, TimeSpan window, int maximumCalls, out DateTimeOffset retryAt)
    {
        lock (_gate)
        {
            var cutoff = now - window;
            while (_calls.Count > 0 && _calls.Peek() <= cutoff) _calls.Dequeue();
            if (_calls.Count >= maximumCalls)
            {
                retryAt = _calls.Peek().Add(window);
                return false;
            }

            _calls.Enqueue(now);
            retryAt = DateTimeOffset.MinValue;
            return true;
        }
    }
}

/// <summary>One half-open probe is admitted after the open interval; state is local to this worker process.</summary>
public sealed class ExplanationCircuitBreaker(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;
    private bool _probeInFlight;
    private long _generation;

    public bool TryEnter(out long permitGeneration)
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            if (_openUntil is null)
            {
                permitGeneration = _generation;
                return true;
            }

            if (_openUntil > now || _probeInFlight)
            {
                permitGeneration = -1;
                return false;
            }

            _probeInFlight = true;
            permitGeneration = ++_generation;
            return true;
        }
    }

    public void RecordSuccess(long permitGeneration)
    {
        lock (_gate)
        {
            if (permitGeneration != _generation) return;
            _consecutiveFailures = 0;
            _openUntil = null;
            _probeInFlight = false;
        }
    }

    public void RecordFailure(long permitGeneration, int threshold, TimeSpan openDuration)
    {
        lock (_gate)
        {
            if (permitGeneration != _generation) return;
            _consecutiveFailures++;
            if (_probeInFlight || _consecutiveFailures >= threshold)
            {
                _openUntil = timeProvider.GetUtcNow().Add(openDuration);
                _generation++;
            }
            _probeInFlight = false;
        }
    }

    public void AbandonPermit(long permitGeneration)
    {
        lock (_gate)
        {
            if (permitGeneration == _generation) _probeInFlight = false;
        }
    }
}

public sealed class DeterministicExplanationFallback
{
    private const string PolicyVersion = "explanation-fallback-v1";
    private static readonly string[] Limitations =
    [
        "This fallback uses only the selected canonical score, insight classification, and cited evidence metadata.",
        "Evidence references do not establish causation or coach intent.",
        "Open the cited canonical evidence to inspect the recorded actions."
    ];

    public ExplanationExecutionResult Create(
        MatchIntelligenceContextPacket? context,
        ExplanationAudience audience,
        ExplanationFallbackReason reason)
    {
        if (!IsUsable(context, audience))
            return new(null, ExplanationExecutionSource.DeterministicFallback, ExplanationFallbackReason.InvalidRequest, null, null);

        var selected = context!;
        var ids = selected.Evidence.Select(item => item.Id).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var kinds = selected.Evidence.Select(item => item.Kind).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var time = TimeSpan.FromSeconds(selected.SelectedTimeSeconds);
        var clock = $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
        var category = FormatCategory(selected.Claim.Category);
        var text = audience switch
        {
            ExplanationAudience.Fan => $"At {clock}, the recorded score is {selected.Score.Home}–{selected.Score.Away}. " +
                                       $"The selected insight is classified as {category}, with {ids.Length} canonical " +
                                       $"evidence item(s) ({string.Join("/", kinds)}). The evidence is available to inspect below.",
            ExplanationAudience.Analyst => $"At {clock} on canonical revision {selected.CanonicalRevision}, the recorded score is " +
                                           $"{selected.Score.Home}–{selected.Score.Away}; the selected insight is classified as " +
                                           $"{category} and references {ids.Length} canonical evidence item(s) ({string.Join("/", kinds)}).",
            _ => throw new ArgumentOutOfRangeException(nameof(audience))
        };
        var claim = new VerifiedExplanationClaim(text, Array.AsReadOnly(ids));
        var explanation = new VerifiedExplanation(text, Array.AsReadOnly(new[] { claim }),
            Array.AsReadOnly(Limitations.ToArray()), selected.MatchId, selected.CanonicalRevision,
            selected.SelectedTimeSeconds, audience, "deterministic-fallback",
            ExplanationVerificationDisposition.EvidenceOnlyReformulation, PolicyVersion);
        return new(explanation, ExplanationExecutionSource.DeterministicFallback, reason, null, null);
    }

    internal static bool IsUsable(MatchIntelligenceContextPacket? context, ExplanationAudience audience) =>
        context is not null && Enum.IsDefined(audience) &&
        !string.IsNullOrWhiteSpace(context.MatchId) && context.MatchId.Length <= 128 &&
        !context.MatchId.Any(char.IsControl) && context.CanonicalRevision > 0 &&
        context.SelectedTimeSeconds is >= 0 and <= 5400 &&
        context.ProjectionAsOfTimeSeconds is >= 0 && context.ProjectionAsOfTimeSeconds <= context.SelectedTimeSeconds &&
        context.ThroughSequence >= 0 && context.Score is { Home: >= 0, Away: >= 0 } &&
        context.Claim is not null && Enum.IsDefined(context.Claim.Category) &&
        context.Evidence is { Count: > 0 and <= 12 } &&
        context.Evidence.All(item => item is not null && (item.Kind is "event" or "frame") &&
            !string.IsNullOrWhiteSpace(item.Id) && item.Id.Length <= 256 && !item.Id.Any(char.IsControl) &&
            item.Sequence >= 0 && item.Sequence <= context.ThroughSequence &&
            item.ClockSeconds >= 0 && item.ClockSeconds <= context.SelectedTimeSeconds &&
            item.Period is >= 1 and <= 5 && !string.IsNullOrWhiteSpace(item.PayloadJson) &&
            Encoding.UTF8.GetByteCount(item.PayloadJson) <= 6_144 &&
            !string.IsNullOrWhiteSpace(item.TrustBoundary) && item.TrustBoundary.Length <= 256 &&
            !string.IsNullOrWhiteSpace(item.Relation) && item.Relation.Length <= 128) &&
        context.Evidence.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() == context.Evidence.Count;

    private static string FormatCategory(MatchIntelligenceClaimCategory category) => category switch
    {
        MatchIntelligenceClaimCategory.ObservedFact => "an observed fact",
        MatchIntelligenceClaimCategory.DerivedFact => "a derived fact",
        MatchIntelligenceClaimCategory.EvidenceSupportedPattern => "an evidence-supported pattern",
        MatchIntelligenceClaimCategory.SupportedInterpretation => "a supported interpretation",
        MatchIntelligenceClaimCategory.Hypothesis => "a hypothesis",
        MatchIntelligenceClaimCategory.Simulated => "a simulated claim",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };
}

/// <summary>Turns candidate generation into a bounded, fail-safe explanation path without coupling live updates to AI.</summary>
public sealed class ExplanationResiliencePolicy(
    IExplanationCandidateGenerator generator,
    ExplanationCandidateVerifier verifier,
    DeterministicExplanationFallback fallback,
    IExplanationResponseCache cache,
    SlidingExplanationCallBudget callBudget,
    SemaphoreSlim concurrency,
    ExplanationCircuitBreaker circuitBreaker,
    ExplanationGovernanceOptions options,
    TimeProvider timeProvider)
{
    private static readonly Meter Meter = new("PitchMind.Explanations", "1.0.0");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("pitchmind.explanation.requests");
    private static readonly Counter<long> Fallbacks = Meter.CreateCounter<long>("pitchmind.explanation.fallbacks");
    private static readonly Counter<long> TokenUsage = Meter.CreateCounter<long>("pitchmind.explanation.tokens");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("pitchmind.explanation.duration", "ms");

    public async Task<ExplanationExecutionResult> ExecuteAsync(
        string authorizationScope,
        MatchIntelligenceContextPacket? context,
        ExplanationAudience audience,
        string? question,
        CancellationToken cancellationToken = default)
    {
        options.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (!DeterministicExplanationFallback.IsUsable(context, audience))
            return RecordFallback(context, audience, ExplanationFallbackReason.InvalidRequest);

        if (string.IsNullOrWhiteSpace(authorizationScope) || authorizationScope.Length > 256 ||
            string.IsNullOrWhiteSpace(question) ||
            question.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')))
            return RecordFallback(context, audience, ExplanationFallbackReason.InvalidRequest);

        if (question.Length > options.MaximumQuestionCharacters)
            return RecordFallback(context, audience, ExplanationFallbackReason.RequestTooLarge);

        var contextBytes = JsonSerializer.SerializeToUtf8Bytes(context).Length;
        var inputBytes = contextBytes + Encoding.UTF8.GetByteCount(question);
        if (inputBytes > options.MaximumInputBytes)
            return RecordFallback(context, audience, ExplanationFallbackReason.RequestTooLarge);

        var now = timeProvider.GetUtcNow();
        var key = CacheKey(authorizationScope, context!, audience, question);
        if (cache.TryGet(key, now, out var cached) && cached is not null)
            return new(cached, ExplanationExecutionSource.CachedVerifiedCandidate, null, null, null);

        if (!circuitBreaker.TryEnter(out var circuitPermit))
            return RecordFallback(context, audience, ExplanationFallbackReason.CircuitOpen);

        if (!concurrency.Wait(0))
        {
            circuitBreaker.AbandonPermit(circuitPermit);
            return RecordFallback(context, audience, ExplanationFallbackReason.ConcurrencyLimited);
        }

        if (!callBudget.TryAcquire(now, TimeSpan.FromSeconds(options.WindowSeconds), options.MaximumCallsPerWindow, out _))
        {
            concurrency.Release();
            circuitBreaker.AbandonPermit(circuitPermit);
            return RecordFallback(context, audience, ExplanationFallbackReason.RequestBudgetExhausted);
        }

        Requests.Add(1);
        var started = timeProvider.GetTimestamp();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.CallTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        Task<ExplanationGenerationResult>? generation = null;
        var releaseAfterGeneration = false;
        try
        {
            generation = generator.GenerateAsync(new(context!, audience, question, options.MaximumOutputTokens,
                options.MaximumInputTokens), linked.Token);
            var result = await generation.WaitAsync(linked.Token).ConfigureAwait(false);
            if (!result.Succeeded || result.Candidate is null)
            {
                circuitBreaker.RecordFailure(circuitPermit, options.CircuitFailureThreshold, TimeSpan.FromSeconds(options.CircuitOpenSeconds));
                return RecordFallback(context, audience, ExplanationFallbackReason.CandidateUnavailable);
            }

            var candidate = result.Candidate;
            if (candidate.InputTokens is int observedInputTokens && observedInputTokens >= 0 &&
                candidate.OutputTokens is int observedOutputTokens && observedOutputTokens >= 0)
                TokenUsage.Add((long)observedInputTokens + observedOutputTokens);
            if (candidate.InputTokens is not { } inputTokens || candidate.OutputTokens is not { } outputTokens ||
                inputTokens < 0 || inputTokens > options.MaximumInputTokens || outputTokens < 0 ||
                outputTokens > options.MaximumOutputTokens)
            {
                circuitBreaker.RecordFailure(circuitPermit, options.CircuitFailureThreshold, TimeSpan.FromSeconds(options.CircuitOpenSeconds));
                return RecordFallback(context, audience, ExplanationFallbackReason.CandidateRejected);
            }

            var verification = verifier.Verify(candidate, context, audience);
            if (!verification.Succeeded || verification.Explanation is null)
            {
                circuitBreaker.RecordFailure(circuitPermit, options.CircuitFailureThreshold, TimeSpan.FromSeconds(options.CircuitOpenSeconds));
                return RecordFallback(context, audience, ExplanationFallbackReason.CandidateRejected);
            }

            circuitBreaker.RecordSuccess(circuitPermit);
            cache.Set(key, verification.Explanation, timeProvider.GetUtcNow(), TimeSpan.FromSeconds(options.CacheTtlSeconds));
            return new(verification.Explanation, ExplanationExecutionSource.VerifiedCandidate, null, inputTokens, outputTokens);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeferConcurrencyReleaseIfProviderStillRunning();
            circuitBreaker.AbandonPermit(circuitPermit);
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            DeferConcurrencyReleaseIfProviderStillRunning();
            circuitBreaker.RecordFailure(circuitPermit, options.CircuitFailureThreshold, TimeSpan.FromSeconds(options.CircuitOpenSeconds));
            return RecordFallback(context, audience, ExplanationFallbackReason.CallTimedOut);
        }
        catch (OperationCanceledException)
        {
            DeferConcurrencyReleaseIfProviderStillRunning();
            circuitBreaker.RecordFailure(circuitPermit, options.CircuitFailureThreshold, TimeSpan.FromSeconds(options.CircuitOpenSeconds));
            return RecordFallback(context, audience, ExplanationFallbackReason.CandidateUnavailable);
        }
        catch (Exception)
        {
            DeferConcurrencyReleaseIfProviderStillRunning();
            circuitBreaker.RecordFailure(circuitPermit, options.CircuitFailureThreshold,
                TimeSpan.FromSeconds(options.CircuitOpenSeconds));
            return RecordFallback(context, audience, ExplanationFallbackReason.CandidateUnavailable);
        }
        finally
        {
            if (!releaseAfterGeneration) concurrency.Release();
            Duration.Record(timeProvider.GetElapsedTime(started).TotalMilliseconds);
        }

        void DeferConcurrencyReleaseIfProviderStillRunning()
        {
            if (generation is not { IsCompleted: false }) return;
            releaseAfterGeneration = true;
            _ = generation.ContinueWith(completed =>
            {
                _ = completed.Exception;
                concurrency.Release();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private ExplanationExecutionResult RecordFallback(
        MatchIntelligenceContextPacket? context,
        ExplanationAudience audience,
        ExplanationFallbackReason reason)
    {
        Fallbacks.Add(1, new KeyValuePair<string, object?>("reason", reason.ToString()));
        return fallback.Create(context, audience, reason);
    }

    private static string CacheKey(string scope, MatchIntelligenceContextPacket context, ExplanationAudience audience, string question)
    {
        var packetHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(context)));
        var input = $"{scope}\n{context.MatchId}\n{context.CanonicalRevision}\n{context.SelectedTimeSeconds}\n" +
                    $"{audience}\n{context.Claim.Id}\n{question}\n{packetHash}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}

public sealed class UnavailableExplanationCandidateGenerator : IExplanationCandidateGenerator
{
    public Task<ExplanationGenerationResult> GenerateAsync(
        ExplanationGenerationRequest request,
        CancellationToken cancellationToken) =>
        Task.FromResult(ExplanationGenerationResult.Refused(ExplanationGenerationFailure.Unavailable));
}
