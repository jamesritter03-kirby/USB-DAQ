using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UsbDaq.App.Streaming;

public sealed class ThingsBoardStreaming : IStreamingTarget
{
    private readonly string _host;
    private readonly bool _useHttps;
    private readonly string _accessToken;
    private readonly string _pathPrefix;
    private readonly HttpClient _http;

    public string Name => "ThingsBoard";
    public bool IsConnected { get; private set; }

    public ThingsBoardStreaming(string host, bool useHttps, string accessToken, string pathPrefix = "")
    {
        _host = host;
        _useHttps = useHttps;
        _accessToken = accessToken;
        _pathPrefix = pathPrefix.TrimEnd('/');
        // Accept any cert for dev/local deployments
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        // POST empty telemetry — the correct connectivity test for ThingsBoard
        var scheme = _useHttps ? "https" : "http";
        try
        {
            var resp = await _http.PostAsync(
                $"{scheme}://{_host}{_pathPrefix}/api/v1/{_accessToken}/telemetry",
                new StringContent("{}", Encoding.UTF8, "application/json"),
                ct);
            IsConnected = resp.IsSuccessStatusCode;
            if (!IsConnected)
                throw new InvalidOperationException(
                    $"HTTP {(int)resp.StatusCode} from {scheme}://{_host}{_pathPrefix}/api/v1/.../telemetry — check host, path prefix, and access token.");
        }
        catch (HttpRequestException ex)
        {
            IsConnected = false;
            throw new InvalidOperationException(
                $"Could not reach {scheme}://{_host}{_pathPrefix}/: {ex.Message}", ex);
        }
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public async Task PublishAsync(string key, double valuePsig,
        DateTimeOffset timestamp, CancellationToken ct = default)
    {
        if (!IsConnected) return;
        await PublishToTokenAsync(_accessToken, key, valuePsig, timestamp, ct);
    }

    // Publish to a different device identified by tokenOverride
    public async Task PublishToTokenAsync(string token, string key, double valuePsig,
        DateTimeOffset timestamp, CancellationToken ct = default)
    {
        var scheme = _useHttps ? "https" : "http";
        var payload = $"{{\"ts\":{timestamp.ToUnixTimeMilliseconds()},\"values\":{{\"{key}\":{valuePsig:F3}}}}}";
        await _http.PostAsync(
            $"{scheme}://{_host}{_pathPrefix}/api/v1/{token}/telemetry",
            new StringContent(payload, Encoding.UTF8, "application/json"),
            ct);
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask;
    }
}
