using System.IO.Compression;
using System.Text.Json;
using WitteNog.Core.Interfaces;

namespace WitteNog.Infrastructure.Services;

public class GitHubUpdateService : IUpdateService
{
    private const string Owner = "svdaalst";
    private const string Repo = "wittenog";

    private readonly HttpClient _http;
    private readonly string _currentVersion;

    public GitHubUpdateService(HttpClient http, string currentVersion)
    {
        _http = http;
        _currentVersion = currentVersion;
    }

    public async Task<string?> CheckForUpdateAsync()
    {
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd("WitteNog-Updater/1.0");

        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
        using var response = await _http.GetAsync(url);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);

        var tagName = doc.RootElement.GetProperty("tag_name").GetString();
        if (tagName is null) return null;

        var latestVersion = tagName.TrimStart('v');
        return IsNewer(latestVersion, _currentVersion) ? latestVersion : null;
    }

    public async Task DownloadAndApplyUpdateAsync(string version, IProgress<int> progress)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Automatisch updaten is alleen beschikbaar op Windows.");

        var tag = $"v{version}";
        var zipName = $"WitteNog-{tag}.zip";
        var assetUrl = $"https://github.com/{Owner}/{Repo}/releases/download/{tag}/{zipName}";

        var tempDir = Path.Combine(Path.GetTempPath(), "WitteNog-update");
        var newDir = Path.Combine(Path.GetTempPath(), "WitteNog-new");

        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        if (Directory.Exists(newDir)) Directory.Delete(newDir, recursive: true);
        Directory.CreateDirectory(tempDir);

        // Download ZIP with progress
        var zipPath = Path.Combine(tempDir, zipName);
        using (var response = await _http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var download = await response.Content.ReadAsStreamAsync();
            await using var file = File.Create(zipPath);
            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await download.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0)
                    progress.Report((int)(downloaded * 100 / total));
            }
        }

        progress.Report(90);

        // Extract ZIP
        ZipFile.ExtractToDirectory(zipPath, newDir);

        progress.Report(95);

        // Get current app directory
        var appExe = Environment.ProcessPath ?? throw new InvalidOperationException("Kan app-pad niet bepalen.");
        var appDir = Path.GetDirectoryName(appExe) ?? throw new InvalidOperationException("Kan app-map niet bepalen.");
        var exeName = Path.GetFileName(appExe);

        // Write update batch script
        var batPath = Path.Combine(Path.GetTempPath(), "wittenog-update.bat");
        var script = $"""
            @echo off
            timeout /t 2 /nobreak > nul
            xcopy /s /e /y "{newDir}\*" "{appDir}\" > nul
            start "" "{Path.Combine(appDir, exeName)}"
            """;
        await File.WriteAllTextAsync(batPath, script);

        progress.Report(100);

        // Run updater and exit
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        Environment.Exit(0);
    }

    private static bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(latest, out var l) || !Version.TryParse(current, out var c))
            return false;
        return l > c;
    }
}
