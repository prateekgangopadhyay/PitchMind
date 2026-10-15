using System.Text.Json;

namespace PitchMind.Infrastructure.Feeds;

public interface ICandidateFeedReplayCheckpointStore
{
    Task<CandidateFeedReplayCursor?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CandidateFeedReplayCursor cursor, CancellationToken cancellationToken = default);
}

public interface ICandidateFeedReplaySessionLeaseProvider
{
    ValueTask<IAsyncDisposable> AcquireSessionLeaseAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores only the last consumer-acknowledged raw-source cursor. The checkpoint is local
/// operational state and never represents canonical football state.
/// </summary>
public sealed class FileCandidateFeedReplayCheckpointStore :
    ICandidateFeedReplayCheckpointStore, ICandidateFeedReplaySessionLeaseProvider
{
    private const int CheckpointSchemaVersion = 1;
    private const int MaximumCheckpointBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directoryPath;
    private readonly string _checkpointPath;
    private readonly string _leasePath;

    public FileCandidateFeedReplayCheckpointStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
        _checkpointPath = Path.Combine(_directoryPath, "candidate-feed-replay-checkpoint.json");
        _leasePath = Path.Combine(_directoryPath, ".candidate-feed-replay.lock");
    }

    public ValueTask<IAsyncDisposable> AcquireSessionLeaseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDirectory();
        RejectReparseAncestors(_leasePath);
        try
        {
            var lease = new FileStream(_leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                1, FileOptions.WriteThrough);
            return ValueTask.FromResult<IAsyncDisposable>(new FileReplaySessionLease(lease));
        }
        catch (IOException exception)
        {
            throw new IOException("Another candidate replay session already owns this checkpoint.", exception);
        }
    }

    public async Task<CandidateFeedReplayCursor?> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        RejectReparseAncestors(_checkpointPath);
        if (!File.Exists(_checkpointPath)) return null;

        try
        {
            await using var input = new FileStream(_checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var bytes = await ReadBoundedAsync(input, cancellationToken).ConfigureAwait(false);
            await using var boundedInput = new MemoryStream(bytes, writable: false);
            var stored = await JsonSerializer.DeserializeAsync<StoredCheckpoint>(boundedInput, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (stored is null || stored.SchemaVersion != CheckpointSchemaVersion || stored.Cursor is null)
                throw new InvalidDataException("Candidate replay checkpoint schema is invalid.");
            ValidateCursor(stored.Cursor);
            return stored.Cursor;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Candidate replay checkpoint is corrupt.", exception);
        }
    }

    public async Task SaveAsync(CandidateFeedReplayCursor cursor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ValidateCursor(cursor);
        EnsureDirectory();
        RejectReparseAncestors(_checkpointPath);

        var temporaryPath = Path.Combine(_directoryPath, $".candidate-feed-checkpoint-{Guid.NewGuid():N}.tmp");
        try
        {
            var stored = new StoredCheckpoint(CheckpointSchemaVersion, cursor);
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(output, stored, JsonOptions, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            RejectReparseAncestors(_checkpointPath);
            File.Move(temporaryPath, _checkpointPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void EnsureDirectory()
    {
        RejectReparseAncestors(_directoryPath);
        Directory.CreateDirectory(_directoryPath);
        RejectReparseAncestors(_directoryPath);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream(MaximumCheckpointBytes);
        var buffer = new byte[4096];
        while (true)
        {
            var requested = Math.Min(buffer.Length, MaximumCheckpointBytes + 1 - (int)output.Length);
            var read = await input.ReadAsync(buffer.AsMemory(0, requested), cancellationToken).ConfigureAwait(false);
            if (read == 0) return output.ToArray();
            if (output.Length + read > MaximumCheckpointBytes)
                throw new InvalidDataException("Candidate replay checkpoint exceeds the size limit.");
            output.Write(buffer, 0, read);
        }
    }

    private static void ValidateCursor(CandidateFeedReplayCursor cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor.ContractVersion) || cursor.ContractVersion.Length > 128 ||
            string.IsNullOrWhiteSpace(cursor.SourceSha256) || cursor.SourceSha256.Length != 64 ||
            cursor.SourceSha256.Any(character => !Uri.IsHexDigit(character)) || cursor.NextSourceRecordIndex < 0 ||
            (cursor.NextSourceRecordIndex == 0) != (cursor.LastSourceSequence is null) ||
            cursor.LastSourceSequence is < 0)
            throw new InvalidDataException("Candidate replay checkpoint cursor is invalid.");
    }

    private static void RejectReparseAncestors(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw new InvalidDataException("Checkpoint path has no root.");
        var current = root;
        RejectReparsePointIfPresent(current);
        foreach (var part in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     .Where(part => !string.IsNullOrEmpty(part)))
        {
            current = Path.Combine(current, part);
            RejectReparsePointIfPresent(current);
        }
    }

    private static void RejectReparsePointIfPresent(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Candidate replay checkpoint paths cannot contain symbolic links or reparse points.");
    }

    private sealed class FileReplaySessionLease(FileStream stream) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => stream.DisposeAsync();
    }

    private sealed record StoredCheckpoint(int SchemaVersion, CandidateFeedReplayCursor Cursor);
}
