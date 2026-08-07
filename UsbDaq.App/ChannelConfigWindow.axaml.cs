using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using UsbDaq.App.ViewModels;
using UsbDaq.Core;

namespace UsbDaq.App;

// Thin ViewModel exposing both the channel and the protocol list to the dialog AXAML
internal sealed class ChannelConfigViewModel
{
    public DeviceChannelViewModel Channel { get; }
    public IReadOnlyList<SerialProtocolDefinition> AvailableProtocols { get; }

    public ChannelConfigViewModel(DeviceChannelViewModel channel,
        ObservableCollection<SerialProtocolDefinition> protocols)
    {
        Channel = channel;
        AvailableProtocols = protocols;
    }
}

public partial class ChannelConfigWindow : Window
{
    private ChannelConfigViewModel? _vm;
    // Snapshots for cancel
    private string _snapName = string.Empty;
    private Color _snapColor;
    private SerialProtocolDefinition _snapProtocol = SerialProtocolDefinition.Gp50Poll;
    private int _snapStation;
    private double _snapLowAlarm;
    private double _snapHighAlarm;
    private bool _snapAlarmEnabled;

    // Required by Avalonia AXAML loader
    public ChannelConfigWindow()
    {
        InitializeComponent();
    }

    public ChannelConfigWindow(DeviceChannelViewModel channel,
        ObservableCollection<SerialProtocolDefinition> protocols)
    {
        _vm = new ChannelConfigViewModel(channel, protocols);
        _snapName = channel.SignalName;
        _snapColor = channel.TraceColor;
        _snapProtocol = channel.Protocol;
        _snapStation = channel.StationNumber;
        _snapLowAlarm = channel.LowAlarm;
        _snapHighAlarm = channel.HighAlarm;
        _snapAlarmEnabled = channel.AlarmEnabled;

        InitializeComponent();
        DataContext = _vm;
        UpdateProtocolInfo();
    }

    private void ColorSwatch_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || sender is not Button { Tag: string hex }) return;
        try { _vm.Channel.TraceColor = Color.Parse(hex); }
        catch { /* ignore invalid hex */ }
    }

    private void ProtocolCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateProtocolInfo();
    }

    private void UpdateProtocolInfo()
    {
        if (_vm is null) return;
        var p = _vm.Channel.Protocol;
        if (ModeText is not null)
            ModeText.Text = p.RequestTemplate is null ? "Passive (stream)" : "Active (poll)";
        if (RequestText is not null)
            RequestText.Text = p.RequestTemplate is null ? "—"
                : p.RequestTemplate.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Channel.SignalName = _snapName;
            _vm.Channel.TraceColor = _snapColor;
            _vm.Channel.Protocol = _snapProtocol;
            _vm.Channel.StationNumber = _snapStation;
            _vm.Channel.LowAlarm = _snapLowAlarm;
            _vm.Channel.HighAlarm = _snapHighAlarm;
            _vm.Channel.AlarmEnabled = _snapAlarmEnabled;
        }
        Close();
    }
}
