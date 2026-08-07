using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using UsbDaq.App.Models;
using UsbDaq.App.Streaming;
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
    private bool _showLegend = true;
    private bool _showCursors = true;
    private bool _showAlarmLines = true;
    private SerialProtocolDefinition _defaultProtocol = SerialProtocolDefinition.Gp50Poll;
    private string _customProtocolName = "My Protocol";
    private string _customProtocolBaud = "9600";
    private string _customProtocolRequest = string.Empty;
    private string _customProtocolTerminator = "LF";
    private string _customProtocolPattern = @"[+\-]?[\d]*\.?[\d]+";
    private bool _stackedPlots;
    private bool _cursorSnapToData;
    private int _historyDurationSecs = 120;

    // MQTT streaming config
    private bool _mqttEnabled;
    private string _mqttHost = "localhost";
    private string _mqttPort = "1883";
    private string _mqttTopic = "daq/{channel}";
    private string _mqttUsername = "";
    private string _mqttPassword = "";
    private int _mqttPublishIntervalMs;
    private MqttStreaming? _mqttTarget;

    // Redis streaming config
    private bool _redisEnabled;
    private string _redisConnStr = "localhost:6379";
    private string _redisKey = "daq:{channel}";
    private bool _redisStream;
    private int _redisPublishIntervalMs;
    private int _redisExpirySeconds;
    private RedisStreaming? _redisTarget;

    // ThingsBoard streaming config
    private bool _tbEnabled;
    private string _tbHost = "demo.thingsboard.io";
    private bool _tbHttps = true;
    private string _tbToken = "";
    private string _tbKeyTemplate = "{channel}";
    private string _tbPathPrefix = "";
    private int _tbPublishIntervalMs;
    private ThingsBoardStreaming? _tbTarget;

    public MainWindowViewModel()
    {
        _factory = new PressureDeviceFactory();
        _sensorSpec = new SensorSpecification("612CSFCZ10FM", 0, 30000, "psig");

        AvailableDevices = new ObservableCollection<DeviceDescriptor>();
        Channels = new ObservableCollection<DeviceChannelViewModel>();
        ThemeModes = new ObservableCollection<string>(new[] { "Default", "Light", "Dark" });
        GraphModes = new ObservableCollection<string>(new[] { "Sliding Window", "Strip Chart" });

        RecordFilePath = Path.Combine(Environment.CurrentDirectory, "captures", $"daq_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        Channels.CollectionChanged += (_, _) => OnPropertyChanged(nameof(AcquisitionStatusLabel));

        AvailableProtocols = new ObservableCollection<SerialProtocolDefinition>(SerialProtocolDefinition.Presets);
        TerminatorOptions = new ObservableCollection<string> { "CR", "LF", "CRLF" };
        RefreshProfileNames();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeviceDescriptor> AvailableDevices { get; }

    public ObservableCollection<DeviceChannelViewModel> Channels { get; }

    public ObservableCollection<string> ThemeModes { get; }

    public ObservableCollection<string> GraphModes { get; }

    public ObservableCollection<SerialProtocolDefinition> AvailableProtocols { get; }

    public ObservableCollection<string> TerminatorOptions { get; }

    public SerialProtocolDefinition DefaultProtocol
    {
        get => _defaultProtocol;
        set => SetField(ref _defaultProtocol, value);
    }

    // Custom protocol builder fields
    public string CustomProtocolName
    {
        get => _customProtocolName;
        set => SetField(ref _customProtocolName, value);
    }

    public string CustomProtocolBaud
    {
        get => _customProtocolBaud;
        set => SetField(ref _customProtocolBaud, value);
    }

    public string CustomProtocolRequest
    {
        get => _customProtocolRequest;
        set => SetField(ref _customProtocolRequest, value);
    }

    public string CustomProtocolTerminator
    {
        get => _customProtocolTerminator;
        set => SetField(ref _customProtocolTerminator, value);
    }

    public string CustomProtocolPattern
    {
        get => _customProtocolPattern;
        set => SetField(ref _customProtocolPattern, value);
    }

    public bool StackedPlots
    {
        get => _stackedPlots;
        set => SetField(ref _stackedPlots, value);
    }

    public bool CursorSnapToData
    {
        get => _cursorSnapToData;
        set => SetField(ref _cursorSnapToData, value);
    }

    public int HistoryDurationSecs
    {
        get => _historyDurationSecs;
        set => SetField(ref _historyDurationSecs, Math.Clamp(value, 10, 3600));
    }

    // Ring buffer depth computed from history duration + current sample rate
    private int MaxSamples => Math.Max(60, (int)(_historyDurationSecs * 1000.0 / Math.Max(1, _sampleIntervalMs)));

    // ── MQTT ──
    public bool MqttEnabled { get => _mqttEnabled; set => SetField(ref _mqttEnabled, value); }
    public string MqttHost { get => _mqttHost; set => SetField(ref _mqttHost, value); }
    public string MqttPort { get => _mqttPort; set => SetField(ref _mqttPort, value); }
    public string MqttTopic { get => _mqttTopic; set => SetField(ref _mqttTopic, value); }
    public string MqttUsername { get => _mqttUsername; set => SetField(ref _mqttUsername, value); }
    public string MqttPassword { get => _mqttPassword; set => SetField(ref _mqttPassword, value); }
    public int MqttPublishIntervalMs { get => _mqttPublishIntervalMs; set => SetField(ref _mqttPublishIntervalMs, Math.Max(0, value)); }
    public bool MqttConnected => _mqttTarget?.IsConnected ?? false;

    public async Task ConnectMqttAsync()
    {
        try
        {
            if (_mqttTarget is not null) await _mqttTarget.DisposeAsync();
            if (!int.TryParse(_mqttPort, out var port)) port = 1883;
            _mqttTarget = new MqttStreaming(_mqttHost, port, _mqttTopic,
                string.IsNullOrWhiteSpace(_mqttUsername) ? null : _mqttUsername,
                string.IsNullOrWhiteSpace(_mqttPassword) ? null : _mqttPassword);
            await _mqttTarget.ConnectAsync();
            OnPropertyChanged(nameof(MqttConnected));
            Status = "MQTT connected.";
        }
        catch (Exception ex) { Status = $"MQTT error: {ex.Message}"; }
    }

    public async Task DisconnectMqttAsync()
    {
        if (_mqttTarget is not null) { await _mqttTarget.DisconnectAsync(); OnPropertyChanged(nameof(MqttConnected)); }
        Status = "MQTT disconnected.";
    }

    // ── Redis ──
    public bool RedisEnabled { get => _redisEnabled; set => SetField(ref _redisEnabled, value); }
    public string RedisConnStr { get => _redisConnStr; set => SetField(ref _redisConnStr, value); }
    public string RedisKey { get => _redisKey; set => SetField(ref _redisKey, value); }
    public bool RedisStream { get => _redisStream; set => SetField(ref _redisStream, value); }
    public int RedisPublishIntervalMs { get => _redisPublishIntervalMs; set => SetField(ref _redisPublishIntervalMs, Math.Max(0, value)); }
    public int RedisExpirySeconds { get => _redisExpirySeconds; set => SetField(ref _redisExpirySeconds, Math.Max(0, value)); }
    public bool RedisConnected => _redisTarget?.IsConnected ?? false;

    public async Task ConnectRedisAsync()
    {
        try
        {
            if (_redisTarget is not null) await _redisTarget.DisposeAsync();
            _redisTarget = new RedisStreaming(_redisConnStr, _redisKey, _redisStream, _redisExpirySeconds);
            await _redisTarget.ConnectAsync();
            OnPropertyChanged(nameof(RedisConnected));
            Status = "Redis connected.";
        }
        catch (Exception ex) { Status = $"Redis error: {ex.Message}"; }
    }

    public async Task DisconnectRedisAsync()
    {
        if (_redisTarget is not null) { await _redisTarget.DisconnectAsync(); OnPropertyChanged(nameof(RedisConnected)); }
        Status = "Redis disconnected.";
    }

    // ── ThingsBoard ──
    public bool TbEnabled { get => _tbEnabled; set => SetField(ref _tbEnabled, value); }
    public string TbHost
    {
        get => _tbHost;
        set { if (SetField(ref _tbHost, value)) OnPropertyChanged(nameof(TbPreviewUrl)); }
    }
    public bool TbHttps
    {
        get => _tbHttps;
        set { if (SetField(ref _tbHttps, value)) OnPropertyChanged(nameof(TbPreviewUrl)); }
    }
    public string TbToken
    {
        get => _tbToken;
        set { if (SetField(ref _tbToken, value)) OnPropertyChanged(nameof(TbPreviewUrl)); }
    }
    public string TbKeyTemplate { get => _tbKeyTemplate; set => SetField(ref _tbKeyTemplate, value); }
    public string TbPathPrefix
    {
        get => _tbPathPrefix;
        set { if (SetField(ref _tbPathPrefix, value)) OnPropertyChanged(nameof(TbPreviewUrl)); }
    }
    public int TbPublishIntervalMs { get => _tbPublishIntervalMs; set => SetField(ref _tbPublishIntervalMs, Math.Max(0, value)); }
    public bool TbConnected => _tbTarget?.IsConnected ?? false;

    // Shows the exact URL that will be called
    public string TbPreviewUrl =>
        $"{(_tbHttps ? "https" : "http")}://{_tbHost}{_tbPathPrefix.TrimEnd('/')}/api/v1/{{token}}/telemetry";

    public async Task ConnectThingsBoardAsync()
    {
        try
        {
            if (_tbTarget is not null) await _tbTarget.DisposeAsync();
            _tbTarget = new ThingsBoardStreaming(_tbHost, _tbHttps, _tbToken, _tbPathPrefix);
            await _tbTarget.ConnectAsync();
            OnPropertyChanged(nameof(TbConnected));
            Status = "ThingsBoard connected.";
        }
        catch (Exception ex) { Status = $"ThingsBoard error: {ex.Message}"; }
    }

    public async Task DisconnectThingsBoardAsync()
    {
        if (_tbTarget is not null) { await _tbTarget.DisconnectAsync(); OnPropertyChanged(nameof(TbConnected)); }
        Status = "ThingsBoard disconnected.";
    }

    // ── Profiles ─────────────────────────────────────────────

    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsbDaq", "profiles");

    private string _newProfileName = "Default";

    public ObservableCollection<string> SavedProfileNames { get; } = new();

    public string NewProfileName
    {
        get => _newProfileName;
        set => SetField(ref _newProfileName, value);
    }

    public void RefreshProfileNames()
    {
        SavedProfileNames.Clear();
        if (!Directory.Exists(ProfilesDir)) return;
        foreach (var f in Directory.GetFiles(ProfilesDir, "*.json").OrderBy(x => x))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (!name.StartsWith("_")) // hide internal auto-save profiles
                SavedProfileNames.Add(name);
        }
    }

    public async Task SaveProfileAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) { Status = "Profile name cannot be empty."; return; }
        Directory.CreateDirectory(ProfilesDir);
        var profile = new DaqProfile
        {
            Name = name,
            Created = DateTime.Now,
            SampleIntervalMs = SampleIntervalMs,
            HistoryDurationSecs = HistoryDurationSecs,
            LowAlarmPsig = LowAlarmPsig,
            HighAlarmPsig = HighAlarmPsig,
            GraphMode = GraphMode,
            StackedPlots = StackedPlots,
            ShowLegend = ShowLegend,
            ShowCursors = ShowCursors,
            ShowAlarmLines = ShowAlarmLines,
            CursorSnapToData = CursorSnapToData,
            ShowPointMarkers = ShowPointMarkers,
            GraphAutoFollow = GraphAutoFollow,
            MqttHost = MqttHost, MqttPort = MqttPort, MqttTopic = MqttTopic,
            MqttUsername = MqttUsername, MqttPassword = MqttPassword, MqttPublishIntervalMs = MqttPublishIntervalMs,
            RedisConnStr = RedisConnStr, RedisKey = RedisKey, RedisStream = RedisStream, RedisPublishIntervalMs = RedisPublishIntervalMs, RedisExpirySeconds = RedisExpirySeconds,
            TbHost = TbHost, TbHttps = TbHttps, TbToken = TbToken, TbKeyTemplate = TbKeyTemplate, TbPathPrefix = TbPathPrefix, TbPublishIntervalMs = TbPublishIntervalMs,
            DefaultProtocolName = DefaultProtocol.Name,
            Channels = Channels.Select(c => new ChannelEntry
            {
                DeviceId = c.Descriptor.Id,
                DeviceDisplayName = c.Descriptor.DisplayName,
                DeviceTransport = c.Descriptor.Transport,
                SignalName = c.SignalName,
                ColorHex = c.ColorHex,
                ProtocolName = c.Protocol.Name,
                StationNumber = c.StationNumber,
                IsVisible = c.IsVisible,
                MqttTopicOverride = c.MqttTopicOverride,
                RedisKeyOverride = c.RedisKeyOverride,
                TbKeyOverride = c.TbKeyOverride,
                TbTokenOverride = c.TbTokenOverride,
            }).ToList()
        };
        var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(ProfilesDir, $"{name}.json"), json);
        RefreshProfileNames();
        NewProfileName = name;
        Status = $"Profile '{name}' saved.";
    }

    public async Task LoadProfileAsync(string name)
    {
        var file = Path.Combine(ProfilesDir, $"{name}.json");
        if (!File.Exists(file)) { Status = $"Profile '{name}' not found."; return; }
        var json = await File.ReadAllTextAsync(file);
        var p = JsonSerializer.Deserialize<DaqProfile>(json);
        if (p is null) return;

        SampleIntervalMs = p.SampleIntervalMs;
        HistoryDurationSecs = p.HistoryDurationSecs;
        LowAlarmPsig = p.LowAlarmPsig;
        HighAlarmPsig = p.HighAlarmPsig;
        GraphMode = p.GraphMode;
        StackedPlots = p.StackedPlots;
        ShowLegend = p.ShowLegend;
        ShowCursors = p.ShowCursors;
        ShowAlarmLines = p.ShowAlarmLines;
        CursorSnapToData = p.CursorSnapToData;
        ShowPointMarkers = p.ShowPointMarkers;
        GraphAutoFollow = p.GraphAutoFollow;
        MqttHost = p.MqttHost; MqttPort = p.MqttPort; MqttTopic = p.MqttTopic;
        MqttUsername = p.MqttUsername; MqttPassword = p.MqttPassword; MqttPublishIntervalMs = p.MqttPublishIntervalMs;
        RedisConnStr = p.RedisConnStr; RedisKey = p.RedisKey; RedisStream = p.RedisStream; RedisPublishIntervalMs = p.RedisPublishIntervalMs; RedisExpirySeconds = p.RedisExpirySeconds;
        TbHost = p.TbHost; TbHttps = p.TbHttps; TbToken = p.TbToken; TbKeyTemplate = p.TbKeyTemplate; TbPathPrefix = p.TbPathPrefix; TbPublishIntervalMs = p.TbPublishIntervalMs;
        DefaultProtocol = AvailableProtocols.FirstOrDefault(x => x.Name == p.DefaultProtocolName)
            ?? SerialProtocolDefinition.Gp50Poll;

        if (!IsAcquiring)
        {
            foreach (var ch in Channels.ToList())
                await RemoveChannelAsync(ch);
            foreach (var entry in p.Channels)
            {
                var desc = new DeviceDescriptor(entry.DeviceId, entry.DeviceDisplayName, entry.DeviceTransport);
                var proto = AvailableProtocols.FirstOrDefault(x => x.Name == entry.ProtocolName) ?? DefaultProtocol;
                var channel = new DeviceChannelViewModel(desc, _sensorSpec, entry.ColorHex, proto, entry.StationNumber)
                {
                    SignalName = entry.SignalName,
                    IsVisible = entry.IsVisible,
                    MqttTopicOverride = entry.MqttTopicOverride,
                    RedisKeyOverride = entry.RedisKeyOverride,
                    TbKeyOverride = entry.TbKeyOverride,
                    TbTokenOverride = entry.TbTokenOverride,
                    Status = "From profile"
                };
                Channels.Add(channel);
            }
        }
        NewProfileName = name;
        Status = $"Profile '{name}' loaded.";
    }

    public Task DeleteProfileAsync(string name)
    {
        var file = Path.Combine(ProfilesDir, $"{name}.json");
        if (File.Exists(file)) File.Delete(file);
        RefreshProfileNames();
        if (NewProfileName == name) NewProfileName = "Default";
        Status = $"Profile '{name}' deleted.";
        return Task.CompletedTask;
    }

    // Saves the current state as the last session (auto-loaded on next startup)
    public async Task SaveSessionAsync()
    {
        try { await SaveProfileAsync("_last_session"); }
        catch { /* never fail app close */ }
    }

    // Loads the last session if one was saved
    public async Task LoadLastSessionAsync()
    {
        var file = Path.Combine(ProfilesDir, "_last_session.json");
        if (!File.Exists(file)) return;
        try { await LoadProfileAsync("_last_session"); }
        catch { /* ignore corrupt session file */ }
    }

    public void SaveCustomProtocol()
    {
        var name = CustomProtocolName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            Status = "Protocol name cannot be empty.";
            return;
        }

        if (!int.TryParse(CustomProtocolBaud, out var baud) || baud <= 0)
        {
            Status = "Invalid baud rate.";
            return;
        }

        var newLine = CustomProtocolTerminator switch
        {
            "CR" => "\r",
            "CRLF" => "\r\n",
            _ => "\n"
        };

        // Convert user-visible escape sequences to real chars in request template
        var request = string.IsNullOrWhiteSpace(CustomProtocolRequest)
            ? null
            : CustomProtocolRequest.Replace("\\r", "\r").Replace("\\n", "\n");

        var pattern = string.IsNullOrWhiteSpace(CustomProtocolPattern)
            ? SerialProtocolDefinition.Gp50Poll.ValuePattern
            : CustomProtocolPattern.Trim();

        var protocol = new SerialProtocolDefinition(name, baud, newLine, request, pattern);
        AvailableProtocols.Add(protocol);
        DefaultProtocol = protocol;
        Status = $"Protocol '{name}' saved.";
    }

    public void RemoveProtocol(SerialProtocolDefinition protocol)
    {
        if (SerialProtocolDefinition.Presets.Contains(protocol))
        {
            Status = "Built-in protocols cannot be removed.";
            return;
        }

        AvailableProtocols.Remove(protocol);
        if (DefaultProtocol == protocol)
            DefaultProtocol = SerialProtocolDefinition.Gp50Poll;
        Status = $"Protocol '{protocol.Name}' removed.";
    }

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
                OnPropertyChanged(nameof(AcquisitionStatusLabel));
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
                OnPropertyChanged(nameof(AcquisitionStatusLabel));
                OnPropertyChanged(nameof(IsNotAcquiring));
            }
        }
    }

    public bool IsNotAcquiring => !IsAcquiring;

    public bool IsRecording
    {
        get => _isRecording;
        set => SetField(ref _isRecording, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
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

    public bool ShowLegend
    {
        get => _showLegend;
        set => SetField(ref _showLegend, value);
    }

    public bool ShowCursors
    {
        get => _showCursors;
        set => SetField(ref _showCursors, value);
    }

    public bool ShowAlarmLines
    {
        get => _showAlarmLines;
        set => SetField(ref _showAlarmLines, value);
    }

    public string AcquisitionStatusLabel
    {
        get
        {
            if (IsAcquiring)
                return $"Live — {Channels.Count(c => c.IsAcquiring)} ch";
            var connected = Channels.Count(c => c.IsConnected);
            return connected > 0 ? $"{connected} connected" : "Idle";
        }
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
        var channel = new DeviceChannelViewModel(descriptor, _sensorSpec, color, _defaultProtocol)
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
        if (SelectedChannel is not null)
            await RemoveChannelAsync(SelectedChannel, cancellationToken);
    }

    public async Task RemoveChannelAsync(DeviceChannelViewModel channel, CancellationToken cancellationToken = default)
    {
        if (channel.IsConnected)
            await DisconnectChannelAsync(channel, cancellationToken);

        Channels.Remove(channel);
        if (SelectedChannel == channel)
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

    public async Task ConnectChannelAsync(DeviceChannelViewModel channel, CancellationToken cancellationToken = default)
    {
        if (channel.IsConnected)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var device = _factory.Create(channel.Descriptor, _sensorSpec, channel.Protocol, channel.StationNumber);
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

    public async Task DisconnectChannelAsync(DeviceChannelViewModel channel, CancellationToken cancellationToken = default)
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
                    channel.AddSample(reading, MaxSamples);
                    channel.IsAlarm = reading.PressurePsig < LowAlarmPsig || reading.PressurePsig > HighAlarmPsig;
                    UpdateAggregates();
                });

                if (IsRecording)
                {
                    await AppendRecordAsync(channel, reading, token);
                }

                // Publish to enabled streaming targets with per-channel key overrides and rate limiting
                var now = reading.Timestamp;
                if (_mqttTarget?.IsConnected == true &&
                    (_mqttPublishIntervalMs <= 0 || (now - channel.MqttLastPublished).TotalMilliseconds >= _mqttPublishIntervalMs))
                {
                    channel.MqttLastPublished = now;
                    var key = string.IsNullOrWhiteSpace(channel.MqttTopicOverride)
                        ? _mqttTopic.Replace("{channel}", channel.DisplayName)
                        : channel.MqttTopicOverride;
                    try { await _mqttTarget.PublishAsync(key, reading.PressurePsig, reading.Timestamp, token); }
                    catch { /* best-effort */ }
                }
                if (_redisTarget?.IsConnected == true &&
                    (_redisPublishIntervalMs <= 0 || (now - channel.RedisLastPublished).TotalMilliseconds >= _redisPublishIntervalMs))
                {
                    channel.RedisLastPublished = now;
                    var key = string.IsNullOrWhiteSpace(channel.RedisKeyOverride)
                        ? _redisKey.Replace("{channel}", channel.DisplayName)
                        : channel.RedisKeyOverride;
                    try { await _redisTarget.PublishAsync(key, reading.PressurePsig, reading.Timestamp, token); }
                    catch { /* best-effort */ }
                }
                if (_tbTarget?.IsConnected == true &&
                    (_tbPublishIntervalMs <= 0 || (now - channel.TbLastPublished).TotalMilliseconds >= _tbPublishIntervalMs))
                {
                    channel.TbLastPublished = now;
                    var key = string.IsNullOrWhiteSpace(channel.TbKeyOverride)
                        ? _tbKeyTemplate.Replace("{channel}", channel.DisplayName)
                        : channel.TbKeyOverride;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(channel.TbTokenOverride))
                            await _tbTarget.PublishToTokenAsync(channel.TbTokenOverride, key, reading.PressurePsig, reading.Timestamp, token);
                        else
                            await _tbTarget.PublishAsync(key, reading.PressurePsig, reading.Timestamp, token);
                    }
                    catch { /* best-effort */ }
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
