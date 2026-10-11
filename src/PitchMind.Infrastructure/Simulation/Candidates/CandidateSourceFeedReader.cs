using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PitchMind.Infrastructure.Simulation.Candidates;

/// <summary>
/// Loads a manifest-pinned candidate JSON file for local inspection and deterministic replay.
/// This adapter deliberately does not map fields to canonical football contracts.
/// </summary>
public sealed class CandidateSourceFeedReader
{
    private const int MaximumSourceBytes = 50_000_000;
    private const int MaximumManifestBytes = 262_144;
    private const int MaximumEvents = 100_000;

    public async Task<CandidateSourceFeed> ReadAsync(
        string sourcePath,
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var manifestBytes = await ReadBoundedBytesAsync(
            manifestPath,
            MaximumManifestBytes,
            "Candidate manifest",
            cancellationToken);
        using var manifestDocument = JsonDocument.Parse(manifestBytes);
        RejectDuplicateProperties(manifestDocument.RootElement, "manifest");
        var manifest = ReadManifest(manifestDocument.RootElement);

        var sourceBytes = await ReadBoundedBytesAsync(
            sourcePath,
            MaximumSourceBytes,
            "Candidate source",
            cancellationToken);

        var actualSha256 = Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
        if (sourceBytes.LongLength != manifest.ByteSize || actualSha256 != manifest.Sha256)
            throw new InvalidDataException("Candidate source bytes do not match the manifest-pinned size and SHA-256.");

        using var sourceDocument = JsonDocument.Parse(sourceBytes);
        var root = sourceDocument.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0 || root.GetArrayLength() > MaximumEvents)
            throw new InvalidDataException("Candidate source must be a non-empty JSON array within the event limit.");
        if (root.GetArrayLength() != manifest.ExpectedEventCount)
            throw new InvalidDataException("Candidate event count differs from the manifest.");

