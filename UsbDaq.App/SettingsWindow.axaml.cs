using Avalonia.Controls;
using Avalonia.Interactivity;
using UsbDaq.App.ViewModels;

namespace UsbDaq.App;

public partial class SettingsWindow : Window
{
    private readonly MainWindowViewModel? _vm;

    // Required by Avalonia AXAML loader
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(MainWindowViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

    private void SaveCustomProtocol_Click(object? sender, RoutedEventArgs e)
        => _vm?.SaveCustomProtocol();

    private async void MqttConnect_Click(object? sender, RoutedEventArgs e)
        => await (_vm?.ConnectMqttAsync() ?? System.Threading.Tasks.Task.CompletedTask);

    private async void MqttDisconnect_Click(object? sender, RoutedEventArgs e)
        => await (_vm?.DisconnectMqttAsync() ?? System.Threading.Tasks.Task.CompletedTask);

    private async void RedisConnect_Click(object? sender, RoutedEventArgs e)
        => await (_vm?.ConnectRedisAsync() ?? System.Threading.Tasks.Task.CompletedTask);

    private async void RedisDisconnect_Click(object? sender, RoutedEventArgs e)
        => await (_vm?.DisconnectRedisAsync() ?? System.Threading.Tasks.Task.CompletedTask);

    private async void TbConnect_Click(object? sender, RoutedEventArgs e)
        => await (_vm?.ConnectThingsBoardAsync() ?? System.Threading.Tasks.Task.CompletedTask);

    private async void TbDisconnect_Click(object? sender, RoutedEventArgs e)
        => await (_vm?.DisconnectThingsBoardAsync() ?? System.Threading.Tasks.Task.CompletedTask);
}
