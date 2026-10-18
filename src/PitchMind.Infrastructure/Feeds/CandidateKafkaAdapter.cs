using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;

namespace PitchMind.Infrastructure.Feeds;

public interface ICandidateEventPublisher
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);
    Task PublishAsync(CandidateReplayEnvelope envelope, CancellationToken cancellationToken);
}

public sealed class CandidateKafkaProducer : ICandidateEventPublisher, IDisposable
{
    public const string DefaultTopic = "pitchmind.candidate.raw.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _bootstrap;
    private readonly string _topic;
    private readonly IProducer<string, string> _producer;
    private int _ready;

    public CandidateKafkaProducer(string bootstrap, string topic = DefaultTopic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrap);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        _bootstrap = bootstrap; _topic = topic;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrap, Acks = Acks.All, EnableIdempotence = true,
            MessageTimeoutMs = 10000, ClientId = "pitchmind-candidate-simulation",
            BrokerAddressFamily = BrokerAddressFamily.V4
        }).Build();
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _ready) == 1) return;
        using var admin = new AdminClientBuilder(new AdminClientConfig
        {
            BootstrapServers = _bootstrap,
            BrokerAddressFamily = BrokerAddressFamily.V4
        }).Build();
        try
        {
            await admin.CreateTopicsAsync([new TopicSpecification { Name = _topic, NumPartitions = 1, ReplicationFactor = 1 }])
                .WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (CreateTopicsException e) when (e.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists)) { }
        Volatile.Write(ref _ready, 1);
    }

    public async Task PublishAsync(CandidateReplayEnvelope envelope, CancellationToken cancellationToken)
    {
        var source = envelope.SourceRecord;
        var message = new CandidateKafkaMessage(envelope.ContractVersion, envelope.SourceSha256, source.SourceMatchId,
            source.SourceEventId, source.SourceSequence, source.RawClockValue, source.RawPeriod, source.RawEventType, source.RawPayload);
        var headers = new Headers
        {
            { "schemaName", "CandidateRawEventV1"u8.ToArray() },
            { "sourceSha256", System.Text.Encoding.UTF8.GetBytes(message.SourceSha256) },
            { "sourceMatchId", System.Text.Encoding.UTF8.GetBytes(message.SourceMatchId) },
            { "sourceSequence", System.Text.Encoding.UTF8.GetBytes(message.SourceSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)) }
        };
        var result = await _producer.ProduceAsync(_topic,
            new Message<string, string> { Key = message.SourceMatchId, Value = JsonSerializer.Serialize(message, JsonOptions), Headers = headers },
            cancellationToken).ConfigureAwait(false);
        if (result.Status == PersistenceStatus.NotPersisted)
            throw new InvalidOperationException("Kafka did not durably acknowledge the candidate event.");
    }
    public void Dispose() => _producer.Dispose();
}

public sealed class CandidateKafkaConsumer(CandidateFeedPersistence persistence)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(string bootstrap, string topic, string groupId, CancellationToken cancellationToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrap, GroupId = groupId, AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, EnablePartitionEof = false, ClientId = "pitchmind-candidate-event-worker",
            BrokerAddressFamily = BrokerAddressFamily.V4
        }).Build();
        consumer.Subscribe(topic);
        while (!cancellationToken.IsCancellationRequested)
        {
            ConsumeResult<string, string> result;
            try { result = consumer.Consume(cancellationToken); }
            catch (ConsumeException e) when (!e.Error.IsFatal)
            { await Task.Delay(250, cancellationToken).ConfigureAwait(false); continue; }
            var bytes = System.Text.Encoding.UTF8.GetBytes(result.Message.Value ?? string.Empty);
            try
            {
                var message = JsonSerializer.Deserialize<CandidateKafkaMessage>(result.Message.Value ?? string.Empty, JsonOptions)
                    ?? throw new InvalidDataException("Kafka candidate record was empty.");
                if (!HeaderEquals(result.Message.Headers, "schemaName", "CandidateRawEventV1") ||
                    !HeaderEquals(result.Message.Headers, "sourceSha256", message.SourceSha256) ||
                    !HeaderEquals(result.Message.Headers, "sourceMatchId", message.SourceMatchId) ||
                    !HeaderEquals(result.Message.Headers, "sourceSequence", message.SourceSequence.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    throw new InvalidDataException("Kafka message headers do not match source envelope.");
                await persistence.EnqueueAsync(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or ArgumentException or KeyNotFoundException)
            {
                await persistence.RecordPoisonAsync("kafka-local", result.Partition.Value.ToString(),
                    result.Offset.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), bytes,
                    "INVALID_CANDIDATE_EVENT", e.GetType().Name, cancellationToken).ConfigureAwait(false);
            }
            // Commit only after the raw outbox row or the poison record is durable.
            consumer.Commit(result);
        }
    }

    private static bool HeaderEquals(Headers headers, string name, string expected)
    {
        var header = headers.LastOrDefault(item => item.Key == name);
        return header is not null && System.Text.Encoding.UTF8.GetString(header.GetValueBytes()) == expected;
    }
}
