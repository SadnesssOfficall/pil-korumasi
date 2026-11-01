using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PilKorumasi;

public partial class MainWindow : Window
{
    readonly App _app = (App)Application.Current;
    bool _loading = true;

    public MainWindow()
    {
        InitializeComponent();
        LoadSettings();
        _app.Updated += Render;
        Closed += (_, _) => _app.Updated -= Render;
        Render(_app.Last);
        DrawHistory();
        DeviceText.Text = DeviceName();
    }

    static string DeviceName()
    {
        try
        {
            using var s = new System.Management.ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (var o in s.Get()) return o["Model"]?.ToString()?.Trim() ?? "";
        }
        catch
        {
        }
        return "";
    }

    void LoadSettings()
    {
        var s = _app.Settings;
        ProtectionBox.IsChecked = s.Protection;
        GameBox.IsChecked = s.GameMode;
        SoundBox.IsChecked = s.Sound;
        StartupBox.IsChecked = Store.StartsWithWindows;
        UpperSlider.Value = s.UpperLimit;
        LowerSlider.Value = s.LowerLimit;
        HotSlider.Value = s.HotLimitC;
        RemindSlider.Value = s.RemindMinutes;
        _loading = false;
        Labels();
    }

    void Labels()
    {
        UpperText.Text = $"%{UpperSlider.Value:0}";
        LowerText.Text = $"%{LowerSlider.Value:0}";
        HotText.Text = $"{HotSlider.Value:0} °C";
        RemindText.Text = $"{RemindSlider.Value:0} dk";
    }