        var events = new List<CandidateSourceEnvelope>(root.GetArrayLength());
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        var matchIds = new HashSet<string>(StringComparer.Ordinal);
        var teamIds = new HashSet<string>(StringComparer.Ordinal);
        var eventTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var previousSequence = manifest.SequenceStart - 1;
        long? previousClockSeconds = null;
        foreach (var sourceEvent in root.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceEvent.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Every candidate event must be a JSON object.");
            RejectDuplicateProperties(sourceEvent, "candidate event");

            var eventId = RequiredString(sourceEvent, "eventId");
            var matchId = RequiredString(sourceEvent, "matchId");
            var eventType = RequiredString(sourceEvent, "eventType");
            var rawSequence = RequiredIntegerToken(sourceEvent, "sequence");
            var rawClock = RequiredIntegerToken(sourceEvent, "clockSeconds");
            var rawPeriod = RequiredIntegerToken(sourceEvent, "period");
            if (!long.TryParse(rawClock, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var clockSeconds)
                || !int.TryParse(rawPeriod, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var period))
                throw new InvalidDataException("Candidate clockSeconds and period must remain integer source tokens.");

            if (!long.TryParse(rawSequence, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
                || sequence != previousSequence + 1)
                throw new InvalidDataException("Candidate source sequence must be contiguous and retain its manifest numbering.");

            // Validate against source-declared bounds while preserving the exact source tokens.
            // Do not convert them into canonical match time, periods, event enums, or facts.
            if (clockSeconds < manifest.SourceClockStart || clockSeconds > manifest.SourceClockEnd)
                throw new InvalidDataException("Candidate clockSeconds falls outside the manifest-declared source clock bounds.");
            if (previousClockSeconds is not null && clockSeconds < previousClockSeconds.Value)
                throw new InvalidDataException("Candidate source clockSeconds must be nondecreasing in source sequence order.");
            if (!manifest.ExpectedPeriods.Contains(period))
                throw new InvalidDataException("Candidate period is not listed in the manifest-declared periods.");
            if (!eventIds.Add(eventId))
                throw new InvalidDataException("Candidate source event IDs must be unique.");
            matchIds.Add(matchId);
            teamIds.Add(RequiredString(sourceEvent, "teamId"));
            eventTypeCounts[eventType] = eventTypeCounts.GetValueOrDefault(eventType) + 1;

            events.Add(new CandidateSourceEnvelope(
                eventId,
                matchId,
                sequence,
                rawSequence,
                rawClock,
                rawPeriod,
                eventType,
                sourceEvent.GetRawText()));
            previousSequence = sequence;
            previousClockSeconds = clockSeconds;
        }

        if (previousSequence != manifest.SequenceEnd)
            throw new InvalidDataException("Candidate source sequence end differs from the manifest.");
        if (matchIds.Count != manifest.ExpectedMatchCount)
            throw new InvalidDataException("Candidate match IDs do not match the manifest-declared match count.");
        if (teamIds.Count != manifest.ExpectedTeamCount)
            throw new InvalidDataException("Candidate team IDs do not match the manifest-declared team count.");
        foreach (var requiredCount in manifest.RequiredEventCounts)
            if (eventTypeCounts.GetValueOrDefault(requiredCount.Key) != requiredCount.Value)
                throw new InvalidDataException($"Candidate event count for '{requiredCount.Key}' differs from the manifest.");
        foreach (var marker in manifest.RequiredEventMarkers)
            if (!events.Any(item => item.EventType == marker.Key
                && long.TryParse(item.RawClockSeconds, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var markerClock)
                && markerClock == marker.Value.ClockSeconds
                && int.TryParse(item.RawPeriod, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var markerPeriod)
                && markerPeriod == marker.Value.Period))
                throw new InvalidDataException($"Candidate required marker '{marker.Key}' does not match its manifest-declared clock and period.");

        return new CandidateSourceFeed(
            manifest.FixtureId,
            manifest.Provenance,
            manifest.RedistributionPermission,
            actualSha256,
            sourceBytes.LongLength,
            events.AsReadOnly());
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(
        string path,
        int maximumBytes,
        string description,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ReadBoundedBytesAsync(input, maximumBytes, description, cancellationToken);
    }

    internal static async Task<byte[]> ReadBoundedBytesAsync(
        Stream input,
        int maximumBytes,
        string description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (!input.CanRead || !input.CanSeek)
            throw new ArgumentException("Candidate input must be a readable, seekable stream with a bounded initial length.", nameof(input));

        if (input.Length <= 0 || input.Length > maximumBytes)
            throw new InvalidDataException($"{description} is empty or exceeds the bounded reader size.");

        using var output = new MemoryStream((int)Math.Min(input.Length, maximumBytes));
        var buffer = new byte[81_920];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Read at most one byte past the cap so growth after the initial length
            // check is detected before any oversized allocation or write occurs.
            var remainingWithOverflowByte = maximumBytes - (int)output.Length + 1;
            var count = await input.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remainingWithOverflowByte)),
                cancellationToken);
            if (count == 0) break;
            if (output.Length + count > maximumBytes)
                throw new InvalidDataException($"{description} is empty or exceeds the bounded reader size.");

            var requiredCapacity = checked((int)(output.Length + count));
            if (requiredCapacity > output.Capacity)
            {
                // MemoryStream grows geometrically by default and can allocate beyond the
                // configured byte limit. Grow explicitly with the same hard cap instead.
                var doubledCapacity = output.Capacity == 0 ? 1 : output.Capacity * 2;
                output.Capacity = Math.Min(maximumBytes, Math.Max(requiredCapacity, doubledCapacity));
            }
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
        }

        if (output.Length == 0)
            throw new InvalidDataException($"{description} is empty or exceeds the bounded reader size.");
        return output.ToArray();
    }

    private static CandidateManifest ReadManifest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || RequiredString(root, "manifestVersion") != "1.0.0"
            || RequiredString(root, "status") != "candidate_not_canonical")
            throw new InvalidDataException("Manifest must explicitly classify the fixture as candidate_not_canonical.");

        var source = RequiredObject(root, "source");
        if (!RequiredBoolean(source, "synthetic"))
            throw new InvalidDataException("Manifest must preserve the source's owner-confirmed synthetic provenance declaration.");
        var provenance = RequiredString(source, "provenance");
        var redistributionPermission = RequiredString(source, "redistributionPermission");
        if (provenance != "repository-owner-confirmed" || redistributionPermission != "repository-owner-confirmed")
            throw new InvalidDataException("Manifest must retain the repository-owner-confirmed provenance and redistribution declaration.");

        var coordinates = RequiredObject(root, "coordinates");
        if (RequiredString(coordinates, "status") != "unverified"
            || RequiredNullableString(coordinates, "unit") is not null
            || RequiredNullableString(coordinates, "axisConvention") is not null
            || RequiredNull(coordinates, "pitchDimensionsMetres") is false)
            throw new InvalidDataException("Candidate coordinate semantics must remain explicitly unverified.");

        var playerMetrics = RequiredObject(root, "playerMetrics");
        if (RequiredString(playerMetrics, "status") != "unvalidated"
            || RequiredString(playerMetrics, "unitsAndValidity") != "unverified"
            || RequiredBoolean(playerMetrics, "mayBePresentedAsObservedPerformance"))
            throw new InvalidDataException("Candidate player metrics must remain unvalidated and not presentable as observed performance.");

        var canonicalization = RequiredObject(root, "canonicalization");
        if (RequiredString(canonicalization, "mappingStatus") != "not_approved"
            || RequiredBoolean(canonicalization, "mayCreateCanonicalEvents"))
            throw new InvalidDataException("Candidate source mappings must remain unapproved and unable to create canonical events.");

        var sha256 = RequiredString(source, "sha256");
        if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character) || char.IsUpper(character)))
            throw new InvalidDataException("Manifest source sha256 must be lowercase hexadecimal.");

        var byteSize = RequiredInt64(source, "byteSize");
        var expectedEventCount = RequiredInt32(source, "expectedEventCount");
        var sequenceStart = RequiredInt64(source, "sequenceStart");
        var sequenceEnd = RequiredInt64(source, "sequenceEnd");
        if (byteSize <= 0 || expectedEventCount <= 0 || sequenceStart <= 0 || sequenceEnd < sequenceStart
            || sequenceEnd - sequenceStart + 1 != expectedEventCount)
            throw new InvalidDataException("Manifest byte, event-count, and source-sequence bounds must be positive, ordered, and consistent.");

        var match = RequiredObject(root, "match");
        var expectedMatchCount = RequiredInt32(match, "expectedMatchCount");
        var expectedTeamCount = RequiredInt32(match, "expectedTeamCount");
        var sourceClockStart = RequiredInt64(match, "sourceClockStart");
        var sourceClockEnd = RequiredInt64(match, "sourceClockEnd");
        if (expectedMatchCount <= 0 || expectedTeamCount <= 0 || sourceClockStart < 0 || sourceClockEnd < sourceClockStart)
            throw new InvalidDataException("Manifest match and source-clock bounds must be positive and ordered.");

        if (!match.TryGetProperty("expectedPeriods", out var periodsElement) || periodsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Manifest expectedPeriods must be an array.");
        var expectedPeriods = new HashSet<int>();
        foreach (var item in periodsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number
                || !int.TryParse(item.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out var period)
                || !expectedPeriods.Add(period))
                throw new InvalidDataException("Manifest expectedPeriods must contain unique integer source periods.");
        }
        if (expectedPeriods.Count == 0)
            throw new InvalidDataException("Manifest expectedPeriods must not be empty.");

        var requiredEventCounts = ReadCountMap(RequiredObject(match, "requiredEventCounts"));
        var requiredEventMarkers = ReadMarkerMap(RequiredObject(match, "requiredEventMarkers"));
        if (requiredEventMarkers.Any(marker => marker.Value.ClockSeconds < sourceClockStart
            || marker.Value.ClockSeconds > sourceClockEnd
            || !expectedPeriods.Contains(marker.Value.Period)))
            throw new InvalidDataException("Manifest event markers must remain within declared clock and period bounds.");

        return new CandidateManifest(
            RequiredString(root, "fixtureId"),
            provenance,
            redistributionPermission,
            sha256,
            byteSize,
            expectedEventCount,
            sequenceStart,
            sequenceEnd,
            expectedMatchCount,
            expectedTeamCount,
            sourceClockStart,
            sourceClockEnd,
            expectedPeriods,
            requiredEventCounts,
            requiredEventMarkers);
    }

    private static IReadOnlyDictionary<string, int> ReadCountMap(JsonElement counts)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in counts.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.Number
                || !int.TryParse(property.Value.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                || count <= 0)
                throw new InvalidDataException("Manifest event counts must contain positive integer counts keyed by event type.");
            result.Add(property.Name, count);
        }
        return result;
    }

    private static IReadOnlyDictionary<string, CandidateEventMarker> ReadMarkerMap(JsonElement markers)
    {
        var result = new Dictionary<string, CandidateEventMarker>(StringComparer.Ordinal);
        foreach (var property in markers.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Manifest event markers must be objects keyed by event type.");
            var clockSeconds = RequiredInt64(property.Value, "clockSeconds");
            var period = RequiredInt32(property.Value, "period");
            if (clockSeconds < 0 || period < 0)
                throw new InvalidDataException("Manifest event marker clock and period must be non-negative.");
            result.Add(property.Name, new CandidateEventMarker(clockSeconds, period));
        }
        return result;
    }

    private static void RejectDuplicateProperties(JsonElement element, string description)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException($"Duplicate property in {description} is not accepted.");
                RejectDuplicateProperties(property.Value, description);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                RejectDuplicateProperties(item, description);
        }
    }

    private static string RequiredString(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Required string property '{name}' is missing or invalid.");
        var value = property.GetString();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Required string property '{name}' is blank.");
    }

    private static string? RequiredNullableString(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var property))
            throw new InvalidDataException($"Required property '{name}' is missing.");
        if (property.ValueKind == JsonValueKind.Null)
            return null;
        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : throw new InvalidDataException($"Property '{name}' must be null or a string.");
    }

    private static JsonElement RequiredObject(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Required object property '{name}' is missing or invalid.");
        return property;
    }

    private static string RequiredIntegerToken(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"Required integer token '{name}' is missing or invalid.");
        var raw = property.GetRawText();
        return long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
            ? raw
            : throw new InvalidDataException($"Required integer token '{name}' is not an integer.");
    }

    private static long RequiredInt64(JsonElement owner, string name)
    {
        var token = RequiredIntegerToken(owner, name);
        return long.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static int RequiredInt32(JsonElement owner, string name)
    {
        var token = RequiredIntegerToken(owner, name);
        return int.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    private static bool RequiredBoolean(JsonElement owner, string name)
    {
        if (!owner.TryGetProperty(name, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Required boolean property '{name}' is missing or invalid.");
        return property.GetBoolean();
    }

    private static bool RequiredNull(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Null;

    private sealed record CandidateManifest(
        string FixtureId,
        string Provenance,
        string RedistributionPermission,
        string Sha256,
        long ByteSize,
        int ExpectedEventCount,
        long SequenceStart,
        long SequenceEnd,
        int ExpectedMatchCount,
        int ExpectedTeamCount,
        long SourceClockStart,
        long SourceClockEnd,
        IReadOnlySet<int> ExpectedPeriods,
        IReadOnlyDictionary<string, int> RequiredEventCounts,
        IReadOnlyDictionary<string, CandidateEventMarker> RequiredEventMarkers);

    private sealed record CandidateEventMarker(long ClockSeconds, int Period);
}
