using System.Diagnostics;
using System.IO;
using System.Management;
using System.Xml.Linq;

namespace PilKorumasi;

public sealed record BatterySnapshot(
    int Percent,
    bool PluggedIn,
    bool Charging,
    double RateWatts,
    int RemainingMwh,
    int FullMwh,
    int DesignMwh,
    int Cycles,
    double? TemperatureC,
    double VoltageV,
    TimeSpan? TimeLeft)
{
    public double HealthPercent => DesignMwh > 0 ? Math.Min(100, FullMwh * 100.0 / DesignMwh) : 0;
}

public static class BatteryService
{
    static int _designMwh;

    public static int DesignCapacity()
    {
        if (_designMwh > 0) return _designMwh;
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData");
            foreach (var item in searcher.Get())
            {
                _designMwh = Convert.ToInt32(item["DesignedCapacity"]);
                if (_designMwh > 0) return _designMwh;
            }
        }
        catch (ManagementException)
        {
        }
        _designMwh = DesignFromReport();
        return _designMwh;
    }

    static int DesignFromReport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pil-korumasi-{Environment.ProcessId}.xml");
        try
        {
            using var process = Process.Start(new ProcessStartInfo("powercfg", $"/batteryreport /xml /output \"{path}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
            process?.WaitForExit(15000);
            if (!File.Exists(path)) return 0;
            var doc = XDocument.Load(path);
            var value = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "DesignCapacity")?.Value;
            return int.TryParse(value, out var mwh) ? mwh : 0;
        }
        catch
        {
            return 0;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    static T? Read<T>(string cls, Func<ManagementBaseObject, T> map) where T : class
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\wmi", $"SELECT * FROM {cls}");
            foreach (var item in searcher.Get()) return map(item);
        }
        catch (ManagementException)
        {
        }
        return null;
    }

    public static BatterySnapshot? Read()
    {
        var status = Read("BatteryStatus", o => new
        {
            Remaining = Convert.ToInt32(o["RemainingCapacity"]),
            ChargeRate = Convert.ToInt32(o["ChargeRate"]),
            DischargeRate = Convert.ToInt32(o["DischargeRate"]),
            Online = (bool)o["PowerOnline"],
            Charging = (bool)o["Charging"],
            Voltage = Convert.ToInt32(o["Voltage"]),
        });
        if (status is null) return null;

        var full = Read("BatteryFullChargedCapacity", o => new { Value = Convert.ToInt32(o["FullChargedCapacity"]) })?.Value ?? 0;
        var cycles = Read("BatteryCycleCount", o => new { Value = Convert.ToInt32(o["CycleCount"]) })?.Value ?? 0;
        var temp = Read("BatteryTemperature", o => new { Value = Convert.ToDouble(o["Temperature"]) })?.Value;

        var design = DesignCapacity();
        var percent = full > 0 ? (int)Math.Round(Math.Clamp(status.Remaining * 100.0 / full, 0, 100)) : 0;
        var rate = status.Charging ? status.ChargeRate : -status.DischargeRate;

        TimeSpan? left = null;
        if (!status.Online && status.DischargeRate > 500) left = TimeSpan.FromHours(status.Remaining / (double)status.DischargeRate);
        else if (status.Charging && status.ChargeRate > 500 && full > status.Remaining) left = TimeSpan.FromHours((full - status.Remaining) / (double)status.ChargeRate);

        double? tempC = temp is > 0 ? Math.Round(temp.Value / 10.0 - 273.15, 1) : null;
        if (tempC is < 0 or > 100) tempC = null;

        return new BatterySnapshot(percent, status.Online, status.Charging, rate / 1000.0, status.Remaining, full, design, cycles, tempC, status.Voltage / 1000.0, left);
    }

    public static string CreateReport()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "pil-raporu.html");
        using var process = Process.Start(new ProcessStartInfo("powercfg", $"/batteryreport /output \"{path}\"") { CreateNoWindow = true, UseShellExecute = false });
        process?.WaitForExit(20000);
        return path;
    }
}
