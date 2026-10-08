using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PitchMind.AgentWorker;

public sealed class Worker(ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Agent worker scaffold started; no model calls configured.");
        await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }
}
