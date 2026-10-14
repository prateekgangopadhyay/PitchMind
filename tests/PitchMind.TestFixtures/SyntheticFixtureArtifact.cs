using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PitchMind.TestFixtures;

/// <summary>
/// A deterministic serialization of an explicitly synthetic test fixture.
/// The hash covers the exact UTF-8 JSON bytes and is not a canonical-feed hash.
/// </summary>
public sealed class SyntheticFixtureArtifact
{
    public const string CurrentFormatVersion = "pitchmind-synthetic-fixture-json-v1";

    private sealed record ArtifactPayload(string FormatVersion, GovernedSyntheticScenario Scenario);

    private SyntheticFixtureArtifact(GovernedSyntheticScenario scenario, string formatVersion, string contentSha256, string json)
    {
        Scenario = scenario;
        FormatVersion = formatVersion;
        ContentSha256 = contentSha256;
        Json = json;
    }

    public GovernedSyntheticScenario Scenario { get; }
    public string FormatVersion { get; }
    public string ContentSha256 { get; }
    public string Json { get; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static SyntheticFixtureArtifact Create(GovernedSyntheticScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var json = JsonSerializer.Serialize(new ArtifactPayload(CurrentFormatVersion, scenario), SerializerOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new(scenario, CurrentFormatVersion, hash, json);
    }
}
