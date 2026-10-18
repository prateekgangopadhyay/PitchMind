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
    var configuredMode = builder.Configuration["Foundry:Mode"];
    var hasProjectEndpoint = Uri.TryCreate(endpointValue, UriKind.Absolute, out var configuredEndpoint) &&
                             configuredEndpoint.AbsolutePath.StartsWith("/api/projects/", StringComparison.Ordinal);
    var useProjectAgent = string.Equals(configuredMode, "ProjectAgent", StringComparison.OrdinalIgnoreCase) ||
                          (string.IsNullOrWhiteSpace(configuredMode) && hasProjectEndpoint);
    if (!string.IsNullOrWhiteSpace(configuredMode) &&
        !string.Equals(configuredMode, "ProjectAgent", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(configuredMode, "ModelDeployment", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Foundry:Mode must be ProjectAgent or ModelDeployment.");
    var agentName = builder.Configuration["Foundry:AgentName"] ?? "TacticalAnalyst";
    if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) || (!useProjectAgent && string.IsNullOrWhiteSpace(deployment)))
        throw new InvalidOperationException("Azure AgentWorker requires Foundry:Endpoint and either Foundry:Deployment or ProjectAgent mode with Foundry:AgentName.");
    var knowledgeEndpointValue = builder.Configuration["KnowledgeSearch:Endpoint"];
    var knowledgeIndex = builder.Configuration["KnowledgeSearch:Index"];
    var semanticConfiguration = builder.Configuration["KnowledgeSearch:SemanticConfiguration"];
    var searchSettingsCount = new[] { knowledgeEndpointValue, knowledgeIndex, semanticConfiguration }
        .Count(value => !string.IsNullOrWhiteSpace(value));
    if (searchSettingsCount is > 0 and < 3 ||
        (searchSettingsCount == 3 && !Uri.TryCreate(knowledgeEndpointValue, UriKind.Absolute, out _)))
        throw new InvalidOperationException("Azure tactical Knowledge Search requires a valid endpoint, index and semantic configuration together.");
    if (!useProjectAgent && searchSettingsCount != 3)
        throw new InvalidOperationException("Model-deployment mode requires the curated tactical Knowledge Search endpoint, index and semantic configuration.");

    var options = new FoundryAdapterOptions(endpoint, deployment ?? string.Empty,
        MaximumRequestBytes: ReadLimit("Foundry:MaximumRequestBytes", 40_960),
        MaximumResponseBytes: ReadLimit("Foundry:MaximumResponseBytes", 16_384),
        MaximumOutputTokens: ReadLimit("Foundry:MaximumOutputTokens", 512),
        TimeoutSeconds: ReadLimit("Foundry:TimeoutSeconds", 15),
        MaximumConcurrency: ReadLimit("Foundry:MaximumConcurrency", 4),
        UseProjectAgentEndpoint: useProjectAgent,
        AgentName: agentName);
    options.Validate();
    builder.Services.AddSingleton(options);
    builder.Services.AddSingleton<TokenCredential>(_ => new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()));
    builder.Services.AddSingleton<IFoundryAccessTokenProvider>(services => new ManagedIdentityFoundryAccessTokenProvider(
        services.GetRequiredService<TokenCredential>(), useProjectAgent ? "https://ai.azure.com/.default" : "https://cognitiveservices.azure.com/.default"));
    if (searchSettingsCount == 3)
    {
        var knowledgeEndpoint = new Uri(knowledgeEndpointValue!, UriKind.Absolute);
        builder.Services.AddSingleton<IAzureAiSearchTokenProvider, ManagedIdentityAzureAiSearchTokenProvider>();
        var searchOptions = new AzureAiSearchTacticalKnowledgeOptions(knowledgeEndpoint, knowledgeIndex!, semanticConfiguration!,
            Top: ReadLimit("KnowledgeSearch:Top", 3), TimeoutSeconds: ReadLimit("KnowledgeSearch:TimeoutSeconds", 3),
            MaximumResponseBytes: ReadLimit("KnowledgeSearch:MaximumResponseBytes", 65_536));
        searchOptions.Validate();
        builder.Services.AddSingleton(searchOptions);
        builder.Services.AddSingleton<ITacticalKnowledgeRetriever, AzureAiSearchTacticalKnowledgeRetriever>();
    }
    else
    {
        // Project-agent mode may use the agent's configured Foundry File Search knowledge source.
        // Embedded cards remain advisory and cannot contribute canonical match evidence.
        builder.Services.AddSingleton<ITacticalKnowledgeRetriever, LocalTacticalKnowledgeRetriever>();
    }
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
