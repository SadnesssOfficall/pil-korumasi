using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace PilKorumasi;

public sealed record UpdateInfo(Version Version, string Tag, string Notes, string DownloadUrl, long Size, string PageUrl);

/// <summary>GitHub'daki son sürümü denetler, exe'yi indirir ve çalışan dosyanın yerine koyar.</summary>
public static class Updater
{
    public const string Repo = "SadnesssOfficall/pil-korumasi";
    const string AssetName = "PilKorumasi.exe";

    static readonly HttpClient Http = CreateClient();

    public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0));

    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PilKorumasi-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>Daha yeni bir sürüm varsa döner; yoksa null. Ağ hatasında istisna fırlatır.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", token);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = Normalize(latest);
        if (latest <= Current) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
            return new UpdateInfo(
                latest,
                tag,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.GetProperty("size").GetInt64(),
                root.GetProperty("html_url").GetString() ?? "");
        }
        return null;
    }

    /// <summary>Yeni exe'yi geçici klasöre indirir; ilerleme 0..1 arası bildirilir.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken token = default)
    {
        var target = Path.Combine(Path.GetTempPath(), $"PilKorumasi-{info.Version}.exe");
        using var response = await Http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? info.Size;
        await using (var source = await response.Content.ReadAsStreamAsync(token))
        await using (var file = File.Create(target))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), token);
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }
        if (total > 0 && new FileInfo(target).Length != total) throw new IOException("İndirme yarım kaldı.");
        return target;
    }

    /// <summary>Uygulama kapanınca exe'yi değiştirip yeniden başlatan betiği çalıştırır.</summary>
    public static void ApplyAndRestart(string downloaded)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Uygulama yolu bulunamadı.");
        var script = Path.Combine(Path.GetTempPath(), "PilKorumasi-guncelle.cmd");
        var pid = Environment.ProcessId;
        File.WriteAllText(script, $"""
            @echo off
            :bekle
            tasklist /FI "PID eq {pid}" | find "{pid}" >nul && (timeout /t 1 /nobreak >nul & goto bekle)
            set n=0
            :tasi
            move /y "{downloaded}" "{current}" >nul 2>&1 || (set /a n+=1 & timeout /t 1 /nobreak >nul & if %n% lss 15 goto tasi)
            start "" "{current}" --guncellendi
            del "%~f0"
            """);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }
}
