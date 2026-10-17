using System.Text;
using PitchMind.Infrastructure.Storage;
using Xunit;

namespace PitchMind.Infrastructure.Tests;

public sealed class AzureManagedRedisProjectionCacheTests
{
    [Fact]
    public void ParsesAzureManagedRedisConnectionStringCorrectly()
    {
        var cs = "pm-dev-redis.redis.cache.windows.net:6380,password=secretAuthKey123!,ssl=True,abortConnect=False";
        var cache = new AzureManagedRedisProjectionCache(cs);

        Assert.Equal("pm-dev-redis.redis.cache.windows.net", cache.Host);
        Assert.Equal(6380, cache.Port);
        Assert.True(cache.UseSsl);
    }

    [Fact]
    public void ParsesStandardRedisConnectionString()
    {
        var cs = "127.0.0.1:6379,ssl=False";
        var cache = new AzureManagedRedisProjectionCache(cs);

        Assert.Equal("127.0.0.1", cache.Host);
        Assert.Equal(6379, cache.Port);
        Assert.False(cache.UseSsl);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_GracefullyWhenRedisEndpointUnavailable()
    {
        // Unreachable port on loopback - should degrade gracefully to null without throwing
        var cache = new AzureManagedRedisProjectionCache("127.0.0.1", 64999, useSsl: false, timeoutMs: 300);

        var result = await cache.GetAsync("test-key", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_DoesNotThrow_WhenRedisEndpointUnavailable()
    {
        var cache = new AzureManagedRedisProjectionCache("127.0.0.1", 64999, useSsl: false, timeoutMs: 300);
        var data = Encoding.UTF8.GetBytes("test-payload");

        // Set should degrade gracefully and not throw
        await cache.SetAsync("test-key", data, TimeSpan.FromMinutes(1), CancellationToken.None);
    }

    [Fact]
    public async Task RemoveAsync_DoesNotThrow_WhenRedisEndpointUnavailable()
    {
        var cache = new AzureManagedRedisProjectionCache("127.0.0.1", 64999, useSsl: false, timeoutMs: 300);

        await cache.RemoveAsync("test-key", CancellationToken.None);
    }

    [Fact]
    public async Task PingAsync_ReturnsFalse_WhenRedisEndpointUnavailable()
    {
        var cache = new AzureManagedRedisProjectionCache("127.0.0.1", 64999, useSsl: false, timeoutMs: 300);

        var isAlive = await cache.PingAsync(CancellationToken.None);

        Assert.False(isAlive);
    }
}
