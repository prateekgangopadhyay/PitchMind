using System.Text.Json;
using Azure.Messaging.EventHubs;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PitchMind.Application.Abstractions;
using PitchMind.EventWorker;
using PitchMind.Infrastructure.Storage;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class EventHubOutboxPublisherTests
{
    private static readonly Guid MessageId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MatchId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly string Sha256 = new('a', 64);

    [Fact]
    public async Task PublishesOriginalCommittedPayloadWithStableIdAndMatchPartitionKey()
    {
        var payload = Payload(MessageId, MatchId);
        var sender = new CapturingSender();
        var publisher = new EventHubOutboxPublisher(sender);

        await publisher.PublishAsync(MessageId, payload, CancellationToken.None);

        var sent = Assert.Single(sender.Sent);
        Assert.Equal(MatchId.ToString("D"), sent.PartitionKey);
        Assert.Equal(MessageId.ToString("D"), sent.Event.MessageId);
        Assert.Equal("application/json", sent.Event.ContentType);
        Assert.Equal(payload, sent.Event.EventBody.ToString());
        Assert.Equal("ProjectionPublishedV1", sent.Event.Properties["schemaName"]);
        Assert.Equal(MatchId.ToString("D"), sent.Event.Properties["matchId"]);
        Assert.Equal("9", sent.Event.Properties["canonicalRevision"]);
        Assert.Equal("4", sent.Event.Properties["projectionRevision"]);
        Assert.Equal("42", sent.Event.Properties["sequence"]);
        Assert.Equal(Sha256, sent.Event.Properties["sourceSha256"]);
    }

    [Fact]
    public async Task RejectsOutboxIdMismatchBeforeSending()
    {
        var sender = new CapturingSender();
        var publisher = new EventHubOutboxPublisher(sender);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            publisher.PublishAsync(Guid.NewGuid(), Payload(MessageId, MatchId), CancellationToken.None));

        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task RejectsDuplicatePayloadPropertiesBeforeSending()
    {
        var sender = new CapturingSender();
        var publisher = new EventHubOutboxPublisher(sender);
        var payload = Payload(MessageId, MatchId).Replace("\"sequence\":42", "\"sequence\":42,\"sequence\":43", StringComparison.Ordinal);

        await Assert.ThrowsAsync<InvalidDataException>(() => publisher.PublishAsync(MessageId, payload, CancellationToken.None));

        Assert.Empty(sender.Sent);
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("attacker.servicebus.windows.net.evil.example")]
    [InlineData("namespace.servicebus.windows.net/ignored-path")]
    [InlineData("namespace.servicebus.windows.net:444")]
    [InlineData("https://namespace.servicebus.windows.net")]
    public void RejectsNonAzureOrMalformedEventHubNamespace(string value) =>
        Assert.Throws<InvalidOperationException>(() => AzureEventWorkerConfiguration.ValidateEventHubNamespace(value));

    [Fact]
    public void AcceptsAzureEventHubNamespaceAndNormalizesCase() =>
        Assert.Equal("pitchmind.servicebus.windows.net",
            AzureEventWorkerConfiguration.ValidateEventHubNamespace("PITCHMIND.servicebus.windows.net"));

    [Fact]
    public void AcceptsAzureBlobServiceUriForProcessorCheckpointStorage() =>
        Assert.Equal("https://pitchmind.blob.core.windows.net",
            AzureEventWorkerConfiguration.ValidateBlobServiceUri("https://PITCHMIND.blob.core.windows.net"));

    [Theory]
    [InlineData("http://pitchmind.blob.core.windows.net")]
    [InlineData("https://pitchmind.blob.core.windows.net.evil.example")]
    [InlineData("https://pitchmind.blob.core.windows.net/other")]
    [InlineData("https://pitchmind.blob.core.windows.net:444")]
    public void RejectsNonAzureOrMalformedBlobServiceUri(string value) =>
        Assert.Throws<InvalidOperationException>(() => AzureEventWorkerConfiguration.ValidateBlobServiceUri(value));

    [Fact]
    public async Task ConsumerDurablyIsolatesMismatchedTransportPropertiesBeforeBrokerCheckpoint()
    {
        var callbackReached = false;
        var poison = new CapturingPoisonStore();
        var checkpoints = new CapturingCheckpointStore();
        var handler = new EventHubProjectionEventHandler(null!, poison, checkpoints, "projection-reader-v1");
        var properties = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["schemaName"] = "ProjectionPublishedV2",
            ["matchId"] = MatchId.ToString("D"),
            ["canonicalRevision"] = "9",
            ["projectionRevision"] = "4",
            ["sequence"] = "42",
            ["sourceSha256"] = Sha256
        };
        var message = new ProjectionEvent(MessageId.ToString("D"), "9000", 100,
            System.Text.Encoding.UTF8.GetBytes(Payload(MessageId, MatchId)), properties);

        await handler.HandleAsync(message, "0", _ =>
        {
            callbackReached = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(callbackReached);
        var storedPoison = Assert.Single(poison.Records);
        Assert.Equal("TRANSPORT_METADATA_MISMATCH", storedPoison.ReasonCode);
        Assert.Equal("9000", storedPoison.Offset);
        Assert.Equal(100, storedPoison.BrokerSequenceNumber);
        Assert.Equal(storedPoison.PoisonId, checkpoints.PoisonId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("local")]
    [InlineData("dev")]
    public void EventWorkerRejectsNonAzureRuntimeProfiles(string? profile) =>
        Assert.Throws<InvalidOperationException>(() => AzureEventWorkerConfiguration.ValidateRuntimeProfile(profile));

    [Fact]
    public void EventWorkerAcceptsAzureRuntimeProfile() =>
        AzureEventWorkerConfiguration.ValidateRuntimeProfile("azure");

    [Fact]
    public void ProjectionConsumerCompositionRegistersPostgresCheckpointStore()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["EventHubs:Namespace"] = "pitchmind.servicebus.windows.net",
            ["EventHubs:HubName"] = "canonical-telemetry",
            ["EventHubs:ConsumerGroup"] = "projection-reader",
            ["EventHubs:ProjectionConsumerId"] = "projection-reader-v1",
            ["Storage:BlobServiceUri"] = "https://pitchmind.blob.core.windows.net",
            ["Storage:CheckpointContainer"] = "projection-checkpoints"
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<TokenCredential>(new NeverCalledCredential());
        services.AddSingleton(NpgsqlDataSource.Create("Host=localhost;Database=pitchmind;Username=worker"));
        AzureEventWorkerConfiguration.AddAzureProjectionConsumer(services, configuration);

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<EventHubProjectionEventHandler>();

        Assert.NotNull(handler);
        Assert.IsType<PostgresProjectionPublishedConsumer>(provider.GetRequiredService<PostgresProjectionPublishedConsumer>());
        Assert.IsType<PostgresConsumerCheckpointStore>(provider.GetRequiredService<IConsumerCheckpointStore>());
    }

    [Fact]
    public void AcceptsPasswordBasedPostgresConfigurationWithoutCallingEntraCredential()
    {
        var credential = new NeverCalledCredential();
        using var dataSource = AzureEventWorkerConfiguration.CreatePostgresDataSource(
            "Host=pm.postgres.database.azure.com;Database=pitchmind;Username=worker;Password=plaintext", credential);
        Assert.NotNull(dataSource);
        Assert.False(credential.WasCalled);
    }

    [Fact]
    public void RejectsNonAzurePostgresHostBeforeCreatingDataSource()
    {
        var credential = new NeverCalledCredential();
        Assert.Throws<InvalidOperationException>(() => AzureEventWorkerConfiguration.CreatePostgresDataSource(
            "Host=localhost;Database=pitchmind;Username=worker", credential));
        Assert.False(credential.WasCalled);
    }

    private static string Payload(Guid messageId, Guid matchId) => JsonSerializer.Serialize(new
    {
        messageId,
        matchId,
        canonicalRevision = 9L,
        projectionRevision = 4,
        sequence = 42L,
        clockSeconds = 300,
        algorithmVersion = "aggregate-v1",
        sourceSha256 = Sha256,
        stateSha256 = new string('b', 64),
        state = new { homeScore = 1, awayScore = 0 }
    });

    private sealed class CapturingSender : IEventHubEventSender
    {
        public List<(EventData Event, string PartitionKey)> Sent { get; } = [];

        public Task SendAsync(EventData eventData, string partitionKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Sent.Add((eventData, partitionKey));
            return Task.CompletedTask;
        }
    }

    private sealed class NeverCalledCredential : TokenCredential
    {
        public bool WasCalled { get; private set; }
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Credential must not be called by this test.");
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            WasCalled = true;
            throw new InvalidOperationException("Credential must not be called by this test.");
        }
    }

    private sealed class CapturingPoisonStore : IProjectionPoisonStore
    {
        public List<ProjectionPoisonRecord> Records { get; } = [];

        public Task<Guid> RecordAsync(ProjectionPoisonRecord record, CancellationToken cancellationToken)
        {
            Records.Add(record);
            return Task.FromResult(record.PoisonId);
        }

        public Task MarkReplayedAsync(Guid poisonId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CapturingCheckpointStore : IConsumerCheckpointStore
    {
        public Guid? PoisonId { get; private set; }

        public Task<ConsumerCheckpointAdvanceResult> AdvanceAsync(ConsumerCheckpoint checkpoint,
            CancellationToken cancellationToken, bool alreadyProcessed = false) =>
            throw new NotImplementedException();

        public Task<ConsumerCheckpointAdvanceResult> AdvancePoisonAsync(string consumerId, string partitionId,
            string offset, long brokerSequenceNumber, Guid poisonId, CancellationToken cancellationToken)
        {
            PoisonId = poisonId;
            return Task.FromResult(ConsumerCheckpointAdvanceResult.Advanced);
        }
    }
}
