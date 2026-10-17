using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Domain.Match;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Prewarms the noncanonical, revision-keyed cache from a committed PostgreSQL outbox row.</summary>
public sealed class ProjectionOutboxCachePublisher(
    IMatchProjectionCache cache,
    TimeSpan? timeToLive = null) : IOutboxPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(30);
    private static readonly HashSet<string> RequiredProperties = new(StringComparer.Ordinal)
    {
        "messageId", "matchId", "canonicalRevision", "projectionRevision", "sequence", "clockSeconds",
        "algorithmVersion", "sourceSha256", "stateSha256", "state"
    };

    public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
    {
        if (timeToLive.HasValue && timeToLive.Value <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeToLive));
        var envelope = Parse(messageId, messageJson);
        var state = envelope.State.Deserialize<MatchState>(JsonOptions)
            ?? throw new InvalidDataException("Projection outbox state is empty.");
        if (state.MatchId != envelope.MatchId.ToString() || state.LastSequence != envelope.Sequence ||
            state.ClockSeconds != envelope.ClockSeconds || state.ClockSeconds is < 0 or > 5400)
            throw new InvalidDataException("Projection outbox metadata does not match its state.");
        var stateJson = JsonSerializer.Serialize(state, JsonOptions);
        var stateHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stateJson)));
        if (!string.Equals(stateHash, envelope.StateSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Projection outbox state hash does not match its state.");

        var projection = new SelectedMatchProjection(state, envelope.CanonicalRevision,
            envelope.ProjectionRevision, envelope.Sequence, envelope.ClockSeconds,
            envelope.AlgorithmVersion, envelope.SourceSha256, envelope.StateSha256);
        var key = CachedMatchProjectionReader.BuildKey(new MatchId(envelope.MatchId),
            envelope.CanonicalRevision, envelope.ClockSeconds, envelope.AlgorithmVersion);
        var bytes = CachedMatchProjectionReader.SerializeCacheValue(projection);
        await cache.SetAsync(key, bytes, timeToLive ?? DefaultTimeToLive, cancellationToken).ConfigureAwait(false);
    }

    private static ProjectionOutboxEnvelope Parse(Guid expectedMessageId, string messageJson)
    {
        if (expectedMessageId == Guid.Empty || string.IsNullOrWhiteSpace(messageJson) || messageJson.Length > 65_536)
            throw new InvalidDataException("Projection outbox identity or payload size is invalid.");
        using var document = JsonDocument.Parse(messageJson, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Projection outbox payload must be a JSON object.");
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!properties.TryAdd(property.Name, property.Value))
                throw new InvalidDataException("Projection outbox payload contains duplicate properties.");
        if (!properties.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(RequiredProperties))
            throw new InvalidDataException("Projection outbox payload does not match ProjectionPublishedV1.");

        var message = ReadGuid(properties["messageId"], "messageId");
        var match = ReadGuid(properties["matchId"], "matchId");
        var revision = ReadInt64(properties["canonicalRevision"], "canonicalRevision");
        var projectionRevision = ReadInt32(properties["projectionRevision"], "projectionRevision");
        var sequence = ReadInt64(properties["sequence"], "sequence");
        var clockSeconds = ReadInt32(properties["clockSeconds"], "clockSeconds");
        var algorithm = ReadString(properties["algorithmVersion"], "algorithmVersion");
        var sourceHash = ReadHash(properties["sourceSha256"], "sourceSha256");
        var stateHash = ReadHash(properties["stateSha256"], "stateSha256");
        var state = properties["state"];
        if (message != expectedMessageId || match == Guid.Empty || revision < 1 || projectionRevision < 1 ||
            sequence < 0 || clockSeconds is < 0 or > 5400 || state.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Projection outbox identity or state is invalid.");
        return new ProjectionOutboxEnvelope(match, revision, projectionRevision, sequence, clockSeconds,
            algorithm, sourceHash, stateHash, state.Clone());
    }

    private static Guid ReadGuid(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed) && parsed != Guid.Empty
            ? parsed : throw new InvalidDataException($"Projection outbox {name} is invalid.");

    private static int ReadInt32(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed : throw new InvalidDataException($"Projection outbox {name} is invalid.");

    private static long ReadInt64(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed)
            ? parsed : throw new InvalidDataException($"Projection outbox {name} is invalid.");

    private static string ReadString(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new InvalidDataException($"Projection outbox {name} is invalid.");

    private static string ReadHash(JsonElement value, string name)
    {
        var hash = ReadString(value, name);
        if (hash.Length != 64 || hash.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"Projection outbox {name} is invalid.");
        return hash;
    }

    private sealed record ProjectionOutboxEnvelope(Guid MatchId, long CanonicalRevision, int ProjectionRevision,
        long Sequence, int ClockSeconds, string AlgorithmVersion, string SourceSha256, string StateSha256,
        JsonElement State);
}
