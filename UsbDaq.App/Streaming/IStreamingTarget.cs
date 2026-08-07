using System;
using System.Threading;
using System.Threading.Tasks;

namespace UsbDaq.App.Streaming;

public interface IStreamingTarget : IAsyncDisposable
{
    string Name { get; }
    bool IsConnected { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    Task PublishAsync(string key, double valuePsig, DateTimeOffset timestamp, CancellationToken ct = default);
}
