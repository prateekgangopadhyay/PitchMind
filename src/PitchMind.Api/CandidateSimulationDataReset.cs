using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.Infrastructure.Feeds;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.Api;

/// <summary>Clears candidate state and starts a fresh replay from the beginning of its source feed.</summary>
internal static class CandidateSimulationDataReset
{
    public static async Task<long> ClearAndRestartAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var simulation = services.GetService<LocalCandidateSimulationService>();
        var persistence = services.GetService<CandidateFeedPersistence>();

        // Stop publishing before removing durable state so a new event cannot race the clear.
        if (simulation is not null)
            await simulation.ResetAsync(cancellationToken).ConfigureAwait(false);

        var dataSource = services.GetService<NpgsqlDataSource>()
            ?? throw new InvalidOperationException("PostgreSQL is required to clear candidate-feed records.");
        var cache = services.GetService<IMatchProjectionCache>()
            ?? throw new InvalidOperationException("Redis is required to clear candidate-feed cache state.");
        var count = await DataClearingService.ClearCandidateDataAsync(
            dataSource, cache, cancellationToken).ConfigureAwait(false);

        // CandidateFeedPersistence also maintains an in-process outbox/cursor. Clear it along
        // with PostgreSQL so replayed source sequences are not treated as duplicates.
        persistence?.ClearInMemory();

        if (simulation is not null && persistence is not null)
            await simulation.StartAsync(persistence, cancellationToken).ConfigureAwait(false);

        return count;
    }
}
