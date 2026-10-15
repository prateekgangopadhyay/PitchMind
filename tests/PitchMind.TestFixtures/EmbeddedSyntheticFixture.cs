namespace PitchMind.TestFixtures;

/// <summary>Opens the versioned synthetic fixture shipped with test support only.</summary>
public static class EmbeddedSyntheticFixture
{
    public const string ResourceName = "PitchMind.TestFixtures.Simulation.synthetic-fixture-v2.json";

    public static Stream OpenRead()
    {
        return typeof(EmbeddedSyntheticFixture).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded test fixture '{ResourceName}' was not found.");
    }
}
