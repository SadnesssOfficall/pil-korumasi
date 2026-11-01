using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using Forms = System.Windows.Forms;

namespace PilKorumasi;

public partial class App : System.Windows.Application
{
    Mutex? _single;
    Forms.NotifyIcon? _tray;
    MainWindow? _window;
    System.Windows.Threading.DispatcherTimer? _timer;
    Icon? _trayIcon;
    DateTime _lastUpper = DateTime.MinValue;
    DateTime _lastLower = DateTime.MinValue;
    DateTime _lastHot = DateTime.MinValue;
    DateTime _lastRecord = DateTime.MinValue;

    public Settings Settings { get; private set; } = new();
    public BatterySnapshot? Last { get; private set; }
    public event Action<BatterySnapshot?>? Updated;

    public UpdateInfo? Update { get; private set; }
    public bool Checking { get; private set; }
    public string? UpdateError { get; private set; }
    public DateTime? LastCheck { get; private set; }
    public event Action? UpdateChanged;
    System.Windows.Threading.DispatcherTimer? _updateTimer;

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        _single = new Mutex(true, "PilKorumasi.Tek", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        Settings = Store.LoadSettings();

        _tray = new Forms.NotifyIcon { Visible = true, Text = "Pil Koruması" };
        _tray.DoubleClick += (_, _) => ShowWindow();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Aç", null, (_, _) => ShowWindow());
        var guard = new Forms.ToolStripMenuItem("Koruma açık") { Checked = Settings.Protection, CheckOnClick = true };
        guard.CheckedChanged += (_, _) => { Settings.Protection = guard.Checked; Store.Save(Settings); };
        menu.Items.Add(guard);
        var game = new Forms.ToolStripMenuItem("Oyun modu") { Checked = Settings.GameMode, CheckOnClick = true };
        game.CheckedChanged += (_, _) => { Settings.GameMode = game.Checked; Store.Save(Settings); Updated?.Invoke(Last); };
        menu.Items.Add(game);
        menu.Items.Add("Güncellemeleri denetle", null, (_, _) => { ShowWindow("hakkinda"); _ = CheckForUpdates(true); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => Quit());
        menu.Opening += (_, _) => { guard.Checked = Settings.Protection; game.Checked = Settings.GameMode; };
        _tray.ContextMenuStrip = menu;

        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();

        var page = e.Args.FirstOrDefault(a => a.StartsWith("--sayfa="))?["--sayfa=".Length..];
        if (!e.Args.Contains("--tray")) ShowWindow(page);
        if (e.Args.Contains("--guncellendi"))
            new ToastWindow($"Pil Koruması {Updater.CurrentText} kuruldu", "Güncelleme tamamlandı. Yeniliklere Hakkında sayfasından bakabilirsin.", AlertKind.Update, () => ShowWindow("hakkinda")).Show();

        _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _updateTimer.Tick += (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            if (Settings.AutoUpdate) _ = CheckForUpdates(false);
        };
        _updateTimer.Start();
    }

    public async Task CheckForUpdates(bool manual)
    {
        if (Checking) return;
        Checking = true;
        UpdateError = null;
        UpdateChanged?.Invoke();
        try
        {
            var found = await Updater.CheckAsync();
            var isNew = found is not null && Update?.Version != found.Version;
            Update = found;
            if (isNew && !manual && found!.Tag != Settings.SkippedVersion && _window is null)
                new ToastWindow($"Yeni sürüm: {found.Version.ToString(3)}", "Pil Koruması için güncelleme hazır. Kurmak için tıkla.", AlertKind.Update, () => ShowWindow("hakkinda")).Show();
        }
        catch
        {
            UpdateError = "Güncelleme sunucusuna ulaşılamadı.";
        }
        finally
        {
            Checking = false;
            LastCheck = DateTime.Now;
            UpdateChanged?.Invoke();
        }
    }

    /// <summary>İndirir, exe'yi değiştirecek betiği başlatır ve uygulamayı kapatır.</summary>
    public async Task InstallUpdate(IProgress<double> progress)
    {
        if (Update is null) return;
        var file = await Updater.DownloadAsync(Update, progress);
        Updater.ApplyAndRestart(file);
        Quit();
    }

    public void SettingsChanged()
    {
        Store.Save(Settings);
        Updated?.Invoke(Last);
    }

    public void ShowWindow(string? page = null)
    {
        if (_window is null)
        {
            _window = new MainWindow();
            _window.Closed += (_, _) => _window = null;
        }
        _window.Show();
        if (_window.WindowState == System.Windows.WindowState.Minimized) _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
        if (page is not null) _window.Navigate(page);
    }

    public void Quit()
    {
        _timer?.Stop();
        _updateTimer?.Stop();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        Shutdown();
    }

    public void Tick()
    {
        BatterySnapshot? snap;
        try
        {
            snap = BatteryService.Read();
        }
        catch
        {
            snap = null;
        }
        Last = snap;
        UpdateTray(snap);
        Updated?.Invoke(snap);
        if (snap is null) return;

        if (DateTime.Now - _lastRecord > TimeSpan.FromHours(1))
        {
            Store.Record(snap);
            _lastRecord = DateTime.Now;
        }

        if (!Settings.Protection) return;
        var every = TimeSpan.FromMinutes(Math.Max(1, Settings.RemindMinutes));

        if (snap.PluggedIn && snap.Percent >= Settings.UpperLimit && !Settings.GameMode)
        {
            if (DateTime.Now - _lastUpper > every)
            {
                _lastUpper = DateTime.Now;
                Alert("Şarj kablosunu çıkarabilirsin", $"Pil %{snap.Percent}. %{Settings.UpperLimit} üstünde prizde beklemek pili yıpratır.", AlertKind.Upper);
            }
        }
        else _lastUpper = DateTime.MinValue;

        if (!snap.PluggedIn && snap.Percent <= Settings.LowerLimit)
        {
            if (DateTime.Now - _lastLower > every)
            {
                _lastLower = DateTime.Now;
                Alert("Şarja takma zamanı", $"Pil %{snap.Percent}. %{Settings.LowerLimit} altına inmek pili yıpratır.", AlertKind.Lower);
            }
        }
        else _lastLower = DateTime.MinValue;

        if (snap.TemperatureC is { } t && t >= Settings.HotLimitC)
        {
            if (DateTime.Now - _lastHot > every)
            {
                _lastHot = DateTime.Now;
                Alert("Pil çok ısındı", $"Pil {t:0.#} °C. Laptopun altını aç, fanları yükselt.", AlertKind.Hot);
            }
        }
    }

    void Alert(string title, string body, AlertKind kind)
    {
        if (Settings.Sound) SystemSounds.Exclamation.Play();
        new ToastWindow(title, body, kind).Show();
    }

    void UpdateTray(BatterySnapshot? snap)
    {
        if (_tray is null) return;
        var text = snap is null ? "?" : snap.Percent >= 100 ? "F" : snap.Percent.ToString();
        var color = snap is null ? Color.Gray
            : snap.Percent <= Settings.LowerLimit ? Color.FromArgb(242, 85, 90)
            : snap.Percent >= Settings.UpperLimit && snap.PluggedIn ? Color.FromArgb(245, 165, 36)
            : Color.FromArgb(61, 220, 151);

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var bg = new SolidBrush(Color.FromArgb(22, 24, 33));
            g.FillEllipse(bg, 0, 0, 31, 31);
            using var pen = new Pen(color, 3.2f);
            var sweep = snap is null ? 0 : 360f * snap.Percent / 100f;
            g.DrawArc(pen, 2, 2, 27, 27, -90, sweep);
            using var font = new Font("Segoe UI", text.Length > 2 ? 9f : 11f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString(text, font);
            using var fg = new SolidBrush(Color.White);
            g.DrawString(text, font, fg, (32 - size.Width) / 2 + 0.5f, (32 - size.Height) / 2 + 0.5f);
        }
        var handle = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        var old = _trayIcon;
        _tray.Icon = icon;
        _trayIcon = icon;
        old?.Dispose();

        _tray.Text = snap is null
            ? "Pil Koruması: pil okunamadı"
            : $"Pil %{snap.Percent} · {(snap.Charging ? "şarj oluyor" : snap.PluggedIn ? "prizde" : "pilde")} · sağlık %{snap.HealthPercent:0.#}";
    }
}

public enum AlertKind { Upper, Lower, Hot, Update }
