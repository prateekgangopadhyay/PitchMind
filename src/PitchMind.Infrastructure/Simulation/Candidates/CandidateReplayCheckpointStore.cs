using System.Text.Json;

namespace PitchMind.Infrastructure.Simulation.Candidates;

/// <summary>
/// Stores the last consumer-acknowledged cursor for a local, noncanonical candidate replay.
/// Replays remain at-least-once across a crash between consumer work and checkpoint write.
/// </summary>
public sealed class CandidateReplayCheckpointStore
{
    private const int CurrentSchemaVersion = 1;
    private const int MaximumCheckpointBytes = 4_096;
    private readonly string _path;

    public CandidateReplayCheckpointStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    /// <summary>Loads a cursor only when it is pinned to the exact candidate feed bytes.</summary>
    public async Task<CandidateReplayCheckpoint?> ReadAsync(
        CandidateSourceFeed feed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        EnsureCandidate(feed);
        if (new FileInfo(_path).LinkTarget is not null)
            throw new InvalidDataException("Candidate replay checkpoint must not be a symbolic link.");

        FileStream input;
        try
        {
            input = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4_096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        byte[] bytes;
        await using (input.ConfigureAwait(false))
        {
            // Inspect the opened object where the platform exposes handle attributes;
            // this avoids treating a path-only check as proof of the object we read.
            if ((File.GetAttributes(input.SafeFileHandle) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Candidate replay checkpoint must not be a reparse point.");

            bytes = await ReadBoundedCheckpointBytesAsync(input, cancellationToken).ConfigureAwait(false);
        }

        CandidateReplayCheckpoint checkpoint;
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
            EnsureUniqueProperties(document.RootElement);
            checkpoint = document.RootElement.Deserialize<CandidateReplayCheckpoint>()
                ?? throw new InvalidDataException("Candidate replay checkpoint is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Candidate replay checkpoint is malformed JSON.", exception);
        }

        Validate(checkpoint, feed);
        return checkpoint;
    }

    /// <summary>
    /// Atomically persists a cursor after the caller has finished processing the currently yielded event.
    /// A crash before this call can replay that event again; consumers must be idempotent.
    /// </summary>
    public async Task<CandidateReplayCheckpoint> WriteAcknowledgedAsync(
        CandidateSourceFeed feed,
        CandidateReplaySession session,
        long acknowledgedSourceSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feed);
        ArgumentNullException.ThrowIfNull(session);
        EnsureCandidate(feed);
        if (!string.Equals(session.SourceSha256, feed.SourceSha256, StringComparison.Ordinal)
            || !string.Equals(session.FixtureId, feed.FixtureId, StringComparison.Ordinal))
            throw new InvalidOperationException("Replay session does not belong to the supplied candidate feed.");
        if (session.LastEmittedSourceSequence != acknowledgedSourceSequence)
            throw new InvalidOperationException("Only the currently emitted source sequence can be acknowledged.");

        var checkpoint = new CandidateReplayCheckpoint(
            CurrentSchemaVersion,
            feed.FixtureId,
            feed.SourceSha256,
            acknowledgedSourceSequence,
            checked(acknowledgedSourceSequence + 1));
        Validate(checkpoint, feed);

        var directory = Path.GetDirectoryName(_path)!;
        EnsureNoLink(directory);
        Directory.CreateDirectory(directory);
        EnsureNoLink(directory);
        if (File.Exists(_path))
            EnsureRegularFile(_path);

        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(checkpoint);
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4_096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        return checkpoint;
    }

    private static void EnsureCandidate(CandidateSourceFeed feed)
    {
        if (feed.IsCanonical || feed.Disposition != "candidate_not_canonical")
            throw new InvalidOperationException("Replay checkpoints are limited to noncanonical candidate feeds.");
    }

    internal static Task<byte[]> ReadBoundedCheckpointBytesAsync(
        Stream input,
        CancellationToken cancellationToken)
    {
        return CandidateSourceFeedReader.ReadBoundedBytesAsync(
            input,
            MaximumCheckpointBytes,
            "Candidate replay checkpoint",
            cancellationToken);
    }

    private static void Validate(CandidateReplayCheckpoint checkpoint, CandidateSourceFeed feed)
    {
        var first = feed.Events.Count == 0 ? 0 : feed.Events[0].SourceSequence;
        var last = feed.Events.Count == 0 ? 0 : feed.Events[^1].SourceSequence;
        if (checkpoint.SchemaVersion != CurrentSchemaVersion
            || !string.Equals(checkpoint.FixtureId, feed.FixtureId, StringComparison.Ordinal)
            || !string.Equals(checkpoint.SourceSha256, feed.SourceSha256, StringComparison.Ordinal)
            || checkpoint.LastAcknowledgedSourceSequence < first
            || checkpoint.LastAcknowledgedSourceSequence > last
            || checkpoint.NextSourceSequence != checkpoint.LastAcknowledgedSourceSequence + 1)
            throw new InvalidDataException("Candidate replay checkpoint does not match the feed or has an invalid cursor.");
    }

    private static void EnsureUniqueProperties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Candidate replay checkpoint root must be an object.");

        var allowed = new HashSet<string>(
            ["SchemaVersion", "FixtureId", "SourceSha256", "LastAcknowledgedSourceSequence", "NextSourceSequence"],
            StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new InvalidDataException("Candidate replay checkpoint contains duplicate properties.");
            if (!allowed.Contains(property.Name))
                throw new InvalidDataException("Candidate replay checkpoint contains an unsupported property.");
        }
        if (!names.SetEquals(allowed))
            throw new InvalidDataException("Candidate replay checkpoint is missing required properties.");
    }

    private static void EnsureRegularFile(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is not null)
            throw new InvalidDataException("Candidate replay checkpoint must not be a symbolic link.");
    }

    private static void EnsureNoLink(string path)
    {
        var info = new DirectoryInfo(path);
        if (info.LinkTarget is not null)
            throw new InvalidDataException("Candidate replay checkpoint directory must not be a symbolic link.");
    }
}

public sealed record CandidateReplayCheckpoint(
    int SchemaVersion,
    string FixtureId,
    string SourceSha256,
    long LastAcknowledgedSourceSequence,
    long NextSourceSequence);
