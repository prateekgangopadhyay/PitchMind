namespace PitchMind.Api;

/// <summary>Requires the API's local-only runtime profile for local workstation execution.</summary>
public static class LocalRuntimeProfileGuard
{
    public static void RequireLocalDevelopmentProfile(string environmentName, string? runtimeProfile)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The local workstation API requires the Development environment and Runtime:Profile=local.");
        }
    }
}
