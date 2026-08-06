using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using UsbDaq.Core;

namespace UsbDaq.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly string[] Palette =
    {
        "#3B82F6", "#EF4444", "#10B981", "#F59E0B", "#A855F7", "#14B8A6", "#F97316", "#E11D48"
    };

    private readonly IPressureDeviceFactory _factory;
    private readonly SensorSpecification _sensorSpec;
    private readonly Dictionary<DeviceChannelViewModel, Task> _channelTasks = new();

    private CancellationTokenSource? _acquisitionCts;
    private DeviceDescriptor? _selectedAvailableDevice;
    private DeviceChannelViewModel? _selectedChannel;
    private bool _isBusy;
    private bool _isAcquiring;
    private bool _isRecording;
    private string _status = "Ready";
    private int _sampleIntervalMs = 250;
    private double _lowAlarmPsig = 500;
    private double _highAlarmPsig = 28000;
    private string _themeMode = "Default";
    private string _recordFilePath = string.Empty;
    private string _overallPressureText = "--.- psig";
    private bool _isAlarmActive;
    private string _graphMode = "Sliding Window";
    private bool _graphAutoFollow = true;
    private bool _showPointMarkers;

    public MainWindowViewModel()
    {
        _factory = new PressureDeviceFactory();
        _sensorSpec = new SensorSpecification("612CSFCZ10FM", 0, 30000, "psig");

        AvailableDevices = new ObservableCollection<DeviceDescriptor>();
        Channels = new ObservableCollection<DeviceChannelViewModel>();
        ThemeModes = new ObservableCollection<string>(new[] { "Default", "Light", "Dark" });
        GraphModes = new ObservableCollection<string>(new[] { "Sliding Window", "Strip Chart" });

        RecordFilePath = Path.Combine(Environment.CurrentDirectory, "captures", $"daq_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeviceDescriptor> AvailableDevices { get; }

    public ObservableCollection<DeviceChannelViewModel> Channels { get; }

    public ObservableCollection<string> ThemeModes { get; }

    public ObservableCollection<string> GraphModes { get; }

    public DeviceDescriptor? SelectedAvailableDevice
    {
        get => _selectedAvailableDevice;
        set
        {
            if (SetField(ref _selectedAvailableDevice, value))
            {
                OnPropertyChanged(nameof(CanAddChannel));
            }
        }
    }

    public DeviceChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (SetField(ref _selectedChannel, value))
            {
                OnPropertyChanged(nameof(HasSelectedChannel));
                OnPropertyChanged(nameof(CanRemoveChannel));
                OnPropertyChanged(nameof(CanConnectSelected));
                OnPropertyChanged(nameof(CanDisconnectSelected));
            }
        }
    }

    public string SensorSummary =>
        $"Model: {_sensorSpec.Model} | Range: {_sensorSpec.MinPressurePsig:F0} to {_sensorSpec.MaxPressurePsig:F0} {_sensorSpec.Units}";

    public double MinPressurePsig => _sensorSpec.MinPressurePsig;

    public double MaxPressurePsig => _sensorSpec.MaxPressurePsig;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanAddChannel));
                OnPropertyChanged(nameof(CanStartAcquisition));
                OnPropertyChanged(nameof(CanStopAcquisition));
            }
        }
    }

    public bool IsAcquiring
    {
        get => _isAcquiring;
        private set
        {
            if (SetField(ref _isAcquiring, value))
            {
                OnPropertyChanged(nameof(CanStartAcquisition));
                OnPropertyChanged(nameof(CanStopAcquisition));
            }
        }
    }

    public bool IsRecording
    {
        get => _isRecording;
        set => SetField(ref _isRecording, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public int SampleIntervalMs
    {
        get => _sampleIntervalMs;
        set => SetField(ref _sampleIntervalMs, Math.Clamp(value, 50, 3000));
    }

    public double LowAlarmPsig
    {
        get => _lowAlarmPsig;
        set => SetField(ref _lowAlarmPsig, Math.Clamp(value, MinPressurePsig, MaxPressurePsig));
    }

    public double HighAlarmPsig
    {
        get => _highAlarmPsig;
        set => SetField(ref _highAlarmPsig, Math.Clamp(value, MinPressurePsig, MaxPressurePsig));
    }

    public bool IsAlarmActive
    {
        get => _isAlarmActive;
        private set => SetField(ref _isAlarmActive, value);
    }

    public string ThemeMode
    {
        get => _themeMode;
        set => SetField(ref _themeMode, value);
    }

    public string RecordFilePath
    {
        get => _recordFilePath;
        set => SetField(ref _recordFilePath, value);
    }

    public string OverallPressureText
    {
        get => _overallPressureText;
        private set => SetField(ref _overallPressureText, value);
    }

    public string GraphMode
    {
        get => _graphMode;
        set => SetField(ref _graphMode, value);
    }

    public bool GraphAutoFollow
    {
        get => _graphAutoFollow;
        set => SetField(ref _graphAutoFollow, value);
    }

    public bool ShowPointMarkers
    {
        get => _showPointMarkers;
        set => SetField(ref _showPointMarkers, value);
    }

    public bool HasSelectedChannel => SelectedChannel is not null;

    public bool CanAddChannel => !IsBusy && SelectedAvailableDevice is not null;

    public bool CanRemoveChannel => !IsBusy && SelectedChannel is not null && !IsAcquiring;

    public bool CanStartAcquisition => !IsBusy && !IsAcquiring && Channels.Any(c => c.IsConnected);

    public bool CanStopAcquisition => IsAcquiring;

    public bool CanConnectSelected => SelectedChannel is not null && !SelectedChannel.IsConnected && !IsBusy;

    public bool CanDisconnectSelected => SelectedChannel is not null && SelectedChannel.IsConnected && !IsAcquiring && !IsBusy;

    public async Task RefreshDevicesAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            AvailableDevices.Clear();
            var discovered = await _factory.DiscoverAsync(cancellationToken);
            foreach (var descriptor in discovered)
            {
                AvailableDevices.Add(descriptor);
            }

            SelectedAvailableDevice = AvailableDevices.FirstOrDefault();
            Status = $"Discovered {AvailableDevices.Count} device(s).";
            OnPropertyChanged(nameof(CanAddChannel));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void AddSelectedDeviceChannel()
    {
        if (SelectedAvailableDevice is null)
        {
            Status = "Select a device first.";
            return;
        }

        var selectedIsSimulation = IsSimulationDescriptor(SelectedAvailableDevice);

        if (!selectedIsSimulation && Channels.Any(c => c.Descriptor.Id.Equals(SelectedAvailableDevice.Id, StringComparison.OrdinalIgnoreCase)))
        {
            Status = "That device is already in the channel list.";
            return;
        }

        var descriptor = selectedIsSimulation
            ? CreateNextSimulatedDescriptor(SelectedAvailableDevice)
            : SelectedAvailableDevice;

        var color = Palette[Channels.Count % Palette.Length];
        var channel = new DeviceChannelViewModel(descriptor, _sensorSpec, color)
        {
            Status = "Added"
        };

        Channels.Add(channel);
        SelectedChannel = channel;
        Status = $"Added {channel.DisplayName}.";
    }

    private DeviceDescriptor CreateNextSimulatedDescriptor(DeviceDescriptor template)
    {
        var index = Channels.Count(c => IsSimulationDescriptor(c.Descriptor)) + 1;
        return new DeviceDescriptor(
            $"SIMULATED-{index}",
            $"{template.DisplayName} #{index}",
            template.Transport);
    }

    private static bool IsSimulationDescriptor(DeviceDescriptor descriptor)
    {
        return descriptor.Transport.Equals("Simulation", StringComparison.OrdinalIgnoreCase)
            || descriptor.Id.StartsWith("SIMULATED", StringComparison.OrdinalIgnoreCase);
    }

    public async Task RemoveSelectedChannelAsync(CancellationToken cancellationToken = default)
    {
        var channel = SelectedChannel;
        if (channel is null)
        {
            return;
        }

        if (channel.IsConnected)
        {
            await DisconnectChannelAsync(channel, cancellationToken);
        }

        Channels.Remove(channel);
        SelectedChannel = Channels.FirstOrDefault();
        Status = "Channel removed.";
    }

    public Task ConnectSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedChannel is null)
        {
            return Task.CompletedTask;
        }

        return ConnectChannelAsync(SelectedChannel, cancellationToken);
    }

    public Task DisconnectSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedChannel is null)
        {
            return Task.CompletedTask;
        }

        return DisconnectChannelAsync(SelectedChannel, cancellationToken);
    }

    public async Task ConnectAllAsync(CancellationToken cancellationToken = default)
    {
        if (Channels.Count == 0)
        {
            if (SelectedAvailableDevice is null)
            {
                Status = "Select a device or add channels first.";
                return;
            }

            AddSelectedDeviceChannel();
        }

        var connectedCountBefore = Channels.Count(c => c.IsConnected);
        foreach (var channel in Channels.Where(c => !c.IsConnected))
        {
            await ConnectChannelAsync(channel, cancellationToken);
        }

        var connectedCountAfter = Channels.Count(c => c.IsConnected);
        if (connectedCountAfter == connectedCountBefore)
        {
            Status = "No additional channels were connected.";
        }
        else
        {
            Status = $"Connected {connectedCountAfter} channel(s).";
        }
    }

    public async Task DisconnectAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var channel in Channels.Where(c => c.IsConnected).ToList())
        {
            await DisconnectChannelAsync(channel, cancellationToken);
        }
    }

    public async Task StartAcquisitionAsync()
    {
        if (IsAcquiring)
        {
            return;
        }

        var activeChannels = Channels.Where(c => c.IsConnected).ToList();
        if (activeChannels.Count == 0)
        {
            Status = "Connect at least one device channel first.";
            return;
        }

        if (LowAlarmPsig >= HighAlarmPsig)
        {
            Status = "Low alarm must be lower than high alarm.";
            return;
        }

        IsAcquiring = true;
        _acquisitionCts = new CancellationTokenSource();
        _channelTasks.Clear();

        if (IsRecording)
        {
            EnsureRecordFile();
        }

        foreach (var channel in activeChannels)
        {
            channel.IsAcquiring = true;
            var task = RunChannelLoopAsync(channel, _acquisitionCts.Token);
            _channelTasks[channel] = task;
        }

        Status = $"Streaming {activeChannels.Count} channel(s).";
        await Task.CompletedTask;
    }

    public async Task StopAcquisitionAsync()
    {
        if (!IsAcquiring)
        {
            return;
        }

        _acquisitionCts?.Cancel();
        var tasks = _channelTasks.Values.ToArray();
        _channelTasks.Clear();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            // Best-effort stop for all loops.
        }

        foreach (var channel in Channels)
        {
            channel.IsAcquiring = false;
        }

        _acquisitionCts?.Dispose();
        _acquisitionCts = null;
        IsAcquiring = false;
        Status = "Acquisition stopped.";
    }

    public void ClearAllSamples()
    {
        foreach (var channel in Channels)
        {
            channel.ClearSamples();
        }

        IsAlarmActive = false;
        OverallPressureText = "--.- psig";
        Status = "Samples cleared.";
    }

    private async Task ConnectChannelAsync(DeviceChannelViewModel channel, CancellationToken cancellationToken)
    {
        if (channel.IsConnected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var device = _factory.Create(channel.Descriptor, _sensorSpec);
            await device.ConnectAsync(cancellationToken);
            channel.Attach(device);
            channel.IsConnected = true;
            channel.Status = "Connected";
            Status = $"Connected {channel.DisplayName}.";
        }
        catch (Exception ex)
        {
            channel.Status = $"Error: {ex.Message}";
            Status = $"Connect failed for {channel.DisplayName}.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanStartAcquisition));
            OnPropertyChanged(nameof(CanConnectSelected));
            OnPropertyChanged(nameof(CanDisconnectSelected));
        }
    }

    private async Task DisconnectChannelAsync(DeviceChannelViewModel channel, CancellationToken cancellationToken)
    {
        var device = channel.GetDevice();
        if (device is null)
        {
            channel.IsConnected = false;
            return;
        }

        IsBusy = true;
        try
        {
            await device.DisconnectAsync(cancellationToken);
            await device.DisposeAsync();
            channel.Attach(null);
            channel.IsConnected = false;
            channel.IsAcquiring = false;
            channel.Status = "Disconnected";
            Status = $"Disconnected {channel.DisplayName}.";
        }
        catch (Exception ex)
        {
            channel.Status = $"Disconnect error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanStartAcquisition));
            OnPropertyChanged(nameof(CanConnectSelected));
            OnPropertyChanged(nameof(CanDisconnectSelected));
        }
    }

    private async Task RunChannelLoopAsync(DeviceChannelViewModel channel, CancellationToken token)
    {
        var device = channel.GetDevice();
        if (device is null)
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            try
            {
                var reading = await device.ReadAsync(token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    channel.AddSample(reading);
                    channel.IsAlarm = reading.PressurePsig < LowAlarmPsig || reading.PressurePsig > HighAlarmPsig;
                    UpdateAggregates();
                });

                if (IsRecording)
                {
                    await AppendRecordAsync(channel, reading, token);
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    channel.Status = $"Read error: {ex.Message}";
                });
            }

            await Task.Delay(SampleIntervalMs, token);
        }

        await Dispatcher.UIThread.InvokeAsync(() => channel.IsAcquiring = false);
    }

    private void UpdateAggregates()
    {
        var connected = Channels.Where(c => c.IsConnected).ToList();
        if (connected.Count == 0)
        {
            OverallPressureText = "--.- psig";
            IsAlarmActive = false;
            return;
        }

        var avg = connected.Average(c => c.CurrentPressure);
        OverallPressureText = $"{avg:F1} {_sensorSpec.Units}";
        IsAlarmActive = connected.Any(c => c.IsAlarm);

        if (IsAlarmActive)
        {
            Status = "Alarm active: one or more channels out of threshold.";
        }
    }

    private void EnsureRecordFile()
    {
        if (string.IsNullOrWhiteSpace(RecordFilePath))
        {
            RecordFilePath = Path.Combine(Environment.CurrentDirectory, "captures", $"daq_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }

        var directory = Path.GetDirectoryName(RecordFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (!File.Exists(RecordFilePath))
        {
            File.WriteAllText(RecordFilePath, "timestamp,device,pressure_psig,units,raw_payload" + Environment.NewLine, Encoding.UTF8);
        }
    }

    private Task AppendRecordAsync(DeviceChannelViewModel channel, PressureReading reading, CancellationToken token)
    {
        var safePayload = reading.RawPayload.Replace("\"", "\"\"");
        var line = string.Create(CultureInfo.InvariantCulture, $"{reading.Timestamp:O},\"{channel.DisplayName}\",{reading.PressurePsig:F3},{reading.Units},\"{safePayload}\"");
        return File.AppendAllTextAsync(RecordFilePath, line + Environment.NewLine, Encoding.UTF8, token);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (propertyName is null)
        {
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
