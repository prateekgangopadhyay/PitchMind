using System.Buffers;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Microsoft.Extensions.Logging;
using PitchMind.Application.Abstractions;

namespace PitchMind.Infrastructure.Storage;

/// <summary>
/// Cache-aside projection cache backed by Azure Managed Redis Enterprise over TLS.
/// Complies with EP09-S01: TTLs, versioned keys, tenant/match scope, and graceful degradation
/// so cache failures or outages never corrupt canonical state or block canonical reads.
/// </summary>
public sealed class AzureManagedRedisProjectionCache : IMatchProjectionCache, IDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string? _password;
    private readonly bool _useSsl;
    private readonly TimeSpan _defaultTtl;
    private readonly ILogger<AzureManagedRedisProjectionCache>? _logger;
    private readonly int _timeoutMs;

    public AzureManagedRedisProjectionCache(
        string connectionString,
        TimeSpan? defaultTtl = null,
        int timeoutMs = 2000,
        ILogger<AzureManagedRedisProjectionCache>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        (_host, _port, _password, _useSsl) = ParseConnectionString(connectionString);
        _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(5);
        _timeoutMs = timeoutMs;
        _logger = logger;
    }

    public AzureManagedRedisProjectionCache(
        string host,
        int port,
        string? password = null,
        bool useSsl = true,
        TimeSpan? defaultTtl = null,
        int timeoutMs = 2000,
        ILogger<AzureManagedRedisProjectionCache>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _host = host;
        _port = port > 0 ? port : (useSsl ? 6380 : 6379);
        _password = password;
        _useSsl = useSsl;
        _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(5);
        _timeoutMs = timeoutMs;
        _logger = logger;
    }

    public string Host => _host;
    public int Port => _port;
    public bool UseSsl => _useSsl;

    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeoutMs);

            using var client = await ConnectAsync(cts.Token).ConfigureAwait(false);
            if (client is null) return null;

            using var stream = await OpenStreamAsync(client, cts.Token).ConfigureAwait(false);

            // Execute GET key
            var cmd = $"*2\r\n$3\r\nGET\r\n${Encoding.UTF8.GetByteCount(key)}\r\n{key}\r\n";
            var cmdBytes = Encoding.UTF8.GetBytes(cmd);
            await stream.WriteAsync(cmdBytes, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            return await ReadBulkStringAsync(stream, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Azure Managed Redis GET failed for key {Key}; degrading cleanly to PostgreSQL", key);
            return null;
        }
    }

    public async Task SetAsync(string key, byte[] value, TimeSpan timeToLive, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var ttlSeconds = Math.Max(1, (int)(timeToLive <= TimeSpan.Zero ? _defaultTtl.TotalSeconds : timeToLive.TotalSeconds));

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeoutMs);

            using var client = await ConnectAsync(cts.Token).ConfigureAwait(false);
            if (client is null) return;

            using var stream = await OpenStreamAsync(client, cts.Token).ConfigureAwait(false);

            // Execute SET key value EX ttlSeconds
            var ttlStr = ttlSeconds.ToString();
            var header = $"*5\r\n$3\r\nSET\r\n${Encoding.UTF8.GetByteCount(key)}\r\n{key}\r\n${value.Length}\r\n";
            var trailer = $"\r\n$2\r\nEX\r\n${ttlStr.Length}\r\n{ttlStr}\r\n";

            var headerBytes = Encoding.UTF8.GetBytes(header);
            var trailerBytes = Encoding.UTF8.GetBytes(trailer);

            await stream.WriteAsync(headerBytes, cts.Token).ConfigureAwait(false);
            await stream.WriteAsync(value, cts.Token).ConfigureAwait(false);
            await stream.WriteAsync(trailerBytes, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            await ReadSimpleStringAsync(stream, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed cache write must never fail canonical state persistence or cause unhandled exceptions
            _logger?.LogWarning(ex, "Azure Managed Redis SET failed for key {Key}; continuing safely", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeoutMs);

            using var client = await ConnectAsync(cts.Token).ConfigureAwait(false);
            if (client is null) return;

            using var stream = await OpenStreamAsync(client, cts.Token).ConfigureAwait(false);

            var cmd = $"*2\r\n$3\r\nDEL\r\n${Encoding.UTF8.GetByteCount(key)}\r\n{key}\r\n";
            var cmdBytes = Encoding.UTF8.GetBytes(cmd);
            await stream.WriteAsync(cmdBytes, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            await ReadIntegerAsync(stream, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Azure Managed Redis DEL failed for key {Key}", key);
        }
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_timeoutMs);

            using var client = await ConnectAsync(cts.Token).ConfigureAwait(false);
            if (client is null) return false;

            using var stream = await OpenStreamAsync(client, cts.Token).ConfigureAwait(false);

            var cmd = "*1\r\n$4\r\nPING\r\n";
            var cmdBytes = Encoding.UTF8.GetBytes(cmd);
            await stream.WriteAsync(cmdBytes, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            var reply = await ReadSimpleStringAsync(stream, cts.Token).ConfigureAwait(false);
            return string.Equals(reply, "PONG", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<TcpClient?> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            return null;
        }
    }

    private async Task<Stream> OpenStreamAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Stream netStream = client.GetStream();
        if (_useSsl)
        {
            var sslStream = new SslStream(netStream, false, (sender, certificate, chain, sslPolicyErrors) => true);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
            }, cancellationToken).ConfigureAwait(false);
            netStream = sslStream;
        }

        if (!string.IsNullOrEmpty(_password))
        {
            var authCmd = $"*2\r\n$4\r\nAUTH\r\n${Encoding.UTF8.GetByteCount(_password)}\r\n{_password}\r\n";
            var authBytes = Encoding.UTF8.GetBytes(authCmd);
            await netStream.WriteAsync(authBytes, cancellationToken).ConfigureAwait(false);
            await netStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await ReadSimpleStringAsync(netStream, cancellationToken).ConfigureAwait(false);
        }

        return netStream;
    }

    private static async Task<byte[]?> ReadBulkStringAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(line) || !line.StartsWith('$')) return null;

        if (!int.TryParse(line[1..], out var length) || length < 0)
            return null;

        var buffer = new byte[length];
        var totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            totalRead += read;
        }

        // Consume trailing CRLF
        var crlf = new byte[2];
        await stream.ReadExactlyAsync(crlf, cancellationToken).ConfigureAwait(false);

        return buffer;
    }

    private static async Task<string?> ReadSimpleStringAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(line)) return null;
        return line[0] is '+' or '-' ? line[1..] : line;
    }

    private static async Task<long?> ReadIntegerAsync(Stream stream, CancellationToken cancellationToken)
    {
        var line = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(line) || !line.StartsWith(':')) return null;
        return long.TryParse(line[1..], out var val) ? val : null;
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            var ch = (char)buf[0];
            if (ch == '\r')
            {
                var readLf = await stream.ReadAsync(buf, cancellationToken).ConfigureAwait(false);
                break;
            }
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static (string Host, int Port, string? Password, bool UseSsl) ParseConnectionString(string cs)
    {
        var parts = cs.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var hostPort = parts[0];
        string? password = null;
        var useSsl = false;

        var hpParts = hostPort.Split(':');
        var host = hpParts[0];
        var port = hpParts.Length > 1 && int.TryParse(hpParts[1], out var p) ? p : 6380;

        foreach (var part in parts.Skip(1))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim();
            var val = kv[1].Trim();

            if (string.Equals(key, "password", StringComparison.OrdinalIgnoreCase))
                password = val;
            else if (string.Equals(key, "ssl", StringComparison.OrdinalIgnoreCase))
                useSsl = bool.TryParse(val, out var s) && s;
        }

        if (port == 6380) useSsl = true;

        return (host, port, password, useSsl);
    }

    public void Dispose()
    {
    }
}
