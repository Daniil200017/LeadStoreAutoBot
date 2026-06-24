using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LeadStoreAutoBot.Services;

public static class UpdateService
{
    public const string GitHubOwner = "Daniil200017";
    public const string GitHubRepo  = "LeadStoreAutoBot";
    public const string AssetName   = "LeadStoreAutoBot.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("LeadStoreAutoBot-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public record UpdateInfo(Version Latest, Version Current, string DownloadUrl, string Notes);


    public static Version CurrentVersion
    {
        get
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v ?? new Version(0, 0, 0);
            }
            catch { return new Version(0, 0, 0); }
        }
    }

    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
        using var resp = await Http.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var latest = ParseVersion(tag);
        if (latest == null) return null;

        var current = CurrentVersion;
        if (latest <= current) return null;

        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase) &&
                    a.TryGetProperty("browser_download_url", out var u))
                {
                    downloadUrl = u.GetString();
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;

        var notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";
        return new UpdateInfo(latest, current, downloadUrl, notes);
    }


    public static async Task<string> DownloadAsync(string downloadUrl, IProgress<int>? progress, CancellationToken ct = default)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"LeadStoreAutoBot_update_{Guid.NewGuid():N}.exe");

        using var resp = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((int)(read * 100 / total));
            else progress?.Report(-1);
        }

        return tempPath;
    }


    public static void ApplyUpdateAndRestart(string newExePath, Version? newVersion = null)
    {
        var currentExe = Environment.ProcessPath
                         ?? Process.GetCurrentProcess().MainModule?.FileName
                         ?? throw new InvalidOperationException("Cannot resolve exe path");

        try { if (newVersion != null) File.WriteAllText(AppPaths.UpdateMarkerPath, newVersion.ToString()); } catch { }

        var pid = Environment.ProcessId;
        var batPath = Path.Combine(Path.GetTempPath(), $"LeadStoreAutoBot_update_{Guid.NewGuid():N}.bat");

        var bat =
            "@echo off\r\n" +
            "chcp 65001 >nul\r\n" +
            ":wait\r\n" +
            $"tasklist /fi \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul\r\n" +
            "if not errorlevel 1 (\r\n" +
            "  timeout /t 1 /nobreak >nul\r\n" +
            "  goto wait\r\n" +
            ")\r\n" +
            "timeout /t 1 /nobreak >nul\r\n" +
            $"copy /Y \"{newExePath}\" \"{currentExe}\" >nul\r\n" +
            $"del /Q \"{newExePath}\" >nul 2>&1\r\n" +
            $"start \"\" \"{currentExe}\"\r\n" +
            "del \"%~f0\" >nul 2>&1\r\n";

        File.WriteAllText(batPath, bat, System.Text.Encoding.UTF8);

        var psi = new ProcessStartInfo
        {
            FileName = batPath,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi);
    }

    private static Version? ParseVersion(string tag)
    {
        var s = tag.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
        var clean = new string(Array.FindAll(s.ToCharArray(), c => char.IsDigit(c) || c == '.'));
        if (string.IsNullOrEmpty(clean)) return null;
        return Version.TryParse(clean.Contains('.') ? clean : clean + ".0", out var v) ? v : null;
    }
}

