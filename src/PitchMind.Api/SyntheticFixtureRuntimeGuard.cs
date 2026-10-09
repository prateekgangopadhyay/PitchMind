namespace PitchMind.Api;

/// <summary>Prevents the offline synthetic match fixture from being served by non-LOCAL API hosts.</summary>
public static class SyntheticFixtureRuntimeGuard
{
    public static void RequireLocalEnvironment(string environmentName)
    {
        if (!string.Equals(environmentName, "Local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The offline synthetic match API requires the resolved .NET environment name to be Local. " +
                "A canonical Azure read path is not available in this runtime.");
        }
    }
}
