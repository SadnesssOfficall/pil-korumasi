using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace PilKorumasi;

public sealed class Settings
{
    public int UpperLimit { get; set; } = 80;
    public int LowerLimit { get; set; } = 30;
    public bool Protection { get; set; } = true;
    public bool GameMode { get; set; }
    public int RemindMinutes { get; set; } = 10;
    public int HotLimitC { get; set; } = 45;
    public bool Sound { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;
    public string? SkippedVersion { get; set; }
}

public sealed record HealthPoint(DateOnly Day, int FullMwh, int DesignMwh, int Cycles);

public static class Store
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PilKorumasi");
    static readonly string SettingsPath = Path.Combine(Dir, "ayarlar.json");
    static readonly string HistoryPath = Path.Combine(Dir, "gecmis.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "PilKorumasi";

    public static Settings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) ?? new Settings();
        }
        catch
        {
        }
        return new Settings();
    }

    public static void Save(Settings settings)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Json));
    }

    public static List<HealthPoint> LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryPath)) return JsonSerializer.Deserialize<List<HealthPoint>>(File.ReadAllText(HistoryPath)) ?? [];
        }
        catch
        {
        }
        return [];
    }

    public static List<HealthPoint> Record(BatterySnapshot snap)
    {
        var history = LoadHistory();
        if (snap.FullMwh <= 0 || snap.DesignMwh <= 0) return history;
        var today = DateOnly.FromDateTime(DateTime.Now);
        history.RemoveAll(p => p.Day == today);
        history.Add(new HealthPoint(today, snap.FullMwh, snap.DesignMwh, snap.Cycles));
        history.Sort((a, b) => a.Day.CompareTo(b.Day));
        if (history.Count > 730) history.RemoveRange(0, history.Count - 730);
        Directory.CreateDirectory(Dir);
        File.WriteAllText(HistoryPath, JsonSerializer.Serialize(history, Json));
        return history;
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(RunName, false);
        }
    }
}
