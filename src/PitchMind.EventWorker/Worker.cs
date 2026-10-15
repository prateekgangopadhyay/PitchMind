using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PitchMind.Infrastructure.Storage;

namespace PitchMind.EventWorker;

public sealed class Worker(PostgresOutboxDispatcher dispatcher, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Canonical outbox dispatcher started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await dispatcher.DispatchOneAsync(stoppingToken);
                if (result == OutboxDispatchResult.NoMessage)
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Canonical outbox dispatch cycle failed; retrying after bounded delay.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}
