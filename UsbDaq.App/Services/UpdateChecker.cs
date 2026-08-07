using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace UsbDaq.App.Services;

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("assets")] List<GitHubAsset> Assets);

public sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
    [property: JsonPropertyName("size")] long Size);

public sealed class UpdateChecker : IDisposable
{
    private const string ApiUrl = "https://api.github.com/repos/jamesritter03-kirby/USB-DAQ/releases/latest";
    private readonly HttpClient _http = new();

    public UpdateChecker()
    {
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("USB-DAQ", AppVersion.Current));
    }

    public async Task<GitHubRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        var json = await _http.GetStringAsync(ApiUrl, ct);
        return JsonSerializer.Deserialize<GitHubRelease>(json);
    }

    public static bool IsNewer(string tagName)
    {
        var tag = tagName.TrimStart('v');
        return Version.TryParse(tag, out var latest) &&
               Version.TryParse(AppVersion.Current, out var current) &&
               latest > current;
    }

    public async Task<string> DownloadAsync(string url, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var dest = Path.Combine(Path.GetTempPath(), "USB-DAQ-update.zip");
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var fs = File.Create(dest);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var buf = new byte[81920];
        long got = 0;
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            await fs.WriteAsync(buf.AsMemory(0, read), ct);
            got += read;
            if (total > 0) progress?.Report((int)(got * 100 / total));
        }
        return dest;
    }

    // Extracts the zip and launches a bat script that swaps the exe after this process exits
    public static void ApplyUpdate(string zipPath)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), "USB-DAQ-update");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        var newExe = Path.Combine(extractDir, "app", "UsbDaq.App.exe");
        if (!File.Exists(newExe))
            newExe = Directory.GetFiles(extractDir, "UsbDaq.App.exe", SearchOption.AllDirectories)[0];

        var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        var bat = Path.Combine(Path.GetTempPath(), "usb-daq-updater.bat");
        File.WriteAllText(bat, $"""
            @echo off
            :wait
            tasklist /FI "PID eq {pid}" 2>NUL | find "{pid}" >NUL 2>&1
            if not errorlevel 1 (
                timeout /t 1 /nobreak >NUL
                goto wait
            )
            copy /y "{newExe}" "{currentExe}"
            start "" "{currentExe}"
            del "%~f0"
            """);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{bat}\"",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });
    }

    public void Dispose() => _http.Dispose();
}
