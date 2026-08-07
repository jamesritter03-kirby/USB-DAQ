using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using UsbDaq.Core;

namespace UsbDaq.App.ViewModels;

public sealed class DeviceChannelViewModel : INotifyPropertyChanged
{
    private readonly SensorSpecification _sensorSpec;
    private readonly ObservableCollection<PressureReading> _samples = new();
    private IPressureDevice? _device;
    private bool _isConnected;
    private bool _isAcquiring;
    private bool _isAlarm;
    private bool _isVisible = true;
    private string _signalName;
    private string _colorHex;
    private Color _traceColor;
    private string _status = "Idle";
    private string _currentPressureText = "--.- psig";
    private double _currentPressure;
    private double _minObserved;
    private double _maxObserved;
    private double _averageObserved;
    private long _sampleCount;
    private double _sumObserved;
    private SerialProtocolDefinition _protocol;
    private int _stationNumber;
    private string _mqttTopicOverride = "";
    private string _redisKeyOverride = "";
    private string _tbKeyOverride = "";
    private string _tbTokenOverride = "";

    // Last-published timestamps for rate-limiting per streaming target (not UI state)
    internal DateTimeOffset MqttLastPublished = DateTimeOffset.MinValue;
    internal DateTimeOffset RedisLastPublished = DateTimeOffset.MinValue;
    internal DateTimeOffset TbLastPublished = DateTimeOffset.MinValue;

    public DeviceChannelViewModel(DeviceDescriptor descriptor, SensorSpecification sensorSpec,
        string colorHex, SerialProtocolDefinition? protocol = null, int stationNumber = 1)
    {
        Descriptor = descriptor;
        _sensorSpec = sensorSpec;
        _signalName = descriptor.DisplayName;
        _colorHex = colorHex;
        _traceColor = ParseColorOrDefault(colorHex);
        _protocol = protocol ?? SerialProtocolDefinition.Gp50Poll;
        _stationNumber = stationNumber;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DeviceDescriptor Descriptor { get; }

    public string ColorHex
    {
        get => _colorHex;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var normalized = value.Trim();
            if (!normalized.StartsWith("#", StringComparison.Ordinal))
            {
                normalized = "#" + normalized;
            }

            normalized = normalized.ToUpperInvariant();
            if (SetField(ref _colorHex, normalized))
            {
                TraceColor = ParseColorOrDefault(normalized);
            }
        }
    }

    public Color TraceColor
    {
        get => _traceColor;
        set
        {
            if (SetField(ref _traceColor, value))
            {
                var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
                if (!_colorHex.Equals(hex, StringComparison.OrdinalIgnoreCase))
                {
                    _colorHex = hex;
                    OnPropertyChanged(nameof(ColorHex));
                }
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    public ObservableCollection<PressureReading> Samples => _samples;

    public string DeviceName => Descriptor.DisplayName;

    public string Units => _sensorSpec.Units;

    public string SignalName
    {
        get => _signalName;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? Descriptor.DisplayName : value.Trim();
            if (SetField(ref _signalName, normalized))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => SignalName;

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetField(ref _isConnected, value))
                OnPropertyChanged(nameof(IsNotConnected));
        }
    }

    public bool IsNotConnected => !IsConnected;

    public SerialProtocolDefinition Protocol
    {
        get => _protocol;
        set => SetField(ref _protocol, value);
    }

    public int StationNumber
    {
        get => _stationNumber;
        set => SetField(ref _stationNumber, Math.Clamp(value, 0, 999));
    }

    public string MqttTopicOverride
    {
        get => _mqttTopicOverride;
        set => SetField(ref _mqttTopicOverride, value ?? "");
    }

    public string RedisKeyOverride
    {
        get => _redisKeyOverride;
        set => SetField(ref _redisKeyOverride, value ?? "");
    }

    public string TbKeyOverride
    {
        get => _tbKeyOverride;
        set => SetField(ref _tbKeyOverride, value ?? "");
    }

    public string TbTokenOverride
    {
        get => _tbTokenOverride;
        set => SetField(ref _tbTokenOverride, value ?? "");
    }

    public bool IsAcquiring
    {
        get => _isAcquiring;
        set => SetField(ref _isAcquiring, value);
    }

    public bool IsAlarm
    {
        get => _isAlarm;
        set => SetField(ref _isAlarm, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string CurrentPressureText
    {
        get => _currentPressureText;
        set => SetField(ref _currentPressureText, value);
    }

    public double CurrentPressure
    {
        get => _currentPressure;
        set => SetField(ref _currentPressure, value);
    }

    public double MinObserved
    {
        get => _minObserved;
        private set => SetField(ref _minObserved, value);
    }

    public double MaxObserved
    {
        get => _maxObserved;
        private set => SetField(ref _maxObserved, value);
    }

    public double AverageObserved
    {
        get => _averageObserved;
        private set => SetField(ref _averageObserved, value);
    }

    public long SampleCount
    {
        get => _sampleCount;
        private set => SetField(ref _sampleCount, value);
    }

    public void Attach(IPressureDevice? device)
    {
        _device = device;
    }

    public IPressureDevice? GetDevice() => _device;

    public void AddSample(PressureReading reading, int maxSamples = 240)
    {
        _samples.Add(reading);
        while (_samples.Count > maxSamples)
        {
            _samples.RemoveAt(0);
        }

        CurrentPressure = reading.PressurePsig;
        CurrentPressureText = $"{reading.PressurePsig:F1} {_sensorSpec.Units}";
        Status = $"{reading.Timestamp:HH:mm:ss}";

        if (SampleCount == 0)
        {
            MinObserved = reading.PressurePsig;
            MaxObserved = reading.PressurePsig;
            _sumObserved = reading.PressurePsig;
            SampleCount = 1;
            AverageObserved = reading.PressurePsig;
            return;
        }

        SampleCount += 1;
        _sumObserved += reading.PressurePsig;
        MinObserved = Math.Min(MinObserved, reading.PressurePsig);
        MaxObserved = Math.Max(MaxObserved, reading.PressurePsig);
        AverageObserved = _sumObserved / SampleCount;
    }

    public void ClearSamples()
    {
        _samples.Clear();
        CurrentPressure = 0;
        CurrentPressureText = $"--.- {_sensorSpec.Units}";
        MinObserved = 0;
        MaxObserved = 0;
        AverageObserved = 0;
        SampleCount = 0;
        _sumObserved = 0;
        IsAlarm = false;
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

    private static Color ParseColorOrDefault(string candidate)
    {
        try
        {
            return Color.Parse(candidate);
        }
        catch
        {
            return Colors.DodgerBlue;
        }
    }
}
