using PitchMind.Application.Abstractions;
using Xunit;

namespace PitchMind.Application.Tests;

public sealed class ParallelOutboxPublisherTests
{
    [Fact]
    public async Task StartsEveryProjectionBeforeAwaitingCompletion()
    {
        var started = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new BlockingPublisher(() =>
        {
            if (Interlocked.Increment(ref started) == 2) bothStarted.TrySetResult();
        }, release.Task);
        var second = new BlockingPublisher(() =>
        {
            if (Interlocked.Increment(ref started) == 2) bothStarted.TrySetResult();
        }, release.Task);
        var publisher = new ParallelOutboxPublisher([first, second]);

        var publish = publisher.PublishAsync(Guid.NewGuid(), "{}", CancellationToken.None);
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(publish.IsCompleted);
        release.SetResult();
        await publish;

        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
    }

    [Fact]
    public async Task PropagatesProjectionFailureSoDurableOutboxCanRetry()
    {
        var successful = new RecordingPublisher();
        var failing = new RecordingPublisher(new IOException("projection unavailable"));
        var publisher = new ParallelOutboxPublisher([successful, failing]);

        await Assert.ThrowsAsync<IOException>(() =>
            publisher.PublishAsync(Guid.NewGuid(), "{}", CancellationToken.None));

        Assert.Equal(1, successful.Calls);
        Assert.Equal(1, failing.Calls);
    }

    private sealed class BlockingPublisher(Action onStart, Task release) : IOutboxPublisher
    {
        public int Calls { get; private set; }

        public async Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
        {
            Calls++;
            onStart();
            await release.WaitAsync(cancellationToken);
        }
    }

    private sealed class RecordingPublisher(Exception? failure = null) : IOutboxPublisher
    {
        public int Calls { get; private set; }

        public Task PublishAsync(Guid messageId, string messageJson, CancellationToken cancellationToken)
        {
            Calls++;
            return failure is null ? Task.CompletedTask : Task.FromException(failure);
        }
    }
}
