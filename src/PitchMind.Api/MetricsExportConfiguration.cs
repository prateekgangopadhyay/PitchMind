using OpenTelemetry.Metrics;

namespace PitchMind.Api;

internal static class MetricsExportConfiguration
{
    public static bool AddConfiguredOtlpMetricsExporter(IServiceCollection services, IConfiguration configuration,
        string environmentName, string? runtimeProfile)
    {
        var endpointValue = configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"] ??
                            configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpointValue))
            return false;

        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            endpoint.UserInfo.Length > 0 || endpoint.Query.Length > 0 || endpoint.Fragment.Length > 0)
            throw new InvalidOperationException("The configured OTLP metrics endpoint must be an absolute HTTP(S) URI without credentials, query, or fragment.");

        var isExplicitLocal = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase);
        if (endpoint.Scheme == Uri.UriSchemeHttp && !isExplicitLocal)
            throw new InvalidOperationException("The configured OTLP metrics endpoint must use HTTPS outside the explicit Development/local profile.");

        services.AddOpenTelemetry().WithMetrics(metrics => metrics
            .AddMeter(ApiRequestTelemetry.InstrumentationName)
            .AddMeter("PitchMind.Infrastructure.Outbox")
            .AddOtlpExporter());
        return true;
    }
}
