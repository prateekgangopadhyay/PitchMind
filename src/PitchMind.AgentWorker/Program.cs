using PitchMind.AgentWorker;
using PitchMind.AgentWorker.Foundry;
using PitchMind.Application.MatchIntelligence;
using System.Globalization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var governance = new ExplanationGovernanceOptions(
    MaximumQuestionCharacters: ReadLimit("Foundry:Policy:MaximumQuestionCharacters", 2_000),
    MaximumInputBytes: ReadLimit("Foundry:Policy:MaximumInputBytes", 40_000),
    MaximumInputTokens: ReadLimit("Foundry:Policy:MaximumInputTokens", 16_384),
    MaximumOutputTokens: ReadLimit("Foundry:Policy:MaximumOutputTokens", 512),
    MaximumConcurrentCalls: ReadLimit("Foundry:Policy:MaximumConcurrentCalls", 4),
    MaximumCallsPerWindow: ReadLimit("Foundry:Policy:MaximumCallsPerWindow", 12),
    WindowSeconds: ReadLimit("Foundry:Policy:WindowSeconds", 60),
    CallTimeoutSeconds: ReadLimit("Foundry:Policy:CallTimeoutSeconds", 5),
    CircuitFailureThreshold: ReadLimit("Foundry:Policy:CircuitFailureThreshold", 3),
    CircuitOpenSeconds: ReadLimit("Foundry:Policy:CircuitOpenSeconds", 30),
    CacheCapacity: ReadLimit("Foundry:Policy:CacheCapacity", 256),
    CacheTtlSeconds: ReadLimit("Foundry:Policy:CacheTtlSeconds", 30));
governance.Validate();
builder.Services.AddSingleton(governance);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IExplanationCandidateGenerator, UnavailableExplanationCandidateGenerator>();
builder.Services.AddSingleton<IExplanationResponseCache>(_ => new BoundedExplanationResponseCache(governance.CacheCapacity));
builder.Services.AddSingleton<SlidingExplanationCallBudget>();
builder.Services.AddSingleton(_ => new SemaphoreSlim(governance.MaximumConcurrentCalls, governance.MaximumConcurrentCalls));
builder.Services.AddSingleton<ExplanationCircuitBreaker>();
builder.Services.AddSingleton<ExplanationCandidateVerifier>();
builder.Services.AddSingleton<DeterministicExplanationFallback>();
builder.Services.AddSingleton<ExplanationResiliencePolicy>();
if (string.Equals(builder.Configuration["Runtime:Profile"], "azure", StringComparison.OrdinalIgnoreCase))
{
    var endpointValue = builder.Configuration["Foundry:Endpoint"];
    var deployment = builder.Configuration["Foundry:Deployment"];
    if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) || string.IsNullOrWhiteSpace(deployment))
        throw new InvalidOperationException("Azure AgentWorker requires Foundry:Endpoint and Foundry:Deployment configuration.");

    var options = new FoundryAdapterOptions(endpoint, deployment,
        MaximumRequestBytes: ReadLimit("Foundry:MaximumRequestBytes", 40_960),
        MaximumResponseBytes: ReadLimit("Foundry:MaximumResponseBytes", 16_384),
        MaximumOutputTokens: ReadLimit("Foundry:MaximumOutputTokens", 512),
        TimeoutSeconds: ReadLimit("Foundry:TimeoutSeconds", 15),
        MaximumConcurrency: ReadLimit("Foundry:MaximumConcurrency", 4));
    options.Validate();
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<TokenCredential>(_ => new ManagedIdentityCredential());
    builder.Services.AddSingleton<IFoundryAccessTokenProvider, ManagedIdentityFoundryAccessTokenProvider>();
    builder.Services.AddSingleton(_ => new HttpClient(FoundryHttpClientFactory.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan });
    builder.Services.AddSingleton<FoundryExplanationAdapter>();
    builder.Services.AddSingleton<IExplanationCandidateGenerator>(services =>
        services.GetRequiredService<FoundryExplanationAdapter>());
}
builder.Services.AddHostedService<Worker>();
await builder.Build().RunAsync();

int ReadLimit(string key, int defaultValue)
{
    var value = builder.Configuration[key];
    if (value is null) return defaultValue;
    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)) return parsed;
    throw new InvalidOperationException($"{key} must be an integer.");
}
