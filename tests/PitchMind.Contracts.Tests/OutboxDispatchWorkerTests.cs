using Microsoft.Extensions.Logging.Abstractions;
using PitchMind.Api;
using PitchMind.Infrastructure.Storage;
using Xunit;

namespace PitchMind.Contracts.Tests;

public sealed class OutboxDispatchWorkerTests
{
    [Fact]
    public async Task Worker_invokes_dispatcher_and_stops_when_cancelled()
    {
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new OutboxDispatchWorker(_ =>
        {
            dispatched.TrySetResult();
            return Task.FromResult(OutboxDispatchResult.Published);
        }, NullLogger<OutboxDispatchWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.True(worker.ExecuteTask?.IsCompleted);
    }

    [Fact]
    public async Task Worker_recovers_from_dispatch_exception_and_retries()
    {
        var calls = 0;
        var retried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new OutboxDispatchWorker(async token =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new InvalidOperationException("test-only dispatch failure");
            retried.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            return OutboxDispatchResult.NoMessage;
        }, NullLogger<OutboxDispatchWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await retried.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.True(worker.ExecuteTask?.IsCompleted);
    }
}
