namespace PitchMind.Application.Abstractions;

/// <summary>At-least-once transport boundary. Consumers deduplicate using the stable message ID.</summary>
public interface IOutboxPublisher
{
    Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken);
}
