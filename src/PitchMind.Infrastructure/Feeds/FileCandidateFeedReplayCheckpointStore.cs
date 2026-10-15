using System.Text.Json;

namespace PitchMind.Infrastructure.Feeds;

public interface ICandidateFeedReplayCheckpointStore
{
    Task<CandidateFeedReplayCursor?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(CandidateFeedReplayCursor cursor, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores only the last consumer-acknowledged raw-source cursor. The checkpoint is local
/// operational state and never represents canonical football state.
/// </summary>
public sealed class FileCandidateFeedReplayCheckpointStore : ICandidateFeedReplayCheckpointStore
{
    private const int CheckpointSchemaVersion = 1;
    private const int MaximumCheckpointBytes = 16 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _directoryPath;
    private readonly string _checkpointPath;

    public FileCandidateFeedReplayCheckpointStore(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
        _checkpointPath = Path.Combine(_directoryPath, "candidate-feed-replay-checkpoint.json");
    }

    public async Task<CandidateFeedReplayCursor?> LoadAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectory();
        RejectReparsePoint(_checkpointPath);
        if (!File.Exists(_checkpointPath)) return null;

        var info = new FileInfo(_checkpointPath);
        if (info.Length > MaximumCheckpointBytes)
            throw new InvalidDataException("Candidate replay checkpoint exceeds the size limit.");

        try
        {
            await using var input = new FileStream(_checkpointPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var stored = await JsonSerializer.DeserializeAsync<StoredCheckpoint>(input, JsonOptions, cancellationToken)
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
        RejectReparsePoint(_checkpointPath);

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

            RejectReparsePoint(_checkpointPath);
            File.Move(temporaryPath, _checkpointPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private void EnsureDirectory()
    {
        Directory.CreateDirectory(_directoryPath);
        RejectReparsePoint(_directoryPath);
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

    private static void RejectReparsePoint(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Candidate replay checkpoint paths cannot contain symbolic links or reparse points.");
    }

    private sealed record StoredCheckpoint(int SchemaVersion, CandidateFeedReplayCursor Cursor);
}
