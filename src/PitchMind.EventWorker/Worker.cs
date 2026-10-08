using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PitchMind.EventWorker;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Event worker scaffold started; ingestion adapter not yet configured.");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
