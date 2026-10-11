using System.Text.Json;
using PitchMind.Contracts.V1;

namespace PitchMind.Api;

/// <summary>Projects a committed ProjectionPublishedV1 outbox payload into a cursor-only browser message.</summary>
public static class RealtimeCursorTranslator
{
    private const int MaximumPayloadCharacters = 65_536;
    private static readonly HashSet<string> RequiredProperties = new(StringComparer.Ordinal)
    {
        "messageId", "matchId", "revision", "sequence", "clockSeconds",
        "algorithmVersion", "sourceSha256", "stateSha256", "state"
    };

    public static RealtimeCursorV1 FromProjectionPublishedV1(Guid outboxMessageId, string payloadJson,
        DateTimeOffset serverUtc)
    {
        if (outboxMessageId == Guid.Empty) throw new ArgumentException("Outbox message ID is required.", nameof(outboxMessageId));
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
        var revision = ReadInt32(values["revision"], "revision");
        var sequence = ReadInt64(values["sequence"], "sequence");
        _ = ReadInt32(values["clockSeconds"], "clockSeconds");
        _ = ReadNonEmptyString(values["algorithmVersion"], "algorithmVersion");
        ValidateSha256(ReadNonEmptyString(values["sourceSha256"], "sourceSha256"), "sourceSha256");
        ValidateSha256(ReadNonEmptyString(values["stateSha256"], "stateSha256"), "stateSha256");
        if (values["state"].ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Projection outbox state must be an object.");
        if (messageId != outboxMessageId || matchId == Guid.Empty || revision < 1 || sequence < 0)
            throw new InvalidDataException("Projection outbox identity or cursor is invalid.");
        if (serverUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Cursor server time must be UTC.", nameof(serverUtc));

        return new RealtimeCursorV1("matchUpdated", matchId.ToString("D"), revision, sequence, serverUtc, 1);
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

    private static void ValidateSha256(string value, string name)
    {
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"Projection outbox {name} is invalid.");
    }
}
