using Azure.Core;
using Microsoft.Azure.StackExchangeRedis;
using Microsoft.Extensions.Logging;
using PitchMind.Application.Abstractions;
using StackExchange.Redis;

namespace PitchMind.Infrastructure.Storage;

/// <summary>Best-effort, rebuildable Redis cursor/cache using Entra token refresh and TLS.</summary>
public sealed class AzureEntraRedisProjectionCache : IMatchProjectionCache, IAsyncDisposable
{
    private readonly string _endpoint;
    private readonly TokenCredential _credential;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private ConnectionMultiplexer? _connection;

    public AzureEntraRedisProjectionCache(string endpoint, TokenCredential credential, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (!endpoint.Contains(':', StringComparison.Ordinal) || endpoint.Contains(';', StringComparison.Ordinal) ||
            endpoint.Contains(',', StringComparison.Ordinal) || endpoint.Any(char.IsWhiteSpace) ||
            endpoint.Contains('@', StringComparison.Ordinal) || endpoint.Contains("://", StringComparison.Ordinal))
            throw new InvalidOperationException("Redis:Endpoint must be an Azure Managed Redis host and TLS port, without credentials or a URI scheme.");
        _endpoint = endpoint.Trim();
        _credential = credential ?? throw new ArgumentNullException(nameof(credential));
        _logger = logger;
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        try { return await (await GetDatabaseAsync(cancellationToken).ConfigureAwait(false)).StringGetAsync(key).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger?.LogWarning(ex, "Redis cursor read failed; PostgreSQL remains the durable source."); return null; }
    }

    public async Task SetAsync(string key, byte[] value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        try { await (await GetDatabaseAsync(cancellationToken).ConfigureAwait(false)).StringSetAsync(key, value, timeToLive).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger?.LogWarning(ex, "Redis cursor write failed; PostgreSQL remains the durable source."); }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try { await (await GetDatabaseAsync(cancellationToken).ConfigureAwait(false)).KeyDeleteAsync(key).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { _logger?.LogWarning(ex, "Redis cursor removal failed."); }
    }

    private async Task<IDatabase> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsConnected: true } current) return current.GetDatabase();
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsConnected: true } connected) return connected.GetDatabase();
            var options = ConfigurationOptions.Parse(_endpoint);
            options.Ssl = true;
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            options.ConnectTimeout = 5000;
            await options.ConfigureForAzureWithTokenCredentialAsync(_credential).ConfigureAwait(false);
            _connection = await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
            return _connection.GetDatabase();
        }
        finally { _connectGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await _connection.DisposeAsync().ConfigureAwait(false);
        _connectGate.Dispose();
    }
}
