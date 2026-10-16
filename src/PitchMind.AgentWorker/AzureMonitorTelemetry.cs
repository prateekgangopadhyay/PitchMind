using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;

namespace PitchMind.AgentWorker;

internal static class AzureMonitorTelemetry
{
    public static void Configure(IHostApplicationBuilder builder, string? runtimeProfile)
    {
        var connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        var azureProfile = string.Equals(runtimeProfile, "azure", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            if (azureProfile)
                throw new InvalidOperationException("Azure AgentWorker requires APPLICATIONINSIGHTS_CONNECTION_STRING for telemetry export.");
            return;
        }

        if (connectionString.Length > 2_048 || connectionString.Any(char.IsControl))
            throw new InvalidOperationException("APPLICATIONINSIGHTS_CONNECTION_STRING is malformed.");
        if (!azureProfile)
            throw new InvalidOperationException("Application Insights export is permitted only in the Azure runtime profile.");

        TokenCredential credential = new ManagedIdentityCredential(new ManagedIdentityCredentialOptions());
        builder.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("PitchMind.AgentWorker"))
            .WithMetrics(metrics => metrics
                .AddMeter("PitchMind.Explanations")
                .AddAzureMonitorMetricExporter(options => options.ConnectionString = connectionString, credential))
            .WithTracing(tracing => tracing
                .AddSource(PitchMind.Application.MatchIntelligence.ExplanationTelemetry.InstrumentationName)
                .AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString, credential));
        builder.Logging.AddOpenTelemetry(logging =>
            logging.AddAzureMonitorLogExporter(options => options.ConnectionString = connectionString, credential));
    }
}
