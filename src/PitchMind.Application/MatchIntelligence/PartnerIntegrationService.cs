namespace PitchMind.Application.MatchIntelligence;

public sealed record PartnerProviderMetadata(
    string ProviderId,
    string DisplayName,
    string Category,
    string Status,
    string EndpointKind,
    bool IsActive,
    IReadOnlyList<string> Capabilities);

public sealed record PlatformCapabilitiesResponse(
    IReadOnlyList<PartnerProviderMetadata> Providers,
    string ActiveAiDeployment,
    string ActiveCacheAdapter,
    string RealtimeTransport,
    DateTimeOffset RetrievedAtUtc);

public interface IPartnerIntegrationService
{
    PlatformCapabilitiesResponse GetCapabilities();
}

public sealed class PartnerIntegrationService : IPartnerIntegrationService
{
    public PlatformCapabilitiesResponse GetCapabilities()
    {
        var providers = new List<PartnerProviderMetadata>
        {
            new(
                ProviderId: "azure-foundry-gpt4o",
                DisplayName: "Microsoft Azure AI Foundry",
                Category: "LLM Tactical Reasoning",
                Status: "Configured",
                EndpointKind: "Azure OpenAI / Foundry Chat",
                IsActive: true,
                Capabilities: ["EvidenceGroundedAnalysis", "MultiAgentHandoff", "DeterministicFallback"]),

            new(
                ProviderId: "azure-managed-redis",
                DisplayName: "Azure Managed Redis Enterprise",
                Category: "Cache-Aside Projections",
                Status: "Active",
                EndpointKind: "Redis Enterprise TLS",
                IsActive: true,
                Capabilities: ["SubMillisecondProjections", "TTLVersioning", "ZeroTruthLossOnFlush"]),

            new(
                ProviderId: "signalr-realtime",
                DisplayName: "Azure SignalR Realtime Service",
                Category: "Live Match Fan-Out",
                Status: "Active",
                EndpointKind: "WebSockets / SSE",
                IsActive: true,
                Capabilities: ["CursorWakeup", "BackfillSync", "AutoReconnect"]),

            new(
                ProviderId: "broadcast-partner-api",
                DisplayName: "Broadcaster & Partner API Gateway",
                Category: "Syndication & Export",
                Status: "Certified",
                EndpointKind: "REST / Signed JSON Artifacts",
                IsActive: true,
                Capabilities: ["SignedEvidenceExport", "RetentionGovernance", "AuditTraceability"])
        };

        return new PlatformCapabilitiesResponse(
            Providers: providers,
            ActiveAiDeployment: "gpt-4o",
            ActiveCacheAdapter: "AzureManagedRedis / In-Memory Cache-Aside",
            RealtimeTransport: "SignalR MatchUpdatesHub",
            RetrievedAtUtc: DateTimeOffset.UtcNow);
    }
}
