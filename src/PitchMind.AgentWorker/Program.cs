using PitchMind.AgentWorker;
using PitchMind.AgentWorker.Agents;
using PitchMind.AgentWorker.Foundry;
using System.Globalization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var runtimeProfile = builder.Configuration["Runtime:Profile"];
AzureMonitorTelemetry.Configure(builder, runtimeProfile);
if (string.Equals(runtimeProfile, "azure", StringComparison.OrdinalIgnoreCase))
{
    var endpointValue = builder.Configuration["Foundry:Endpoint"];
    var deployment = builder.Configuration["Foundry:Deployment"];
    if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) || string.IsNullOrWhiteSpace(deployment))
        throw new InvalidOperationException("Azure AgentWorker requires Foundry:Endpoint and Foundry:Deployment configuration.");
    var knowledgeEndpointValue = builder.Configuration["KnowledgeSearch:Endpoint"];
    var knowledgeIndex = builder.Configuration["KnowledgeSearch:Index"];
    var semanticConfiguration = builder.Configuration["KnowledgeSearch:SemanticConfiguration"];
    if (!Uri.TryCreate(knowledgeEndpointValue, UriKind.Absolute, out var knowledgeEndpoint) ||
        string.IsNullOrWhiteSpace(knowledgeIndex) || string.IsNullOrWhiteSpace(semanticConfiguration))
        throw new InvalidOperationException("Azure AgentWorker requires the curated tactical Knowledge Search endpoint, index and semantic configuration.");

    var options = new FoundryAdapterOptions(endpoint, deployment,
        MaximumRequestBytes: ReadLimit("Foundry:MaximumRequestBytes", 40_960),
        MaximumResponseBytes: ReadLimit("Foundry:MaximumResponseBytes", 16_384),
        MaximumOutputTokens: ReadLimit("Foundry:MaximumOutputTokens", 512),
        TimeoutSeconds: ReadLimit("Foundry:TimeoutSeconds", 15),
        MaximumConcurrency: ReadLimit("Foundry:MaximumConcurrency", 4));
    options.Validate();
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<TokenCredential>(_ => new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()));
    builder.Services.AddSingleton<IFoundryAccessTokenProvider, ManagedIdentityFoundryAccessTokenProvider>();
    builder.Services.AddSingleton<IAzureAiSearchTokenProvider, ManagedIdentityAzureAiSearchTokenProvider>();
    var searchOptions = new AzureAiSearchTacticalKnowledgeOptions(knowledgeEndpoint, knowledgeIndex, semanticConfiguration,
        Top: ReadLimit("KnowledgeSearch:Top", 3), TimeoutSeconds: ReadLimit("KnowledgeSearch:TimeoutSeconds", 3),
        MaximumResponseBytes: ReadLimit("KnowledgeSearch:MaximumResponseBytes", 65_536));
    searchOptions.Validate();
    builder.Services.AddSingleton(searchOptions);
    builder.Services.AddSingleton<ITacticalKnowledgeRetriever, AzureAiSearchTacticalKnowledgeRetriever>();
    builder.Services.AddSingleton(_ => new HttpClient(FoundryHttpClientFactory.CreateHandler()) { Timeout = Timeout.InfiniteTimeSpan });
    builder.Services.AddSingleton<FoundryExplanationAdapter>();
    builder.Services.AddSingleton<PitchMind.Application.MatchIntelligence.IExplanationCandidateGenerator>(services =>
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
