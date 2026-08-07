using System.IO.Ports;

namespace UsbDaq.Core;

public sealed class PressureDeviceFactory : IPressureDeviceFactory
{
    public static readonly DeviceDescriptor Simulated = new(
        "SIMULATED",
        "Simulated USB Pressure Device",
        "Simulation");

    public Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<DeviceDescriptor> { Simulated };

        foreach (var portName in SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            devices.Add(new DeviceDescriptor(portName, $"USB/Serial {portName}", "Serial"));
        }

        return Task.FromResult<IReadOnlyList<DeviceDescriptor>>(devices);
    }

    public IPressureDevice Create(DeviceDescriptor descriptor, SensorSpecification spec,
        SerialProtocolDefinition? protocol = null, int stationNumber = 1)
    {
        if (descriptor.Transport.Equals("Simulation", StringComparison.OrdinalIgnoreCase)
            || descriptor.Id.StartsWith(Simulated.Id, StringComparison.OrdinalIgnoreCase))
        {
            return new SimulationPressureDevice(descriptor, spec);
        }

        return new SerialLinePressureDevice(descriptor, spec, protocol, stationNumber);
    }
}