    void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        Labels();
        Save();
    }

    void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Save();
    }

    void Save()
    {
        var s = _app.Settings;
        s.Protection = ProtectionBox.IsChecked == true;
        s.GameMode = GameBox.IsChecked == true;
        s.Sound = SoundBox.IsChecked == true;
        s.UpperLimit = (int)UpperSlider.Value;
        s.LowerLimit = (int)LowerSlider.Value;
        s.HotLimitC = (int)HotSlider.Value;
        s.RemindMinutes = (int)RemindSlider.Value;
        _app.SettingsChanged();
    }

    void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            Store.StartsWithWindows = StartupBox.IsChecked == true;
        }
        catch
        {
            StartupBox.IsChecked = Store.StartsWithWindows;
        }
    }

    void Render(BatterySnapshot? snap)
    {
        if (!_loading) GameBox.IsChecked = _app.Settings.GameMode;
        if (snap is null)
        {
            PercentText.Text = "--";
            StateText.Text = "Pil okunamadı";
            StateSub.Text = "Bu bilgisayarda pil bulunamadı ya da Windows bilgi vermiyor.";
            AdviceBox.Visibility = Visibility.Collapsed;
            return;
        }

        var s = _app.Settings;
        PercentText.Text = $"%{snap.Percent}";
        DrawArc(snap.Percent);

        var accent = snap.Percent <= s.LowerLimit ? "#F2555A" : snap.PluggedIn && snap.Percent >= s.UpperLimit ? "#F5A524" : "#3DDC97";
        Arc.Stroke = (Brush)new BrushConverter().ConvertFromString(accent)!;

        StateIcon.Text = snap.Charging ? "" : snap.PluggedIn ? "" : "";
        StateText.Text = snap.Charging ? "Şarj oluyor" : snap.PluggedIn ? "Prizde, şarj olmuyor" : "Pilde çalışıyor";
        var left = snap.TimeLeft is { } t ? (t.TotalHours >= 1 ? $"{(int)t.TotalHours} sa {t.Minutes} dk" : $"{Math.Max(1, t.Minutes)} dk") : null;
        StateSub.Text = snap.Charging
            ? left is null ? $"{snap.RateWatts:0.#} W ile doluyor" : $"{snap.RateWatts:0.#} W · dolmasına yaklaşık {left}"
            : snap.PluggedIn ? "Bilgisayar doğrudan prizden besleniyor." : left is null ? $"{-snap.RateWatts:0.#} W harcanıyor" : $"{-snap.RateWatts:0.#} W harcanıyor · yaklaşık {left} kaldı";

        string advice;
        var good = true;
        if (snap.PluggedIn && snap.Percent >= s.UpperLimit && s.GameMode)
        {
            advice = "Oyun modu açık. Prizde kalman normal; pili korumak için ısıyı düşük tut, oyundan sonra kabloyu çıkar.";
        }
        else if (snap.PluggedIn && snap.Percent >= s.UpperLimit)
        {
            advice = $"Pil %{s.UpperLimit} sınırının üstünde. Ekran kartına ihtiyacın yoksa kabloyu çıkar.";
            good = false;
        }
        else if (!snap.PluggedIn && snap.Percent <= s.LowerLimit)
        {
            advice = $"Pil %{s.LowerLimit} sınırının altında. Şarja tak.";
            good = false;
        }
        else
        {
            advice = $"Her şey yolunda. Pili %{s.LowerLimit} ile %{s.UpperLimit} arasında tutmak ömrünü uzatır.";
        }
        AdviceText.Text = advice;
        AdviceBox.Visibility = Visibility.Visible;
        AdviceBox.Background = (Brush)new BrushConverter().ConvertFromString(good ? "#1A2A24" : "#2E2416")!;
        AdviceText.Foreground = (Brush)new BrushConverter().ConvertFromString(good ? "#A7F3D0" : "#FCD9A0")!;

        HealthText.Text = snap.DesignMwh > 0 ? $"%{snap.HealthPercent:0.#}" : "--";
        HealthText.Foreground = (Brush)new BrushConverter().ConvertFromString(snap.HealthPercent >= 80 ? "#3DDC97" : snap.HealthPercent >= 60 ? "#F5A524" : "#F2555A")!;
        RateText.Text = Math.Abs(snap.RateWatts) < 0.05 ? "0 W" : $"{snap.RateWatts:+0.#;-0.#} W";
        TempText.Text = snap.TemperatureC is { } c ? $"{c:0.#} °C" : "yok";
        VoltText.Text = $"{snap.VoltageV:0.00} V";

        if (snap.DesignMwh > 0)
        {
            WearText.Text = $"%{100 - snap.HealthPercent:0.#} yıpranma{(snap.Cycles > 0 ? $" · {snap.Cycles} döngü" : "")}";
            FullText.Text = $"Şimdi tam dolunca {snap.FullMwh / 1000.0:0.0} Wh";
            DesignText.Text = $"Yeniyken {snap.DesignMwh / 1000.0:0.0} Wh";
            HealthBar.Width = Math.Max(0, (HealthBar.Parent as FrameworkElement)!.ActualWidth * snap.HealthPercent / 100);
            Dispatcher.BeginInvoke(() =>
            {
                if (HealthBar.Parent is FrameworkElement p) HealthBar.Width = p.ActualWidth * snap.HealthPercent / 100;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    void DrawArc(int percent)
    {
        const double size = 170, stroke = 12;
        var r = (size - stroke) / 2;
        var c = size / 2;
        var p = Math.Clamp(percent, 0, 100) / 100.0;
        if (p >= 0.999) p = 0.9999;
        var angle = p * 2 * Math.PI;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        Arc.Data = p <= 0 ? null : new PathGeometry([figure]);
    }

    void DrawHistory()
    {
        Chart.Children.Clear();
        var history = Store.LoadHistory();
        var w = Chart.ActualWidth;
        var h = Chart.ActualHeight;
        if (w <= 0 || h <= 0) return;

        foreach (var level in new[] { 100, 80, 60 })
        {
            var y = h - (level - 40) / 60.0 * h;
            Chart.Children.Add(new Line { X1 = 0, X2 = w, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromRgb(38, 42, 58)), StrokeDashArray = [4, 4] });
            var label = new TextBlock { Text = $"%{level}", Foreground = new SolidColorBrush(Color.FromRgb(110, 115, 135)), FontSize = 10 };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 14);
            Chart.Children.Add(label);
        }

        if (history.Count == 0)
        {
            HistoryNote.Text = "Kayıt yok";
            return;
        }
        HistoryNote.Text = history.Count == 1 ? "Bugün başladı · her gün bir ölçüm" : $"{history[0].Day:dd.MM.yyyy} – {history[^1].Day:dd.MM.yyyy}";

        var points = new PointCollection();
        for (var i = 0; i < history.Count; i++)
        {
            var health = Math.Clamp(history[i].FullMwh * 100.0 / history[i].DesignMwh, 40, 100);
            var x = history.Count == 1 ? w / 2 : 30 + i * (w - 40) / (history.Count - 1);
            points.Add(new Point(x, h - (health - 40) / 60.0 * h));
        }
        Chart.Children.Add(new Polyline { Points = points, Stroke = new SolidColorBrush(Color.FromRgb(61, 220, 151)), StrokeThickness = 2.5 });
        foreach (var pt in points)
        {
            var dot = new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(61, 220, 151)) };
            Canvas.SetLeft(dot, pt.X - 3.5);
            Canvas.SetTop(dot, pt.Y - 3.5);
            Chart.Children.Add(dot);
        }
    }

    void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawHistory();

    void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _app.Tick();
        DrawHistory();
    }

    void Report_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = BatteryService.CreateReport();
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
