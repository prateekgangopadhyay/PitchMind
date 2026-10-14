using Microsoft.Extensions.DependencyInjection;
using PitchMind.Application.Abstractions;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.Api;

/// <summary>Validates that local-only adapters cannot be selected by a deployed runtime.</summary>
public static class RuntimeProfileGuard
{
    public static void Validate(string environmentName, string? runtimeProfile)
    {
        if (string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(runtimeProfile, "azure", StringComparison.OrdinalIgnoreCase) &&
            environmentName is not null &&
            (string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        throw new InvalidOperationException(
            "Use Runtime:Profile=local only in Development, or Runtime:Profile=azure in Development, Staging, or Production.");
    }

    public static void ConfigureLocalServices(IServiceCollection services, string? runtimeProfile)
    {
        if (string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IMatchEventStore, InMemoryMatchEventStore>();
    }
}
