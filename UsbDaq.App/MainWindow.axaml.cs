using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using ClosedXML.Excel;
using UsbDaq.App.ViewModels;
using UsbDaq.Core;

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
            await _viewModel.LoadLastSessionAsync();
            ApplyTheme(_viewModel.ThemeMode);
        };
        Closing += async (_, _) =>
        {
            await _viewModel.SaveSessionAsync();
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

    private void SaveCustomProtocol_OnClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.SaveCustomProtocol();
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_viewModel);
        await dialog.ShowDialog(this);
    }

    private async void SaveProfile_Click(object? sender, RoutedEventArgs e)
    {
        await _viewModel.SaveProfileAsync(_viewModel.NewProfileName);
    }

    private async void LoadProfileItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string name })
            await _viewModel.LoadProfileAsync(name);
    }

    private async void DeleteProfileItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string name })
            await _viewModel.DeleteProfileAsync(name);
    }

    private void ResetZoomButton_OnClick(object? sender, RoutedEventArgs e) => GraphControl.ResetView();
    private void ZoomInButton_OnClick(object? sender, RoutedEventArgs e) => GraphControl.ZoomIn();
    private void ZoomOutButton_OnClick(object? sender, RoutedEventArgs e) => GraphControl.ZoomOut();
    private void Zoom10sButton_OnClick(object? sender, RoutedEventArgs e) => GraphControl.ZoomToSeconds(10, _viewModel.SampleIntervalMs);
    private void Zoom30sButton_OnClick(object? sender, RoutedEventArgs e) => GraphControl.ZoomToSeconds(30, _viewModel.SampleIntervalMs);
    private void Zoom1mButton_OnClick(object? sender, RoutedEventArgs e) => GraphControl.ZoomToSeconds(60, _viewModel.SampleIntervalMs);
    private void Zoom5mButton_OnClick(object? sender, RoutedEventArgs e) => GraphControl.ZoomToSeconds(300, _viewModel.SampleIntervalMs);

    private void SignalColorSwatch_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex } || _viewModel.SelectedChannel is null)
            return;

        try { _viewModel.SelectedChannel.TraceColor = Color.Parse(hex); }
        catch { /* ignore invalid swatch */ }
    }

    private async void CardConfigure_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: DeviceChannelViewModel ch }) return;
        var dialog = new ChannelConfigWindow(ch, _viewModel.AvailableProtocols);
        await dialog.ShowDialog(this);
    }

    private async void CardConnect_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeviceChannelViewModel ch })
            await _viewModel.ConnectChannelAsync(ch);
    }

    private async void CardDisconnect_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeviceChannelViewModel ch })
            await _viewModel.DisconnectChannelAsync(ch);
    }

    private async void CardRemove_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: DeviceChannelViewModel ch })
            await _viewModel.RemoveChannelAsync(ch);
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

    // ── Export handlers ──────────────────────────────────────

    private async void ExportClipboard_Click(object? sender, RoutedEventArgs e)
    {
        var channels = _viewModel.Channels.Where(c => c.Samples.Count > 0).ToList();
        if (channels.Count == 0) { _viewModel.Status = "No data to copy."; return; }
        var sb = BuildCsvString(channels);
        var topLevel = TopLevel.GetTopLevel(this)!;
        await topLevel.Clipboard!.SetTextAsync(sb.ToString());
        _viewModel.Status = $"Copied {channels.Max(c => c.Samples.Count)} rows to clipboard.";
    }

    private async void ExportPng_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Graph as PNG",
            SuggestedFileName = $"daq-graph-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            FileTypeChoices = new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } }
        });
        if (file is null) return;
        try
        {
            var w = (int)GraphControl.Bounds.Width;
            var h = (int)GraphControl.Bounds.Height;
            if (w < 1 || h < 1) { _viewModel.Status = "Graph not visible."; return; }
            using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            rtb.Render(GraphControl);
            await using var stream = await file.OpenWriteAsync();
            rtb.Save(stream);
            _viewModel.Status = $"Graph saved as {file.Name}.";
        }
        catch (Exception ex) { _viewModel.Status = $"PNG export failed: {ex.Message}"; }
    }

    private async void ExportCsv_Click(object? sender, RoutedEventArgs e)
    {
        var channels = _viewModel.Channels.Where(c => c.Samples.Count > 0).ToList();
        if (channels.Count == 0) { _viewModel.Status = "No data to export."; return; }
        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Data as CSV",
            SuggestedFileName = $"daq-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            FileTypeChoices = new[] { new FilePickerFileType("CSV File") { Patterns = new[] { "*.csv" } } }
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(BuildCsvString(channels).ToString());
        _viewModel.Status = $"CSV exported to {file.Name}.";
    }

    private async void ExportJson_Click(object? sender, RoutedEventArgs e)
    {
        var channels = _viewModel.Channels.Where(c => c.Samples.Count > 0).ToList();
        if (channels.Count == 0) { _viewModel.Status = "No data to export."; return; }
        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Data as JSON",
            SuggestedFileName = $"daq-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            FileTypeChoices = new[] { new FilePickerFileType("JSON File") { Patterns = new[] { "*.json" } } }
        });
        if (file is null) return;
        var data = channels.Select(c => new
        {
            channel = c.DisplayName,
            device = c.Descriptor.Id,
            units = c.Units,
            samples = c.Samples.Select(s => new { ts = s.Timestamp.ToString("O"), value = s.PressurePsig }).ToArray()
        }).ToArray();
        await using var stream = await file.OpenWriteAsync();
        await JsonSerializer.SerializeAsync(stream, data, new JsonSerializerOptions { WriteIndented = true });
        _viewModel.Status = $"JSON exported to {file.Name}.";
    }

    private async void ExportExcel_Click(object? sender, RoutedEventArgs e)
    {
        var channels = _viewModel.Channels.Where(c => c.Samples.Count > 0).ToList();
        if (channels.Count == 0) { _viewModel.Status = "No data to export."; return; }
        var topLevel = TopLevel.GetTopLevel(this)!;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Data as Excel",
            SuggestedFileName = $"daq-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx",
            FileTypeChoices = new[] { new FilePickerFileType("Excel Workbook") { Patterns = new[] { "*.xlsx" } } }
        });
        if (file is null) return;
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("DAQ Export");
        ws.Cell(1, 1).Value = "Timestamp";
        for (var ci = 0; ci < channels.Count; ci++)
            ws.Cell(1, ci + 2).Value = $"{channels[ci].DisplayName} ({channels[ci].Units})";
        var maxRows = channels.Max(c => c.Samples.Count);
        for (var i = 0; i < maxRows; i++)
        {
            var row = i + 2;
            if (channels[0].Samples.Count > i)
                ws.Cell(row, 1).Value = channels[0].Samples[i].Timestamp.ToString("O");
            for (var ci = 0; ci < channels.Count; ci++)
                if (channels[ci].Samples.Count > i)
                    ws.Cell(row, ci + 2).Value = channels[ci].Samples[i].PressurePsig;
        }
        await using var stream = await file.OpenWriteAsync();
        wb.SaveAs(stream);
        _viewModel.Status = $"Excel exported to {file.Name}.";
    }

    private static StringBuilder BuildCsvString(IReadOnlyList<DeviceChannelViewModel> channels)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp," + string.Join(",", channels.Select(c => $"{c.DisplayName} ({c.Units})")));
        var max = channels.Max(c => c.Samples.Count);
        for (var i = 0; i < max; i++)
        {
            var ts = channels[0].Samples.Count > i ? channels[0].Samples[i].Timestamp.ToString("O") : "";
            var vals = channels.Select(c => c.Samples.Count > i
                ? c.Samples[i].PressurePsig.ToString("F3", CultureInfo.InvariantCulture) : "");
            sb.AppendLine(ts + "," + string.Join(",", vals));
        }
        return sb;
    }

    // ── Streaming handlers (kept for any remaining toolbar shortcuts) ────────
    private async void MqttConnect_Click(object? sender, RoutedEventArgs e) => await _viewModel.ConnectMqttAsync();
    private async void MqttDisconnect_Click(object? sender, RoutedEventArgs e) => await _viewModel.DisconnectMqttAsync();
    private async void RedisConnect_Click(object? sender, RoutedEventArgs e) => await _viewModel.ConnectRedisAsync();
    private async void RedisDisconnect_Click(object? sender, RoutedEventArgs e) => await _viewModel.DisconnectRedisAsync();
    private async void TbConnect_Click(object? sender, RoutedEventArgs e) => await _viewModel.ConnectThingsBoardAsync();
    private async void TbDisconnect_Click(object? sender, RoutedEventArgs e) => await _viewModel.DisconnectThingsBoardAsync();
}