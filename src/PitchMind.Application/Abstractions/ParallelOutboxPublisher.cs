namespace PitchMind.Application.Abstractions;

/// <summary>
/// Dispatches one committed outbox message to independent, idempotent projections concurrently.
/// The caller acknowledges the durable outbox row only after every handler succeeds; retries may
/// repeat successful handlers, so each handler must be idempotent.
/// </summary>
public sealed class ParallelOutboxPublisher(IReadOnlyList<IOutboxPublisher> publishers) : IOutboxPublisher
{
    public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publishers);
        if (publishers.Count == 0)
            throw new InvalidOperationException("At least one outbox projection handler is required.");

        await Task.WhenAll(publishers.Select(publisher =>
            publisher.PublishAsync(messageId, messageJson, cancellationToken))).ConfigureAwait(false);
    }
}
