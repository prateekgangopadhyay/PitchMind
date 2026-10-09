namespace PitchMind.Api;

/// <summary>Prevents the offline synthetic match fixture from being served outside the local Development profile.</summary>
public static class SyntheticFixtureRuntimeGuard
{
    public static void RequireLocalDevelopmentEnvironment(string environmentName, string? runtimeProfile)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(runtimeProfile, "local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The offline synthetic match API requires the Development environment and Runtime:Profile=local. " +
                "A canonical Azure read path is not available in this runtime.");
        }
    }
}
