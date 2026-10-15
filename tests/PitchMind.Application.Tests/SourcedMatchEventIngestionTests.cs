using System.Runtime.CompilerServices;
using System.Text.Json;
using PitchMind.Application.Abstractions;
using PitchMind.Application.Matches;
using PitchMind.Domain.Events;
using PitchMind.Domain.Match;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class SourcedMatchEventIngestionTests
{
    [Theory]
    [InlineData(MatchEventIngestionFailure.EventIdPayloadConflict, "EVENT_ID_PAYLOAD_CONFLICT")]
    [InlineData(MatchEventIngestionFailure.SequenceGap, "SEQUENCE_GAP")]
    public async Task Classified_rejection_is_persisted_with_exact_source_pointer_and_hash_then_rethrown(
        MatchEventIngestionFailure rejection, string expectedReason)
    {
        const string pointer = "fixture://synthetic-match/source-record/17";
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        var matchId = new MatchId(Guid.NewGuid());
        var eventId = Guid.NewGuid();
        var eventStore = new RejectingEventStore(new MatchEventIngestionException(matchId, eventId, rejection, 17, 19));
        var quarantine = new CapturingQuarantineWriter();
        var service = new SourcedMatchEventIngestionService(eventStore, quarantine);
        var candidate = MatchEvent.Create(eventId, matchId, 19, MatchEventType.Pass, 620, 1, DateTimeOffset.UnixEpoch);

        var thrown = await Assert.ThrowsAsync<MatchEventIngestionException>(() => service.AppendAsync(
            candidate, new IngestionSourceReference(pointer, hash), CancellationToken.None));

        Assert.Same(eventStore.Rejection, thrown);
        Assert.Single(quarantine.Evidence);
        var evidence = quarantine.Evidence[0];
        Assert.NotEqual(Guid.Empty, evidence.RecordId);
        Assert.Equal(matchId.Value, evidence.MatchId);
        Assert.Equal(pointer, evidence.SourcePointer);
        Assert.Equal(hash, evidence.PayloadHash);
        Assert.Equal(expectedReason, evidence.Reason);
        Assert.Equal(TimeSpan.Zero, evidence.ObservedAt.Offset);
        using var serializedEvidence = JsonDocument.Parse(JsonSerializer.Serialize(evidence));
        var propertyNames = serializedEvidence.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "MatchId", "ObservedAt", "PayloadHash", "Reason", "RecordId", "SourcePointer" }, propertyNames);
    }

    [Fact]
    public async Task Missing_source_hash_stays_null_and_a_successful_append_creates_no_quarantine_record()
    {
        var matchId = new MatchId(Guid.NewGuid());
        var eventStore = new AcceptingEventStore();
        var quarantine = new CapturingQuarantineWriter();
        var service = new SourcedMatchEventIngestionService(eventStore, quarantine);
        var candidate = MatchEvent.Create(Guid.NewGuid(), matchId, 0, MatchEventType.KickOff, 0, 1, DateTimeOffset.UnixEpoch);

        await service.AppendAsync(candidate, new IngestionSourceReference("source://match/record/0", null), CancellationToken.None);

        Assert.True(eventStore.Appended);
        Assert.Empty(quarantine.Evidence);
    }

    [Fact]
    public async Task Quarantine_write_failure_remains_visible_with_original_ingestion_failure()
    {
        var matchId = new MatchId(Guid.NewGuid());
        var rejection = new MatchEventIngestionException(matchId, Guid.NewGuid(),
            MatchEventIngestionFailure.SequenceAlreadyUsed, 4, 4);
        var quarantineFailure = new IOException("database unavailable");
        var service = new SourcedMatchEventIngestionService(new RejectingEventStore(rejection),
            new CapturingQuarantineWriter(quarantineFailure));
        var candidate = MatchEvent.Create(Guid.NewGuid(), matchId, 4, MatchEventType.Pass, 620, 1, DateTimeOffset.UnixEpoch);

        var thrown = await Assert.ThrowsAsync<QuarantineEvidenceWriteException>(() => service.AppendAsync(
            candidate, new IngestionSourceReference("source://match/record/4", null), CancellationToken.None));

        Assert.Same(rejection, thrown.IngestionFailure);
        Assert.Same(quarantineFailure, thrown.InnerException);
    }

    private sealed class CapturingQuarantineWriter(Exception? failure = null) : IQuarantineEvidenceWriter
    {
        public List<QuarantineEvidence> Evidence { get; } = [];

        public Task AppendAsync(QuarantineEvidence evidence, CancellationToken cancellationToken)
        {
            if (failure is not null) throw failure;
            Evidence.Add(evidence);
            return Task.CompletedTask;
        }
    }

    private sealed class RejectingEventStore(MatchEventIngestionException rejection) : IMatchEventStore
    {
        public MatchEventIngestionException Rejection => rejection;
        public Task AppendAsync(MatchEvent matchEvent, CancellationToken cancellationToken) => Task.FromException(rejection);
        public async IAsyncEnumerable<MatchEvent> ReadAsync(MatchId matchId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }

    private sealed class AcceptingEventStore : IMatchEventStore
    {
        public bool Appended { get; private set; }
        public Task AppendAsync(MatchEvent matchEvent, CancellationToken cancellationToken)
        {
            Appended = true;
            return Task.CompletedTask;
        }
        public async IAsyncEnumerable<MatchEvent> ReadAsync(MatchId matchId,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            yield break;
        }
    }
}
