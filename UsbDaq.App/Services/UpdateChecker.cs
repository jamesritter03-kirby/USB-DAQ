using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
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

    // Runtime identifier of the current OS/architecture, matching the RIDs used
    // when publishing releases (win-x64, linux-x64, osx-x64, osx-arm64).
    public static string CurrentRid
    {
        get
        {
            var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return $"win-{arch}";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return $"osx-{arch}";
            return $"linux-{arch}";
        }
    }

    // The published application executable file name for the current platform.
    private static string AppExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "UsbDaq.App.exe" : "UsbDaq.App";

    // Picks the release asset that matches the current platform, preferring the
    // Windows one-click installer, then a platform-specific zip.
    public static GitHubAsset? SelectAsset(GitHubRelease release)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var installer = release.Assets.Find(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (installer is not null) return installer;
        }

        var rid = CurrentRid;
        return release.Assets.Find(a =>
                   a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                   a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase))
               ?? release.Assets.Find(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> DownloadAsync(string url, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var ext = url.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? ".exe" : ".zip";
        var dest = Path.Combine(Path.GetTempPath(), "USB-DAQ-update" + ext);
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

    // Runs the downloaded update. For a Windows Setup.exe installer it launches a silent
    // install; for a zip it swaps the app in place via a helper script after this process exits.
    public static void ApplyUpdate(string downloadPath)
    {
        if (downloadPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            ApplyInstaller(downloadPath);
            return;
        }

        var extractDir = Path.Combine(Path.GetTempPath(), "USB-DAQ-update");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ZipFile.ExtractToDirectory(downloadPath, extractDir);

        var exeName = AppExecutableName;
        var newExe = Path.Combine(extractDir, "app", exeName);
        if (!File.Exists(newExe))
            newExe = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories)[0];

        // The whole published payload (apphost + all managed assemblies) lives next to the exe;
        // copying only the exe would leave the real code (UsbDaq.App.dll) stale.
        var payloadDir = Path.GetDirectoryName(newExe)!;
        var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
        var appRoot = Path.GetDirectoryName(currentExe)!;
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            ApplyZipWindows(payloadDir, appRoot, currentExe, pid);
        else
            ApplyZipUnix(payloadDir, appRoot, currentExe, pid);
    }

    private static void ApplyZipWindows(string payloadDir, string appRoot, string currentExe, int pid)
    {
        var bat = Path.Combine(Path.GetTempPath(), "usb-daq-updater.bat");
        File.WriteAllText(bat, $"""
            @echo off
            :wait
            tasklist /FI "PID eq {pid}" 2>NUL | find "{pid}" >NUL 2>&1
            if not errorlevel 1 (
                timeout /t 1 /nobreak >NUL
                goto wait
            )
            xcopy /e /i /y "{payloadDir}\*" "{appRoot}\" >NUL
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

    private static void ApplyZipUnix(string payloadDir, string appRoot, string currentExe, int pid)
    {
        // If running inside a macOS .app bundle, replace the whole payload, re-sign, and relaunch the bundle.
        string? bundlePath = null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var idx = currentExe.IndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
            if (idx >= 0) bundlePath = currentExe[..(idx + 4)];
        }

        var relaunch = bundlePath is not null
            ? $"""
              codesign --force --deep --sign - "{bundlePath}" >/dev/null 2>&1
              xattr -dr com.apple.quarantine "{bundlePath}" >/dev/null 2>&1
              open "{bundlePath}"
              """
            : $"""nohup "{currentExe}" >/dev/null 2>&1 &""";

        var script = Path.Combine(Path.GetTempPath(), "usb-daq-updater.sh");
        File.WriteAllText(script, $"""
            #!/bin/sh
            while kill -0 {pid} 2>/dev/null; do
                sleep 1
            done
            cp -Rf "{payloadDir}/." "{appRoot}/"
            chmod +x "{currentExe}"
            {relaunch}
            rm -f "$0"
            """);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = $"\"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    // Launches the Inno Setup installer silently; it closes the running app, updates in place, and relaunches.
    private static void ApplyInstaller(string exePath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RESTARTAPPLICATIONS",
            UseShellExecute = true
        });
    }

    public void Dispose() => _http.Dispose();
}
