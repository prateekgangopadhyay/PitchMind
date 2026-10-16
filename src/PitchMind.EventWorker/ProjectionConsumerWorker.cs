using Azure.Messaging.EventHubs.Processor;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PitchMind.EventWorker;

public sealed class ProjectionConsumerWorker(
    EventProcessorClient processor,
    EventHubProjectionEventHandler handler,
    ILogger<ProjectionConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        processor.ProcessEventAsync += ProcessEventAsync;
        processor.ProcessErrorAsync += ProcessErrorAsync;

        logger.LogInformation("Projection consumer starting.");
        await processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during host shutdown.
        }
        finally
        {
            await processor.StopProcessingAsync(CancellationToken.None);
            processor.ProcessEventAsync -= ProcessEventAsync;
            processor.ProcessErrorAsync -= ProcessErrorAsync;
        }
    }

    private async Task ProcessEventAsync(ProcessEventArgs args)
    {
        if (args.Data is null) return;
        await handler.HandleAsync(args.Data, args.Partition.PartitionId,
            args.UpdateCheckpointAsync, args.CancellationToken);
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "Projection consumer callback failed for operation {Operation}, partition {PartitionId}.",
            args.Operation, args.PartitionId);
        return Task.CompletedTask;
    }

}
