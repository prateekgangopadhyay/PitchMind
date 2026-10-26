using PitchMind.Infrastructure.Feeds;

namespace PitchMind.Api;

/// <summary>
/// Directly stages candidate simulation envelopes into outbox storage so events stream reliably in all environments.
/// </summary>
public sealed class DirectCandidateEventPublisher(
    CandidateFeedPersistence persistence,
    ICandidateEventPublisher? secondaryPublisher = null) : ICandidateEventPublisher
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (secondaryPublisher is not null)
        {
            try
            {
                await secondaryPublisher.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Secondary broker (e.g. Event Hubs) readiness failure should not block simulation streaming
            }
        }
    }

    public async Task PublishAsync(CandidateReplayEnvelope envelope, CancellationToken cancellationToken)
    {
        var source = envelope.SourceRecord;
        var message = new CandidateKafkaMessage(
            envelope.ContractVersion,
            envelope.SourceSha256,
            source.SourceMatchId,
            source.SourceEventId,
            source.SourceSequence,
            source.RawClockValue,
            source.RawPeriod,
            source.RawEventType,
            source.RawPayload);

        await persistence.EnqueueAsync(message, cancellationToken).ConfigureAwait(false);

        if (secondaryPublisher is not null)
        {
            try
            {
                await secondaryPublisher.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Secondary broker publish failure is non-fatal to live simulation
            }
        }
    }
}
