namespace UsbDaq.Core;

public sealed class SimulationPressureDevice : IPressureDevice
{
    private readonly SensorSpecification _spec;
    private readonly Random _random = new();
    private double _phase;

    public SimulationPressureDevice(DeviceDescriptor descriptor, SensorSpecification spec)
    {
        Descriptor = descriptor;
        _spec = spec;
    }

    public DeviceDescriptor Descriptor { get; }

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task<PressureReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Device is not connected.");
        }

        _phase += 0.14;
        var normalized = (Math.Sin(_phase) + 1d) / 2d;
        var jitter = (_random.NextDouble() - 0.5d) * 0.03d;
        var psig = PressureConversion.NormalizedToPsig(normalized + jitter, _spec);

        return Task.FromResult(new PressureReading(
            DateTimeOffset.UtcNow,
            PressureConversion.ClampPsig(psig, _spec),
            _spec.Units,
            Descriptor.DisplayName,
            $"sim:{normalized:F4}"));
    }

    public ValueTask DisposeAsync()
    {
        IsConnected = false;
        return ValueTask.CompletedTask;
    }
}
