using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UsbDaq.App.Services;

namespace UsbDaq.App;

public sealed class UpdateWindowViewModel : INotifyPropertyChanged
{
    private bool _isDownloading;
    private int _downloadProgress;
    private string _downloadStatus = "";
    private string _installButtonLabel = "Download && Install";

    public required string Version { get; init; }
    public required string ReleaseUrl { get; init; }
    public required string DownloadUrl { get; init; }
    public required string ReleaseNotes { get; init; }

    public string Title => $"USB DAQ v{Version} is available";
    public string Subtitle => $"You are running v{AppVersion.Current}";

    public bool IsDownloading { get => _isDownloading; set { _isDownloading = value; OnPropertyChanged(); OnPropertyChanged(nameof(NotIsDownloading)); } }
    public bool NotIsDownloading => !_isDownloading;
    public int DownloadProgress { get => _downloadProgress; set { _downloadProgress = value; OnPropertyChanged(); } }
    public string DownloadStatus { get => _downloadStatus; set { _downloadStatus = value; OnPropertyChanged(); } }
    public string InstallButtonLabel { get => _installButtonLabel; set { _installButtonLabel = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class UpdateWindow : Window
{
    private readonly UpdateWindowViewModel? _vm;
    private CancellationTokenSource? _cts;

    public UpdateWindow() { InitializeComponent(); }

    public UpdateWindow(UpdateWindowViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;
    }

    private void OpenReleasePageButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is not null)
            Process.Start(new ProcessStartInfo(_vm.ReleaseUrl) { UseShellExecute = true });
    }

    private void NotNowButton_Click(object? sender, RoutedEventArgs e) => Close();

    private async void DownloadInstallButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _cts = new CancellationTokenSource();
        _vm.IsDownloading = true;
        _vm.InstallButtonLabel = "Downloading…";

        try
        {
            using var checker = new UpdateChecker();
            var progress = new Progress<int>(p =>
            {
                _vm.DownloadProgress = p;
                _vm.DownloadStatus = $"Downloading… {p}%";
            });

            _vm.DownloadStatus = "Starting download…";
            var zipPath = await checker.DownloadAsync(_vm.DownloadUrl, progress, _cts.Token);

            _vm.DownloadStatus = "Download complete. Launching updater…";
            UpdateChecker.ApplyUpdate(zipPath);

            // Exit so the updater bat can replace the running exe
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown(0);
        }
        catch (OperationCanceledException)
        {
            _vm.IsDownloading = false;
            _vm.InstallButtonLabel = "Download && Install";
        }
        catch (Exception ex)
        {
            _vm.IsDownloading = false;
            _vm.InstallButtonLabel = "Download && Install";
            _vm.DownloadStatus = $"Error: {ex.Message}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts?.Cancel();
        base.OnClosed(e);
    }
}
