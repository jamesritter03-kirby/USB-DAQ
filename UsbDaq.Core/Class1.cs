using System.Globalization;

namespace UsbDaq.Core;

public sealed record SensorSpecification(
	string Model,
	double MinPressurePsig,
	double MaxPressurePsig,
	string Units);

public sealed record DeviceDescriptor(
	string Id,
	string DisplayName,
	string Transport);

public sealed record PressureReading(
	DateTimeOffset Timestamp,
	double PressurePsig,
	string Units,
	string Source,
	string RawPayload);

public interface IPressureDevice : IAsyncDisposable
{
	DeviceDescriptor Descriptor { get; }
	bool IsConnected { get; }
	Task ConnectAsync(CancellationToken cancellationToken = default);
	Task DisconnectAsync(CancellationToken cancellationToken = default);
	Task<PressureReading> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IPressureDeviceFactory
{
	Task<IReadOnlyList<DeviceDescriptor>> DiscoverAsync(CancellationToken cancellationToken = default);
	IPressureDevice Create(DeviceDescriptor descriptor, SensorSpecification spec);
}

public static class PressureConversion
{
	public static double ClampPsig(double pressurePsig, SensorSpecification spec)
	{
		return Math.Clamp(pressurePsig, spec.MinPressurePsig, spec.MaxPressurePsig);
	}

	public static double NormalizedToPsig(double normalized, SensorSpecification spec)
	{
		var clamped = Math.Clamp(normalized, 0d, 1d);
		var span = spec.MaxPressurePsig - spec.MinPressurePsig;
		return spec.MinPressurePsig + (clamped * span);
	}

	public static bool TryParsePressure(string payload, out double pressurePsig)
	{
		if (double.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out pressurePsig))
		{
			return true;
		}

		var trimmed = payload.Trim();
		var numeric = new string(trimmed.Where(c => char.IsDigit(c) || c is '.' or '-' or '+').ToArray());
		return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out pressurePsig);
	}
}
