using System.IO.Ports;
using System.Text.RegularExpressions;

namespace UsbDaq.Core;

public sealed class SerialLinePressureDevice : IPressureDevice
{
    private readonly SensorSpecification _spec;
    private readonly SerialProtocolDefinition _protocol;
    private readonly int _stationNumber;
    private readonly SerialPort _port;

    public SerialLinePressureDevice(
        DeviceDescriptor descriptor,
        SensorSpecification spec,
        SerialProtocolDefinition? protocol = null,
        int stationNumber = 1)
    {
        Descriptor = descriptor;
        _spec = spec;
        _protocol = protocol ?? SerialProtocolDefinition.Gp50Poll;
        _stationNumber = stationNumber;
        _port = new SerialPort(descriptor.Id, _protocol.BaudRate)
        {
            NewLine = _protocol.NewLine,
            ReadTimeout = 1000,
            WriteTimeout = 500,
        };
    }

    public DeviceDescriptor Descriptor { get; }

    public bool IsConnected => _port.IsOpen;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
            _port.Open();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_port.IsOpen)
            _port.Close();
        return Task.CompletedTask;
    }

    public Task<PressureReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!_port.IsOpen)
            throw new InvalidOperationException("Serial device is not connected.");

        if (_protocol.RequestTemplate is not null)
        {
            var request = _protocol.RequestTemplate.Replace("{station}", _stationNumber.ToString("D3"));
            _port.Write(request);
        }

        var line = _port.ReadLine().Trim();

        var match = Regex.Match(line, _protocol.ValuePattern);
        if (!match.Success || !PressureConversion.TryParsePressure(match.Value, out var parsedPsig))
            throw new FormatException($"Unable to parse pressure from '{line}'.");

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
            _port.Close();
        _port.Dispose();
        return ValueTask.CompletedTask;
    }
}
