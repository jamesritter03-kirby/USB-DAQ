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

```bash
dotnet build UsbDaq612.slnx
dotnet run --project UsbDaq.App/UsbDaq.App.csproj
```

The app is built on Avalonia and runs on **Windows**, **macOS**, and **Linux**.

## Publish (self-contained builds)

Each platform gets its own self-contained package (no .NET install required on the
target machine). Publish for any supported runtime with:

```bash
# Windows (x64)
dotnet publish UsbDaq.App/UsbDaq.App.csproj -c Release -r win-x64   --self-contained true -o publish/win-x64

# Linux (x64)
dotnet publish UsbDaq.App/UsbDaq.App.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64

# macOS (Intel)
dotnet publish UsbDaq.App/UsbDaq.App.csproj -c Release -r osx-x64   --self-contained true -o publish/osx-x64

# macOS (Apple Silicon)
dotnet publish UsbDaq.App/UsbDaq.App.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
```

On macOS/Linux the launcher is `UsbDaq.App` (mark it executable with
`chmod +x UsbDaq.App` if needed). On Windows it is `UsbDaq.App.exe`.

Pushing a `v*` git tag triggers the release workflow, which publishes all four
platform packages (plus a one-click Windows installer) to a GitHub Release.


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
