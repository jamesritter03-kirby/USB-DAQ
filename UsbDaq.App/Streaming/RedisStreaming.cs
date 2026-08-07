using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace UsbDaq.App.Streaming;

public sealed class RedisStreaming : IStreamingTarget
{
    private readonly string _connectionString;
    private readonly string _keyTemplate;
    private readonly bool _useStream;
    private readonly TimeSpan? _expiry;
    private ConnectionMultiplexer? _redis;
    private IDatabase? _db;

    public string Name => "Redis";
    public bool IsConnected => _redis?.IsConnected ?? false;

    public RedisStreaming(string connectionString, string keyTemplate, bool useStream = false, int expirySeconds = 0)
    {
        _connectionString = connectionString;
        _keyTemplate = keyTemplate;
        _useStream = useStream;
        _expiry = expirySeconds > 0 ? TimeSpan.FromSeconds(expirySeconds) : null;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(_connectionString);
        _db = _redis.GetDatabase();
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _redis?.Close();
        return Task.CompletedTask;
    }

    public async Task PublishAsync(string key, double valuePsig,
        DateTimeOffset timestamp, CancellationToken ct = default)
    {
        if (_db is null) return;
        if (_useStream)
        {
            await _db.StreamAddAsync(key, new NameValueEntry[]
            {
                new("value", valuePsig.ToString("F3", CultureInfo.InvariantCulture)),
                new("ts", timestamp.ToUnixTimeMilliseconds().ToString())
            });
        }
        else
        {
            await _db.StringSetAsync(key, valuePsig.ToString("F3", CultureInfo.InvariantCulture), _expiry);
        }
    }

    public ValueTask DisposeAsync()
    {
        _redis?.Dispose();
        return ValueTask.CompletedTask;
    }
}
