using System.Reflection;
using System.Text.Json;

namespace PitchMind.AgentWorker.Agents;

public sealed record TacticalAgentProfile(
    string Id,
    string Name,
    string TacticalSchool,
    string SystemInstruction,
    IReadOnlyList<string> Principles,
    IReadOnlyList<string> DiagnosticQuestions,
    IReadOnlyList<string> EvidenceRequirements,
    IReadOnlyList<string> BlindSpots,
    IReadOnlyList<string> ProhibitedInferences)
{
    public string BuildPromptContext() => string.Join("\n", new[]
    {
        $"Agent: {Name}. Tactical school: {TacticalSchool}.",
        $"Role: {SystemInstruction}",
        Format("Principles", Principles),
        Format("Diagnostic questions", DiagnosticQuestions),
        Format("Evidence requirements", EvidenceRequirements),
        Format("Known blind spots", BlindSpots),
        Format("Prohibited inferences", ProhibitedInferences)
    });

    private static string Format(string heading, IReadOnlyList<string> values) =>
        $"{heading}: {string.Join("; ", values)}";
}

/// <summary>Versioned, read-only tactical lenses. Profiles never own or alter canonical match facts.</summary>
public static class TacticalAgentCatalog
{
    public const string DefaultProfileId = "evidence-coach";
    private const string ResourceSuffix = ".Agents.tactical-agents.v1.json";
    private static readonly IReadOnlyDictionary<string, TacticalAgentProfile> Profiles = Load();

    public static IReadOnlyCollection<TacticalAgentProfile> All => Array.AsReadOnly(Profiles.Values.ToArray());

    public static bool TryGet(string? id, out TacticalAgentProfile profile)
    {
        profile = default!;
        return id is not null && Profiles.TryGetValue(id, out profile!);
    }

    private static IReadOnlyDictionary<string, TacticalAgentProfile> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        if (resourceName is null)
            throw new InvalidOperationException("The versioned tactical agent catalog resource is missing.");

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("The versioned tactical agent catalog could not be opened.");
        var document = JsonSerializer.Deserialize<CatalogDocument>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("The tactical agent catalog is empty.");
        if (document.SchemaVersion != "1.0.0" || document.Profiles is null || document.Profiles.Count is < 2 or > 12)
            throw new InvalidDataException("Tactical agent catalog metadata is invalid.");

        var profiles = new Dictionary<string, TacticalAgentProfile>(StringComparer.Ordinal);
        foreach (var profile in document.Profiles)
        {
            if (profile is null || !IsSafeId(profile.Id) || string.IsNullOrWhiteSpace(profile.Name) ||
                string.IsNullOrWhiteSpace(profile.TacticalSchool) || string.IsNullOrWhiteSpace(profile.SystemInstruction) ||
                profile.SystemInstruction.Length > 2_000 || !HasBoundedValues(profile.Principles, 10, 500) ||
                !HasBoundedValues(profile.DiagnosticQuestions, 10, 500) ||
                !HasBoundedValues(profile.EvidenceRequirements, 10, 500) ||
                !HasBoundedValues(profile.BlindSpots, 10, 500) ||
                !HasBoundedValues(profile.ProhibitedInferences, 10, 500) ||
                profile.BuildPromptContext().Length > 8_000 ||
                !profiles.TryAdd(profile.Id, Freeze(profile)))
                throw new InvalidDataException("Tactical agent catalog contains an invalid or duplicate profile.");
        }

        if (!profiles.ContainsKey(DefaultProfileId))
            throw new InvalidDataException("Tactical agent catalog must include the default evidence coach.");
        return profiles;
    }

    private static bool IsSafeId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');

    private static bool HasBoundedValues(IReadOnlyList<string>? values, int maxCount, int maxLength) =>
        values is { Count: > 0 } && values.Count <= maxCount &&
        values.All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl));

    private static TacticalAgentProfile Freeze(TacticalAgentProfile profile) => profile with
    {
        Principles = Array.AsReadOnly(profile.Principles.ToArray()),
        DiagnosticQuestions = Array.AsReadOnly(profile.DiagnosticQuestions.ToArray()),
        EvidenceRequirements = Array.AsReadOnly(profile.EvidenceRequirements.ToArray()),
        BlindSpots = Array.AsReadOnly(profile.BlindSpots.ToArray()),
        ProhibitedInferences = Array.AsReadOnly(profile.ProhibitedInferences.ToArray())
    };

    private sealed record CatalogDocument(string SchemaVersion, IReadOnlyList<TacticalAgentProfile> Profiles);
}
