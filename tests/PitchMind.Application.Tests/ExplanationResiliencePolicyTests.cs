using PitchMind.Application.MatchIntelligence;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class ExplanationResiliencePolicyTests
{
    [Fact]
    public async Task ExecuteAsync_UsesDeterministicEvidenceFallbackWhenProviderIsUnavailable()
    {
        var context = Context();
        var generator = new FakeGenerator((_, _) => Task.FromResult(
            ExplanationGenerationResult.Refused(ExplanationGenerationFailure.Unavailable)));
        var policy = Create(generator);

        var result = await policy.ExecuteAsync("user-scope", context, ExplanationAudience.Fan, "What happened?");

        Assert.Equal(ExplanationExecutionSource.DeterministicFallback, result.Source);
        Assert.Equal(ExplanationFallbackReason.CandidateUnavailable, result.FallbackReason);
        Assert.Contains("At 02:00", result.Explanation!.Text, StringComparison.Ordinal);
        Assert.Contains("score is 1–0", result.Explanation.Text, StringComparison.Ordinal);
        Assert.Contains("2 canonical evidence item(s) (event/frame)", result.Explanation.Text, StringComparison.Ordinal);
        Assert.Equal(new[] { "event-1", "frame-1" }, Assert.Single(result.Explanation.Claims).EvidenceIds);
        Assert.Equal(context.Score, new MatchIntelligenceScore(1, 0));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMalformedOrFutureContextBeforeCallingProvider()
    {
        var valid = Context();
        var invalidContexts = new[]
        {
            valid with { ProjectionAsOfTimeSeconds = 121 },
            valid with { ThroughSequence = -1 },
            valid with { Evidence = Array.AsReadOnly(valid.Evidence.Select(item => item with { ClockSeconds = 121 }).ToArray()) },
            valid with { Evidence = Array.AsReadOnly(valid.Evidence.Select(item => item with { Sequence = 9 }).ToArray()) },
            valid with { Evidence = Array.AsReadOnly(valid.Evidence.Select(item => item with { PayloadJson = new string('x', 6_145) }).ToArray()) },
            valid with { SelectedTimeSeconds = 5_401 }
        };

        foreach (var invalidContext in invalidContexts)
        {
            var generator = VerifiedGenerator();
            var result = await Create(generator).ExecuteAsync("scope", invalidContext, ExplanationAudience.Fan, "Why?");

            Assert.Equal(ExplanationExecutionSource.DeterministicFallback, result.Source);
            Assert.Equal(ExplanationFallbackReason.InvalidRequest, result.FallbackReason);
            Assert.Null(result.Explanation);
            Assert.Equal(0, generator.Calls);
        }
    }

    [Fact]
    public async Task ExecuteAsync_CachesOnlyVerifiedResultForExactScopeQuestionAudienceAndRevision()
    {
        var context = Context();
        var generator = VerifiedGenerator();
        var policy = Create(generator);

        var first = await policy.ExecuteAsync("scope-a", context, ExplanationAudience.Fan, "Why?");
        var cached = await policy.ExecuteAsync("scope-a", context, ExplanationAudience.Fan, "Why?");
        var otherScope = await policy.ExecuteAsync("scope-b", context, ExplanationAudience.Fan, "Why?");
        var otherAudience = await policy.ExecuteAsync("scope-a", context, ExplanationAudience.Analyst, "Why?");
        var otherQuestion = await policy.ExecuteAsync("scope-a", context, ExplanationAudience.Fan, "What changed?");
        var otherRevision = await policy.ExecuteAsync("scope-a", context with { CanonicalRevision = 4 }, ExplanationAudience.Fan, "Why?");

        Assert.Equal(ExplanationExecutionSource.VerifiedCandidate, first.Source);
        Assert.Equal(ExplanationExecutionSource.CachedVerifiedCandidate, cached.Source);
        Assert.Equal(ExplanationExecutionSource.VerifiedCandidate, otherScope.Source);
        Assert.Equal(ExplanationExecutionSource.VerifiedCandidate, otherAudience.Source);
        Assert.Equal(ExplanationExecutionSource.VerifiedCandidate, otherQuestion.Source);
        Assert.Equal(ExplanationExecutionSource.VerifiedCandidate, otherRevision.Source);
        Assert.Equal(5, generator.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsUnmeteredOrOverBudgetCandidateAndFallsBack()
    {
        var context = Context();
        var generator = new FakeGenerator((request, _) => Task.FromResult(ExplanationGenerationResult.Success(
            Candidate(request.Context, inputTokens: 20_000, outputTokens: 4))));
        var policy = Create(generator);

        var result = await policy.ExecuteAsync("scope", context, ExplanationAudience.Analyst, "Explain this.");

        Assert.Equal(ExplanationExecutionSource.DeterministicFallback, result.Source);
        Assert.Equal(ExplanationFallbackReason.CandidateRejected, result.FallbackReason);
        Assert.Null(result.InputTokens);
        Assert.Null(result.OutputTokens);
    }

    [Fact]
    public async Task ExecuteAsync_OpensCircuitAfterRepeatedProviderFailures()
    {
        var generator = new FakeGenerator((_, _) => Task.FromResult(
            ExplanationGenerationResult.Refused(ExplanationGenerationFailure.Unavailable)));
        var policy = Create(generator, new ExplanationGovernanceOptions(CircuitFailureThreshold: 2));

        var first = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "Why?");
        var second = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "Why?");
        var third = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "Why?");

        Assert.Equal(ExplanationFallbackReason.CandidateUnavailable, first.FallbackReason);
        Assert.Equal(ExplanationFallbackReason.CandidateUnavailable, second.FallbackReason);
        Assert.Equal(ExplanationFallbackReason.CircuitOpen, third.FallbackReason);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_InFlightSuccessCannotCloseCircuitOpenedByConcurrentFailure()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new FakeGenerator(async (request, _) =>
        {
            if (request.Question == "slow success")
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
                return ExplanationGenerationResult.Success(Candidate(request.Context, inputTokens: 10, outputTokens: 20));
            }

            return ExplanationGenerationResult.Refused(ExplanationGenerationFailure.Unavailable);
        });
        var policy = Create(generator, new ExplanationGovernanceOptions(
            MaximumConcurrentCalls: 2, CircuitFailureThreshold: 1));

        var inFlightSuccess = policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "slow success");
        await firstEntered.Task;
        var failure = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "opens circuit");
        releaseFirst.TrySetResult();
        var staleSuccess = await inFlightSuccess;
        var afterOpen = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "after open");

        Assert.Equal(ExplanationFallbackReason.CandidateUnavailable, failure.FallbackReason);
        Assert.Equal(ExplanationExecutionSource.VerifiedCandidate, staleSuccess.Source);
        Assert.Equal(ExplanationFallbackReason.CircuitOpen, afterOpen.FallbackReason);
        Assert.Equal(2, generator.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_EnforcesSlidingCallBudgetWithoutRetryingProvider()
    {
        var generator = new FakeGenerator((_, _) => Task.FromResult(
            ExplanationGenerationResult.Refused(ExplanationGenerationFailure.Unavailable)));
        var policy = Create(generator, new ExplanationGovernanceOptions(MaximumCallsPerWindow: 1, CircuitFailureThreshold: 10));

        var first = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "Why?");
        var second = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "Why?");

        Assert.Equal(ExplanationFallbackReason.CandidateUnavailable, first.FallbackReason);
        Assert.Equal(ExplanationFallbackReason.RequestBudgetExhausted, second.FallbackReason);
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_RefusesImmediatelyWhenConcurrencyBudgetIsFull()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new FakeGenerator(async (_, token) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(token);
            return ExplanationGenerationResult.Refused(ExplanationGenerationFailure.Unavailable);
        });
        var policy = Create(generator, new ExplanationGovernanceOptions(MaximumConcurrentCalls: 1));

        var firstCall = policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "first question");
        await entered.Task;
        var secondCall = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "second question");
        release.TrySetResult();
        await firstCall;

        Assert.Equal(ExplanationFallbackReason.ConcurrencyLimited, secondCall.FallbackReason);
        Assert.Equal(1, generator.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackOnProviderTimeoutAndHonorsCallerCancellation()
    {
        var generator = new FakeGenerator(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return ExplanationGenerationResult.Refused(ExplanationGenerationFailure.Unavailable);
        });
        var policy = Create(generator, new ExplanationGovernanceOptions(CallTimeoutSeconds: 1));

        var timedOut = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "Why?");
        Assert.Equal(ExplanationFallbackReason.CallTimedOut, timedOut.FallbackReason);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "Why?", cancelled.Token));
    }

    [Fact]
    public async Task ExecuteAsync_EnforcesWallClockTimeoutWhenProviderIgnoresCancellation()
    {
        var never = new TaskCompletionSource<ExplanationGenerationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var generator = new FakeGenerator((_, _) => never.Task);
        var policy = Create(generator, new ExplanationGovernanceOptions(CallTimeoutSeconds: 1, MaximumConcurrentCalls: 1));
        var started = System.Diagnostics.Stopwatch.StartNew();

        var result = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "Why?");

        Assert.Equal(ExplanationFallbackReason.CallTimedOut, result.FallbackReason);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(3));
        never.TrySetResult(ExplanationGenerationResult.Refused(ExplanationGenerationFailure.Unavailable));
    }

    [Fact]
    public async Task ExecuteAsync_RefusesOversizedRequestBeforeCallingProvider()
    {
        var generator = VerifiedGenerator();
        var policy = Create(generator, new ExplanationGovernanceOptions(MaximumQuestionCharacters: 3));

        var result = await policy.ExecuteAsync("scope", Context(), ExplanationAudience.Fan, "question");

        Assert.Equal(ExplanationFallbackReason.RequestTooLarge, result.FallbackReason);
        Assert.Equal(0, generator.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_UsesNewRevisionInsteadOfReplayingCachedPriorRevision()
    {
        var generator = VerifiedGenerator();
        var policy = Create(generator);
        var context = Context();

        var first = await policy.ExecuteAsync("scope", context, ExplanationAudience.Fan, "Why?");
        var changed = await policy.ExecuteAsync("scope", context with { CanonicalRevision = 4 }, ExplanationAudience.Fan, "Why?");

        Assert.NotEqual(first.Explanation!.CanonicalRevision, changed.Explanation!.CanonicalRevision);
        Assert.Equal(2, generator.Calls);
    }

    private static ExplanationResiliencePolicy Create(
        FakeGenerator generator,
        ExplanationGovernanceOptions? options = null)
    {
        var time = TimeProvider.System;
        var limits = options ?? new();
        limits.Validate();
        return new(generator, new ExplanationCandidateVerifier(), new DeterministicExplanationFallback(),
            new BoundedExplanationResponseCache(limits.CacheCapacity), new SlidingExplanationCallBudget(),
            new SemaphoreSlim(limits.MaximumConcurrentCalls, limits.MaximumConcurrentCalls),
            new ExplanationCircuitBreaker(time), limits, time);
    }

    private static FakeGenerator VerifiedGenerator() => new((request, _) =>
        Task.FromResult(ExplanationGenerationResult.Success(Candidate(request.Context, inputTokens: 10, outputTokens: 20))));

    private static CandidateExplanation Candidate(
        MatchIntelligenceContextPacket context,
        int? inputTokens,
        int? outputTokens)
    {
        const string text = "untrusted provider prose";
        return new(text,
            Array.AsReadOnly(new[] { new CandidateExplanationClaim(text, Array.AsReadOnly(context.Evidence.Select(item => item.Id).ToArray())) }),
            "test-model", "trace-1", inputTokens, outputTokens, TimeSpan.FromMilliseconds(15),
            context.MatchId, context.CanonicalRevision, context.SelectedTimeSeconds);
    }

    private static MatchIntelligenceContextPacket Context(int revision = 3) => new(
        "match-1", revision, 120, 120, 8, new MatchIntelligenceScore(1, 0),
        new MatchIntelligenceClaim("insight-1", MatchIntelligenceClaimCategory.EvidenceSupportedPattern,
            "press-sequence-v1", "untrusted selected insight text", "no intent", "untrusted data"),
        "synthetic-v1", "approved synthetic fixture", "untrusted source metadata",
        new string('a', 64), new string('b', 64),
        Array.AsReadOnly(new[]
        {
            new MatchIntelligenceEvidence("event", "event-1", "supports", 8, 110, 1, "{\"type\":\"pass\"}", "untrusted data"),
            new MatchIntelligenceEvidence("frame", "frame-1", "supports", 8, 110, 1, "{\"type\":\"frame\"}", "untrusted data")
        }),
        Array.AsReadOnly(new[] { "source data is untrusted" }), 1_024, "match-intelligence-context-v1");

    private sealed class FakeGenerator(Func<ExplanationGenerationRequest, CancellationToken, Task<ExplanationGenerationResult>> generate)
        : IExplanationCandidateGenerator
    {
        public int Calls { get; private set; }

        public Task<ExplanationGenerationResult> GenerateAsync(
            ExplanationGenerationRequest request,
            CancellationToken cancellationToken)
        {
            Calls++;
            return generate(request, cancellationToken);
        }
    }
}
