using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using PitchMind.Domain.Events;

namespace PitchMind.Application.Matches;

/// <summary>A versioned, reviewable demonstration policy for the offline pressing-sequence detector.</summary>
public sealed record PressingSequencePolicy
{
    private const string DemonstrationResource = "PitchMind.Application.Matches.Policies.press-sequence-v1.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private static readonly Lazy<PressingSequencePolicy> Demonstration = new(LoadDemonstration);

    public string Version { get; init; } = string.Empty;
    public string ReviewStatus { get; init; } = string.Empty;
    public int TriggerToGoalWindowSeconds { get; init; }
    public int PressureToResponseWindowSeconds { get; init; }
    public int ResponseToRegainWindowSeconds { get; init; }
    public int RegainToGoalWindowSeconds { get; init; }
    public double MaximumPressureDistanceMetres { get; init; }
    public IReadOnlyList<string> RequiredActionSequence { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RequiredEventTypeSequence { get; init; } = Array.Empty<string>();
    public RequiredOutcomePolicy RequiredOutcomes { get; init; } = new();
    public IReadOnlyList<string> Conditions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Limitations { get; init; } = Array.Empty<string>();

    public static PressingSequencePolicy DemonstrationPolicy => Demonstration.Value;

    public static PressingSequencePolicy Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var policy = JsonSerializer.Deserialize<PressingSequencePolicy>(json, JsonOptions)
                ?? throw new InvalidOperationException("Pressing-sequence policy is empty.");
            return policy.ValidateAndFreeze();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Pressing-sequence policy JSON is invalid.", exception);
        }
    }

    public PressingSequencePolicy Validate() => ValidateAndFreeze();

    private static PressingSequencePolicy LoadDemonstration()
    {
        using var stream = typeof(PressingSequencePolicy).Assembly.GetManifestResourceStream(DemonstrationResource)
            ?? throw new InvalidOperationException($"Embedded policy '{DemonstrationResource}' was not found.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private PressingSequencePolicy ValidateAndFreeze()
    {
        if (string.IsNullOrWhiteSpace(Version) || ReviewStatus != "demonstration-pending-review")
            throw new InvalidOperationException("Policy version is required and this implementation only accepts an explicitly pending demonstration policy.");
        if (TriggerToGoalWindowSeconds <= 0 || PressureToResponseWindowSeconds <= 0 ||
            ResponseToRegainWindowSeconds <= 0 || RegainToGoalWindowSeconds <= 0 ||
            !double.IsFinite(MaximumPressureDistanceMetres) || MaximumPressureDistanceMetres <= 0)
            throw new InvalidOperationException("Policy time and distance thresholds must be finite positive values.");
        if (RequiredActionSequence is null || RequiredEventTypeSequence is null ||
            RequiredActionSequence.Count != 7 || RequiredEventTypeSequence.Count != 7 ||
            RequiredActionSequence.Any(string.IsNullOrWhiteSpace) ||
            RequiredActionSequence.Distinct(StringComparer.Ordinal).Count() != RequiredActionSequence.Count ||
            RequiredEventTypeSequence.Any(type => !Enum.TryParse<MatchEventType>(type, ignoreCase: false, out var parsed) || !Enum.IsDefined(parsed)))
            throw new InvalidOperationException("Policy must define seven actions and seven supported event types in order.");
        if (RequiredOutcomes is null || new[]
            { RequiredOutcomes.Trigger, RequiredOutcomes.Response, RequiredOutcomes.Regain, RequiredOutcomes.Goal }
            .Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Policy outcome requirements must be non-empty.");
        if (Conditions is null || Limitations is null || Conditions.Count == 0 || Conditions.Any(string.IsNullOrWhiteSpace) ||
            Limitations.Count == 0 || Limitations.Any(string.IsNullOrWhiteSpace) ||
            !Conditions.Any(condition => condition.Contains("{distance}", StringComparison.Ordinal)) ||
            !Conditions.Any(condition => condition.Contains("{responseWindow}", StringComparison.Ordinal)) ||
            !Conditions.Any(condition => condition.Contains("{regainWindow}", StringComparison.Ordinal)) ||
            !Conditions.Any(condition => condition.Contains("{outcomeWindow}", StringComparison.Ordinal)))
            throw new InvalidOperationException("Policy must include readable conditions and limitations.");

        return this with
        {
            RequiredActionSequence = Array.AsReadOnly(RequiredActionSequence.ToArray()),
            RequiredEventTypeSequence = Array.AsReadOnly(RequiredEventTypeSequence.ToArray()),
            Conditions = Array.AsReadOnly(Conditions.ToArray()),
            Limitations = Array.AsReadOnly(Limitations.ToArray())
        };
    }
}

public sealed record RequiredOutcomePolicy
{
    public string Trigger { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
    public string Regain { get; init; } = string.Empty;
    public string Goal { get; init; } = string.Empty;
}
