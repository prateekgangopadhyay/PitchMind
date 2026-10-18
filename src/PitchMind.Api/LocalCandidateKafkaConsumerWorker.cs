using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PitchMind.Infrastructure.Feeds;

namespace PitchMind.Api;

public sealed class LocalCandidateKafkaConsumerWorker(CandidateKafkaConsumer consumer,
    IConfiguration configuration, ILogger<LocalCandidateKafkaConsumerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bootstrap = configuration["Kafka:BootstrapServers"] ?? "127.0.0.1:9092";
        var topic = configuration["Kafka:CandidateTopic"] ?? CandidateKafkaProducer.DefaultTopic;
        var group = configuration["Kafka:CandidateConsumerGroup"] ?? "pitchmind-candidate-event-worker-v1";
        logger.LogInformation("LOCAL candidate API worker consuming Kafka topic {Topic}.", topic);
        try
        {
            await consumer.RunAsync(bootstrap, topic, group, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            logger.LogWarning("LocalCandidateKafkaConsumerWorker loop terminated ({ExceptionType}): {Message}",
                ex.GetType().Name, ex.Message);
        }
    }
}
