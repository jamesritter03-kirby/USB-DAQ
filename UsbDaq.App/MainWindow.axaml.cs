using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using UsbDaq.App.ViewModels;

namespace UsbDaq.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _graphTimer;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;

        _graphTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _graphTimer.Tick += (_, _) => GraphControl.InvalidateVisual();

        Opened += async (_, _) =>
        {
            _graphTimer.Start();
            await _viewModel.RefreshDevicesAsync();
            ApplyTheme(_viewModel.ThemeMode);
        };
        Closing += async (_, _) =>
        {
            _graphTimer.Stop();
            await _viewModel.StopAcquisitionAsync();
            await _viewModel.DisconnectAllAsync();
        };
    }

    private async void RefreshButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RefreshDevicesAsync();
    }

    private void AddChannelButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.AddSelectedDeviceChannel();
    }

    private async void ConnectAllButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ConnectAllAsync();
    }

    private async void DisconnectAllButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.DisconnectAllAsync();
    }

    private async void StartButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.StartAcquisitionAsync();
    }

    private async void StopButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.StopAcquisitionAsync();
    }

    private async void ConnectSelectedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.ConnectSelectedAsync();
    }

    private async void DisconnectSelectedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.DisconnectSelectedAsync();
    }

    private async void RemoveSelectedButton_OnClick(object? sender, RoutedEventArgs e)
    {
        await _viewModel.RemoveSelectedChannelAsync();
    }

    private void ClearButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.ClearAllSamples();
    }

    private void ResetZoomButton_OnClick(object? sender, RoutedEventArgs e)
    {
        GraphControl.ResetView();
    }

    private void ZoomInButton_OnClick(object? sender, RoutedEventArgs e)
    {
        GraphControl.ZoomIn();
    }

    private void ZoomOutButton_OnClick(object? sender, RoutedEventArgs e)
    {
        GraphControl.ZoomOut();
    }

    private void SignalColorSwatch_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex } || _viewModel.SelectedChannel is null)
        {
            return;
        }

        try
        {
            _viewModel.SelectedChannel.TraceColor = Color.Parse(hex);
        }
        catch
        {
            // Ignore invalid swatches and keep current color.
        }
    }

    private void ThemeCombo_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplyTheme(_viewModel.ThemeMode);
    }

    private void ApplyTheme(string mode)
    {
        RequestedThemeVariant = mode switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }
}