using PitchMind.Application.BackgroundJobs;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class BackgroundJobProcessorTests
{
    [Fact]
    public async Task ProcessAsync_Executes_Handler_And_Succeeds()
    {
        var sink = new TestDeadLetterSink();
        var processor = new BackgroundJobProcessor(sink);
        var handler = new TestHandler();

        var envelope = new BackgroundJobEnvelope<string?>(
            "job-1", "TestJob", "match-100", 1, 3, DateTimeOffset.UtcNow, "corr-1", "test-payload");

        var outcome = await processor.ProcessAsync(envelope, handler, CancellationToken.None);

        Assert.Equal(JobExecutionOutcome.Completed, outcome);
        Assert.Equal(1, handler.CallCount);
        Assert.Empty(sink.DeadLetteredJobs);
    }

    [Fact]
    public async Task ProcessAsync_Deduplicates_Already_Processed_JobId()
    {
        var sink = new TestDeadLetterSink();
        var processor = new BackgroundJobProcessor(sink);
        var handler = new TestHandler();

        var envelope = new BackgroundJobEnvelope<string?>(
            "job-dedupe", "TestJob", "match-100", 1, 3, DateTimeOffset.UtcNow, "corr-1", "payload");

        var first = await processor.ProcessAsync(envelope, handler, CancellationToken.None);
        var second = await processor.ProcessAsync(envelope, handler, CancellationToken.None);

        Assert.Equal(JobExecutionOutcome.Completed, first);
        Assert.Equal(JobExecutionOutcome.Completed, second);
        Assert.Equal(1, handler.CallCount); // handler only executed once!
    }

    [Fact]
    public async Task ProcessAsync_Routes_To_DeadLetterSink_When_Payload_Or_MatchId_Missing()
    {
        var sink = new TestDeadLetterSink();
        var processor = new BackgroundJobProcessor(sink);
        var handler = new TestHandler();

        var envelope = new BackgroundJobEnvelope<string?>(
            "job-poison", "TestJob", "", 1, 3, DateTimeOffset.UtcNow, "corr-1", null);

        var outcome = await processor.ProcessAsync(envelope, handler, CancellationToken.None);

        Assert.Equal(JobExecutionOutcome.PoisonDeadLetter, outcome);
        Assert.Equal(0, handler.CallCount);
        Assert.Single(sink.DeadLetteredJobs);
        Assert.Equal("POISON_PAYLOAD", sink.DeadLetteredJobs[0].Reason);
    }

    [Fact]
    public async Task ProcessAsync_Routes_To_DeadLetterSink_When_Max_Retries_Exceeded()
    {
        var sink = new TestDeadLetterSink();
        var processor = new BackgroundJobProcessor(sink);
        var handler = new TestHandler { ThrowOnHandle = true };

        var envelope = new BackgroundJobEnvelope<string?>(
            "job-retry-fail", "TestJob", "match-100", 3, 3, DateTimeOffset.UtcNow, "corr-1", "payload");

        var outcome = await processor.ProcessAsync(envelope, handler, CancellationToken.None);

        Assert.Equal(JobExecutionOutcome.PoisonDeadLetter, outcome);
        Assert.Single(sink.DeadLetteredJobs);
        Assert.Equal("EXCEEDED_MAX_RETRIES", sink.DeadLetteredJobs[0].Reason);
    }

    [Fact]
    public async Task ProcessAsync_Returns_RetryableFailure_When_Attempts_Remain()
    {
        var sink = new TestDeadLetterSink();
        var processor = new BackgroundJobProcessor(sink);
        var handler = new TestHandler { ThrowOnHandle = true };

        var envelope = new BackgroundJobEnvelope<string?>(
            "job-transient", "TestJob", "match-100", 1, 3, DateTimeOffset.UtcNow, "corr-1", "payload");

        var outcome = await processor.ProcessAsync(envelope, handler, CancellationToken.None);

        Assert.Equal(JobExecutionOutcome.RetryableFailure, outcome);
        Assert.Empty(sink.DeadLetteredJobs); // Not dead-lettered yet!
    }

    private sealed class TestHandler : IJobHandler<string?>
    {
        public int CallCount { get; private set; }
        public bool ThrowOnHandle { get; init; }

        public Task HandleAsync(BackgroundJobEnvelope<string?> envelope, CancellationToken cancellationToken)
        {
            CallCount++;
            if (ThrowOnHandle) throw new InvalidOperationException("Simulated transient processing failure.");
            return Task.CompletedTask;
        }
    }

    private sealed class TestDeadLetterSink : IJobDeadLetterSink
    {
        public List<(string JobId, string Reason, string Details)> DeadLetteredJobs { get; } = [];

        public Task DeadLetterAsync<T>(BackgroundJobEnvelope<T> envelope, string reason, string details, CancellationToken cancellationToken)
        {
            DeadLetteredJobs.Add((envelope.JobId, reason, details));
            return Task.CompletedTask;
        }
    }
}
