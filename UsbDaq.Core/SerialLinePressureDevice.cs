using System.IO.Ports;

namespace UsbDaq.Core;

public sealed class SerialLinePressureDevice : IPressureDevice
{
    private readonly SensorSpecification _spec;
    private readonly SerialPort _port;

    public SerialLinePressureDevice(DeviceDescriptor descriptor, SensorSpecification spec, int baudRate = 9600)
    {
        Descriptor = descriptor;
        _spec = spec;
        _port = new SerialPort(descriptor.Id, baudRate)
        {
            NewLine = "\n",
            ReadTimeout = 500,
            WriteTimeout = 500,
        };
    }

    public DeviceDescriptor Descriptor { get; }

    public bool IsConnected => _port.IsOpen;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
        {
            _port.Open();
        }

        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_port.IsOpen)
        {
            _port.Close();
        }

        return Task.CompletedTask;
    }

    public Task<PressureReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
        {
            throw new InvalidOperationException("Serial device is not connected.");
        }

        var line = _port.ReadLine().Trim();
        if (!PressureConversion.TryParsePressure(line, out var parsedPsig))
        {
            throw new FormatException($"Unable to parse pressure payload '{line}'.");
        }

        return Task.FromResult(new PressureReading(
            DateTimeOffset.UtcNow,
            PressureConversion.ClampPsig(parsedPsig, _spec),
            _spec.Units,
            Descriptor.DisplayName,
            line));
    }

    public ValueTask DisposeAsync()
    {
        if (_port.IsOpen)
        {
            _port.Close();
        }

        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
