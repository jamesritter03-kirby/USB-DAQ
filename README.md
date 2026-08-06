# USB DAQ - Avalonia (.NET 8)

Desktop DAQ application scaffold for a USB pressure transducer workflow, targeting model **612CSFCZ10FM** with a pressure span of **0 to 30000 psig**.

## What is included

- Avalonia desktop UI for discovery, connect/disconnect, start/stop acquisition, and live pressure display.
- Core DAQ abstractions for plugging in different hardware transports.
- Built-in discovery of serial COM devices (USB/Serial style endpoints).
- Simulation device for offline UI and pipeline testing.
- Pressure clamping and conversion helpers normalized to the configured transducer range.

## Solution structure

- `UsbDaq612.slnx`
- `UsbDaq.App` (Avalonia front end)
- `UsbDaq.Core` (device interfaces + acquisition primitives)

## Run

```powershell
dotnet build .\UsbDaq612.slnx
dotnet run --project .\UsbDaq.App\UsbDaq.App.csproj
```

## Hardware integration note (important)

This scaffold assumes your USB device is exposed as a line-based serial stream and that each line contains a pressure value in psig (examples: `15234.1` or `P=15234.1`).

If your 612CSFCZ10FM interface uses a custom USB HID/vendor protocol, replace the implementation in:

- `UsbDaq.Core/SerialLinePressureDevice.cs`

while keeping the same `IPressureDevice` contract.

## Calibration/range

Current sensor profile is configured in the app ViewModel as:

- Model: `612CSFCZ10FM`
- Min: `0 psig`
- Max: `30000 psig`

You can change this in `UsbDaq.App/ViewModels/MainWindowViewModel.cs`.
