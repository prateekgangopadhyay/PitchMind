using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitchMind.Domain.Simulation;

namespace PitchMind.TestFixtures;

/// <summary>
/// A deterministic serialization of an explicitly synthetic test fixture.
/// The hash covers the exact UTF-8 JSON bytes and is not a canonical-feed hash.
/// </summary>
public sealed record SyntheticFixtureArtifact(
    SyntheticFixture Fixture,
    string FormatVersion,
    string ContentSha256,
    string Json)
{
    public const string CurrentFormatVersion = "pitchmind-synthetic-fixture-json-v1";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static SyntheticFixtureArtifact Create(SyntheticFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var json = JsonSerializer.Serialize(fixture, SerializerOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new(fixture, CurrentFormatVersion, hash, json);
    }
}
