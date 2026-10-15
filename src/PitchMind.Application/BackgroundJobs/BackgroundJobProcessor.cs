using System.Collections.Concurrent;

namespace PitchMind.Application.BackgroundJobs;

public interface IJobDeadLetterSink
{
    Task DeadLetterAsync<T>(BackgroundJobEnvelope<T> envelope, string reason, string details, CancellationToken cancellationToken);
}

public interface IJobHandler<T>
{
    Task HandleAsync(BackgroundJobEnvelope<T> envelope, CancellationToken cancellationToken);
}

public sealed class BackgroundJobProcessor(IJobDeadLetterSink deadLetterSink)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> processedJobIds = new(StringComparer.Ordinal);
    private const int DefaultMaxAttempts = 3;

    public async Task<JobExecutionOutcome> ProcessAsync<T>(
        BackgroundJobEnvelope<T> envelope,
        IJobHandler<T> handler,
        CancellationToken cancellationToken)
    {
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.JobId))
            return JobExecutionOutcome.PoisonDeadLetter;

        // Idempotent deduplication: already processed jobs complete immediately
        if (processedJobIds.ContainsKey(envelope.JobId))
            return JobExecutionOutcome.Completed;

        // Validate payload integrity (fail-closed poison handling)
        if (envelope.Payload is null || string.IsNullOrWhiteSpace(envelope.MatchId))
        {
            await deadLetterSink.DeadLetterAsync(envelope, "POISON_PAYLOAD", "Payload or MatchId is missing or empty.", cancellationToken);
            return JobExecutionOutcome.PoisonDeadLetter;
        }

        var maxAttempts = envelope.MaxAttempts > 0 ? envelope.MaxAttempts : DefaultMaxAttempts;
        if (envelope.AttemptCount > maxAttempts)
        {
            await deadLetterSink.DeadLetterAsync(envelope, "EXCEEDED_MAX_RETRIES", $"Attempt count {envelope.AttemptCount} exceeded maximum {maxAttempts}.", cancellationToken);
            return JobExecutionOutcome.PoisonDeadLetter;
        }

        try
        {
            await handler.HandleAsync(envelope, cancellationToken);
            processedJobIds[envelope.JobId] = DateTimeOffset.UtcNow;
            return JobExecutionOutcome.Completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidDataException ex)
        {
            // Unrecoverable data corruption in job payload: route to DLQ immediately
            await deadLetterSink.DeadLetterAsync(envelope, "POISON_DATA", ex.Message, cancellationToken);
            return JobExecutionOutcome.PoisonDeadLetter;
        }
        catch (Exception)
        {
            if (envelope.AttemptCount >= maxAttempts)
            {
                await deadLetterSink.DeadLetterAsync(envelope, "EXCEEDED_MAX_RETRIES", $"Failed after {envelope.AttemptCount} attempts.", cancellationToken);
                return JobExecutionOutcome.PoisonDeadLetter;
            }
            return JobExecutionOutcome.RetryableFailure;
        }
    }
}
