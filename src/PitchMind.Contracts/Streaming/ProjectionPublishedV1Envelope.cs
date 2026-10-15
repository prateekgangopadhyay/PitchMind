using System.Text.Json;

namespace PitchMind.Contracts.Streaming;

/// <summary>Validated committed-projection notification stored in the PostgreSQL outbox.</summary>
public sealed record ProjectionPublishedV1Envelope(
    Guid MessageId,
    Guid MatchId,
    long CanonicalRevision,
    int ProjectionRevision,
    long Sequence,
    int ClockSeconds,
    string AlgorithmVersion,
    string SourceSha256,
    string StateSha256,
    JsonElement State)
{
    private const int MaximumPayloadCharacters = 65_536;
    private static readonly HashSet<string> RequiredProperties = new(StringComparer.Ordinal)
    {
        "messageId", "matchId", "canonicalRevision", "projectionRevision", "sequence", "clockSeconds",
        "algorithmVersion", "sourceSha256", "stateSha256", "state"
    };

    public static ProjectionPublishedV1Envelope Parse(Guid expectedMessageId, string payloadJson)
    {
        if (expectedMessageId == Guid.Empty)
            throw new ArgumentException("Outbox message ID is required.", nameof(expectedMessageId));
        ArgumentNullException.ThrowIfNull(payloadJson);
        if (payloadJson.Length is 0 or > MaximumPayloadCharacters)
            throw new InvalidDataException("Projection outbox payload is outside the supported size.");

        using var document = JsonDocument.Parse(payloadJson, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Projection outbox payload must be a JSON object.");

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!values.TryAdd(property.Name, property.Value))
                throw new InvalidDataException("Projection outbox payload contains duplicate properties.");
        }
        if (!values.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(RequiredProperties))
            throw new InvalidDataException("Projection outbox payload does not match ProjectionPublishedV1.");

        var messageId = ReadGuid(values["messageId"], "messageId");
        var matchId = ReadGuid(values["matchId"], "matchId");
        var canonicalRevision = ReadInt64(values["canonicalRevision"], "canonicalRevision");
        var projectionRevision = ReadInt32(values["projectionRevision"], "projectionRevision");
        var sequence = ReadInt64(values["sequence"], "sequence");
        var clockSeconds = ReadInt32(values["clockSeconds"], "clockSeconds");
        var algorithmVersion = ReadNonEmptyString(values["algorithmVersion"], "algorithmVersion");
        var sourceSha256 = ReadSha256(values["sourceSha256"], "sourceSha256");
        var stateSha256 = ReadSha256(values["stateSha256"], "stateSha256");
        var state = values["state"];

        if (messageId != expectedMessageId || matchId == Guid.Empty || canonicalRevision < 1 ||
            projectionRevision < 1 || sequence < 0 || clockSeconds < 0 || state.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Projection outbox identity or state is invalid.");

        return new ProjectionPublishedV1Envelope(messageId, matchId, canonicalRevision, projectionRevision,
            sequence, clockSeconds, algorithmVersion, sourceSha256, stateSha256, state.Clone());
    }

    private static Guid ReadGuid(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed)
            ? parsed
            : throw new InvalidDataException($"Projection outbox {name} is invalid.");

    private static int ReadInt32(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : throw new InvalidDataException($"Projection outbox {name} is invalid.");

    private static long ReadInt64(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed)
            ? parsed
            : throw new InvalidDataException($"Projection outbox {name} is invalid.");

    private static string ReadNonEmptyString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new InvalidDataException($"Projection outbox {name} is invalid.");

    private static string ReadSha256(JsonElement value, string name)
    {
        var hash = ReadNonEmptyString(value, name);
        if (hash.Length != 64 || hash.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"Projection outbox {name} is invalid.");
        return hash;
    }
}
