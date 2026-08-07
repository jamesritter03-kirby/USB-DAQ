using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace UsbDaq.App.Streaming;

public sealed class MqttStreaming : IStreamingTarget
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _topicTemplate;
    private readonly string? _username;
    private readonly string? _password;
    private IMqttClient? _client;

    public string Name => "MQTT";
    public bool IsConnected => _client?.IsConnected ?? false;

    public MqttStreaming(string host, int port, string topicTemplate,
        string? username = null, string? password = null)
    {
        _host = host;
        _port = port;
        _topicTemplate = topicTemplate;
        _username = username;
        _password = password;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var factory = new MqttFactory();
        _client = factory.CreateMqttClient();
        var optsBuilder = new MqttClientOptionsBuilder().WithTcpServer(_host, _port);
        if (!string.IsNullOrWhiteSpace(_username))
            optsBuilder = optsBuilder.WithCredentials(_username, _password ?? "");
        await _client.ConnectAsync(optsBuilder.Build(), ct);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_client?.IsConnected == true)
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), ct);
    }

    public async Task PublishAsync(string key, double valuePsig,
        DateTimeOffset timestamp, CancellationToken ct = default)
    {
        if (_client?.IsConnected != true) return;
        var payload = $"{{\"value\":{valuePsig:F3},\"ts\":{timestamp.ToUnixTimeMilliseconds()}}}";
        await _client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(key)
            .WithPayload(Encoding.UTF8.GetBytes(payload))
            .Build(), ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client?.IsConnected == true)
            await _client.DisconnectAsync();
        _client?.Dispose();
    }
}
