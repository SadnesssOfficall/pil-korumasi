using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PilKorumasi;

public partial class MainWindow : Window
{
    readonly App _app = (App)Application.Current;
    bool _loading = true;
    bool _bannerDismissed;
    bool _installing;
    FrameworkElement _page;
    static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Halkada gösterilen yüzde; animasyonla değişir, her karede yay yeniden çizilir.</summary>
    public static readonly DependencyProperty RingValueProperty = DependencyProperty.Register(
        nameof(RingValue), typeof(double), typeof(MainWindow), new PropertyMetadata(0.0, (d, _) => ((MainWindow)d).DrawArc()));

    public double RingValue
    {
        get => (double)GetValue(RingValueProperty);
        set => SetValue(RingValueProperty, value);
    }

    public MainWindow()
    {
        InitializeComponent();
        _page = PageDurum;
        LoadSettings();
        _app.Updated += Render;
        _app.UpdateChanged += RenderUpdate;
        Closed += (_, _) =>
        {
            _app.Updated -= Render;
            _app.UpdateChanged -= RenderUpdate;
        };
        SourceInitialized += (_, _) => ApplyBackdrop();
        Loaded += (_, e) =>
        {
            Render(_app.Last);
            RenderUpdate();
            DrawHistory();
            var start = NavAyarlar.IsChecked == true ? NavAyarlar : NavHakkinda.IsChecked == true ? NavHakkinda : NavDurum;
            MovePill(start, false);
            if (start != NavDurum) Nav_Checked(start, e);
            else Entrance(PageDurum);
            StartAmbient();
        };
        VersionText.Text = $"Sürüm {Updater.CurrentText}";
        DeviceText.Text = DeviceName();
    }

    // ---------- Cam arka plan ----------

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    void ApplyBackdrop()
    {
        var acrylic = false;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            var round = 2;
            DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            // Windows 11 22H2 ve sonrası: sistem akriliği (DWMWA_SYSTEMBACKDROP_TYPE = 3)
            if (Environment.OSVersion.Version.Build >= 22621)
            {
                HwndSource.FromHwnd(hwnd)!.CompositionTarget!.BackgroundColor = Colors.Transparent;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);
                var type = 3;
                acrylic = DwmSetWindowAttribute(hwnd, 38, ref type, sizeof(int)) == 0;
            }
        }
        catch
        {
        }

        Backdrop.Background = acrylic
            ? new SolidColorBrush(Color.FromArgb(150, 8, 18, 20))
            : new LinearGradientBrush(Color.FromRgb(10, 22, 26), Color.FromRgb(8, 30, 32), 60);
    }

    void StartAmbient()
    {
        static DoubleAnimation Drift(double to, double seconds) => new(0, to, TimeSpan.FromSeconds(seconds))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        BlobAMove.BeginAnimation(TranslateTransform.XProperty, Drift(140, 19));
        BlobAMove.BeginAnimation(TranslateTransform.YProperty, Drift(90, 23));
        BlobBMove.BeginAnimation(TranslateTransform.XProperty, Drift(-160, 21));
        BlobBMove.BeginAnimation(TranslateTransform.YProperty, Drift(-110, 17));
    }

    // ---------- Gezinme ----------

    public void Navigate(string page)
    {
        var target = page switch { "ayarlar" => NavAyarlar, "hakkinda" => NavHakkinda, _ => NavDurum };
        target.IsChecked = true;
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || sender is not RadioButton button) return;
        var next = (string)button.Tag switch { "ayarlar" => PageAyarlar, "hakkinda" => PageHakkinda, _ => (FrameworkElement)PageDurum };
        MovePill(button, true);
        if (next == _page) return;

        var previous = _page;
        _page = next;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) =>
        {
            if (_page != previous) previous.Visibility = Visibility.Collapsed;
        };
        previous.BeginAnimation(OpacityProperty, fade);

        next.Visibility = Visibility.Visible;
        next.Opacity = 0;
        next.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(90) });
        ((TranslateTransform)next.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(340)) { BeginTime = TimeSpan.FromMilliseconds(90), EasingFunction = Ease });
        if (next == PageDurum) DrawHistory();
    }

    void MovePill(RadioButton button, bool animate)
    {
        var x = button.TranslatePoint(new Point(0, 0), NavBar).X;
        var width = button.ActualWidth;
        if (!animate)
        {
            NavPill.Width = width;
            NavPillMove.X = x;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(300);
        NavPillMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duration) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.25 } });
        NavPill.BeginAnimation(WidthProperty, new DoubleAnimation(width, duration) { EasingFunction = Ease });
    }

    /// <summary>Kartları sırayla aşağıdan yukarı süzerek getirir.</summary>
    void Entrance(FrameworkElement page)
    {
        var items = new List<UIElement> { HeroCard };
        items.AddRange(StatGrid.Children.Cast<UIElement>());
        items.AddRange(BottomGrid.Children.Cast<UIElement>());
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var move = new TranslateTransform(0, 18);
            item.RenderTransform = move;
            item.Opacity = 0;
            var delay = TimeSpan.FromMilliseconds(60 + i * 55);
            item.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = delay });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(480)) { BeginTime = delay, EasingFunction = Ease });
        }
    }

    // ---------- Ayarlar ----------

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
        ProtectionBox.IsChecked = QuickProtection.IsChecked = s.Protection;
        GameBox.IsChecked = QuickGame.IsChecked = s.GameMode;
        SoundBox.IsChecked = s.Sound;
        AutoUpdateBox.IsChecked = s.AutoUpdate;
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
        _loading = true;
        QuickProtection.IsChecked = ProtectionBox.IsChecked;
        QuickGame.IsChecked = GameBox.IsChecked;
        _loading = false;
        Save();
    }

    void Quick_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _loading = true;
        ProtectionBox.IsChecked = QuickProtection.IsChecked;
        GameBox.IsChecked = QuickGame.IsChecked;
        _loading = false;
        Save();
    }

    void Save()
    {
        var s = _app.Settings;
        s.Protection = ProtectionBox.IsChecked == true;
        s.GameMode = GameBox.IsChecked == true;
        s.Sound = SoundBox.IsChecked == true;
        s.AutoUpdate = AutoUpdateBox.IsChecked == true;
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
            _loading = true;
            StartupBox.IsChecked = Store.StartsWithWindows;
            _loading = false;
        }
    }

    // ---------- Durum ----------

    static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    void Render(BatterySnapshot? snap)
    {
        if (!_loading)
        {
            _loading = true;
            GameBox.IsChecked = QuickGame.IsChecked = _app.Settings.GameMode;
            ProtectionBox.IsChecked = QuickProtection.IsChecked = _app.Settings.Protection;
            _loading = false;
        }
        if (snap is null)
        {
            PercentText.Text = "--";
            StateShort.Text = "";
            StateText.Text = "Pil okunamadı";
            StateSub.Text = "Bu bilgisayarda pil bulunamadı ya da Windows bilgi vermiyor.";
            AdviceBox.Visibility = Visibility.Collapsed;
            return;
        }

        var s = _app.Settings;
        var (from, to) = snap.Percent <= s.LowerLimit ? ("#FF8A8F", "#E5484D")
            : snap.PluggedIn && snap.Percent >= s.UpperLimit ? ("#FFD27A", "#F08C1A")
            : ("#5EF0B4", "#1BB5A8");
        var stroke = new LinearGradientBrush(Hex(from), Hex(to), 45);
        Arc.Stroke = stroke;
        ArcGlow.Stroke = new SolidColorBrush(Hex(to));
        BeginAnimation(RingValueProperty, new DoubleAnimation(snap.Percent, TimeSpan.FromMilliseconds(900)) { EasingFunction = Ease });

        StateIcon.Text = snap.Charging ? "" : snap.PluggedIn ? "" : "";
        StateShort.Text = snap.Charging ? "şarjda" : snap.PluggedIn ? "prizde" : "pilde";
        // Şarj olurken şimşek yavaşça nabız atar
        if (snap.Charging)
        {
            var pulse = new DoubleAnimation(1, 1.25, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
            if (StateIconScale.HasAnimatedProperties == false)
            {
                StateIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                StateIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
            }
        }
        else
        {
            StateIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            StateIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

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
        var tone = good ? Hex("#3EE2A0") : Hex("#F5B13D");
        AdviceText.Text = advice;
        AdviceIcon.Text = good ? "" : "";
        AdviceBox.Visibility = Visibility.Visible;
        AdviceBox.Background = new SolidColorBrush(Color.FromArgb(28, tone.R, tone.G, tone.B));
        AdviceBox.BorderBrush = new SolidColorBrush(Color.FromArgb(70, tone.R, tone.G, tone.B));
        AdviceText.Foreground = AdviceIcon.Foreground = new SolidColorBrush(good ? Hex("#B6F5DA") : Hex("#FCDDA6"));

        HealthText.Text = snap.DesignMwh > 0 ? $"%{snap.HealthPercent:0.#}" : "--";
        HealthText.Foreground = new SolidColorBrush(Hex(snap.HealthPercent >= 80 ? "#3EE2A0" : snap.HealthPercent >= 60 ? "#F5B13D" : "#F2626A"));
        RateText.Text = Math.Abs(snap.RateWatts) < 0.05 ? "0 W" : $"{snap.RateWatts:+0.#;-0.#} W";
        TempText.Text = snap.TemperatureC is { } c ? $"{c:0.#} °C" : "yok";
        VoltText.Text = $"{snap.VoltageV:0.00} V";

        if (snap.DesignMwh > 0)
        {
            WearText.Text = $"%{100 - snap.HealthPercent:0.#} yıpranma{(snap.Cycles > 0 ? $" · {snap.Cycles} döngü" : "")}";
            FullText.Text = $"Şimdi {snap.FullMwh / 1000.0:0.0} Wh";
            DesignText.Text = $"Yeniyken {snap.DesignMwh / 1000.0:0.0} Wh";
            Dispatcher.BeginInvoke(() =>
            {
                if (HealthBar.Parent is not FrameworkElement p || p.ActualWidth <= 0) return;
                var width = p.ActualWidth * Math.Clamp(snap.HealthPercent, 0, 100) / 100;
                HealthBar.BeginAnimation(WidthProperty, new DoubleAnimation(width, TimeSpan.FromMilliseconds(900)) { EasingFunction = Ease });
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    void DrawArc()
    {
        const double size = 200, stroke = 14;
        var value = RingValue;
        PercentText.Text = $"%{Math.Round(value):0}";
        var r = (size - stroke) / 2;
        var c = size / 2;
        var p = Math.Clamp(value, 0, 100) / 100.0;
        if (p >= 0.999) p = 0.9999;
        var angle = p * 2 * Math.PI;
        var figure = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
        figure.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(angle), c - r * Math.Cos(angle)), new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true));
        var geometry = p <= 0.001 ? null : new PathGeometry([figure]);
        Arc.Data = geometry;
        ArcGlow.Data = geometry;
    }

    void DrawHistory()
    {
        Chart.Children.Clear();
        var history = Store.LoadHistory();
        var w = Chart.ActualWidth;
        var full = Chart.ActualHeight;
        if (w <= 0 || full <= 0) return;
        const double top = 8;
        var h = full - top - 4;
        double Y(double health) => top + h - (health - 40) / 60.0 * h;

        foreach (var level in new[] { 100, 80, 60 })
        {
            var y = Y(level);
            Chart.Children.Add(new Line { X1 = 30, X2 = w, Y1 = y, Y2 = y, Stroke = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), StrokeDashArray = [3, 5] });
            var label = new TextBlock { Text = $"%{level}", Foreground = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), FontSize = 10 };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 7);
            Chart.Children.Add(label);
        }

        if (history.Count == 0)
        {
            HistoryNote.Text = "Kayıt yok";
            return;
        }
        HistoryNote.Text = history.Count == 1 ? "Bugün başladı · günde bir ölçüm" : $"{history[0].Day:dd.MM.yyyy} – {history[^1].Day:dd.MM.yyyy}";

        var points = new List<Point>();
        for (var i = 0; i < history.Count; i++)
        {
            var health = Math.Clamp(history[i].FullMwh * 100.0 / history[i].DesignMwh, 40, 100);
            var x = history.Count == 1 ? (w + 30) / 2 : 40 + i * (w - 50) / (history.Count - 1);
            points.Add(new Point(x, Y(health)));
        }

        var accent = Hex("#3EE2A0");
        if (points.Count > 1)
        {
            var area = new PathFigure { StartPoint = new Point(points[0].X, full), IsClosed = true };
            foreach (var pt in points) area.Segments.Add(new LineSegment(pt, false));
            area.Segments.Add(new LineSegment(new Point(points[^1].X, full), false));
            Chart.Children.Add(new Path
            {
                Data = new PathGeometry([area]),
                Fill = new LinearGradientBrush(Color.FromArgb(70, accent.R, accent.G, accent.B), Color.FromArgb(0, accent.R, accent.G, accent.B), 90),
            });
            var line = new Polyline { Points = new PointCollection(points), Stroke = new SolidColorBrush(accent), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
            Chart.Children.Add(line);
            // Çizgi soldan sağa çizilir gibi açılır
            var reveal = new RectangleGeometry(new Rect(0, 0, 0, full));
            Chart.Clip = reveal;
            reveal.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, 0, full), new Rect(0, 0, w, full), TimeSpan.FromMilliseconds(900)) { EasingFunction = Ease });
        }
        else Chart.Clip = null;

        foreach (var pt in points)
        {
            var dot = new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(accent), Stroke = new SolidColorBrush(Color.FromArgb(200, 10, 22, 26)), StrokeThickness = 2 };
            Canvas.SetLeft(dot, pt.X - 4.5);
            Canvas.SetTop(dot, pt.Y - 4.5);
            Chart.Children.Add(dot);
        }
    }

    void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawHistory();

    // ---------- Güncelleme ----------

    void RenderUpdate()
    {
        var update = _app.Update;
        NavDot.Visibility = update is not null ? Visibility.Visible : Visibility.Collapsed;

        var showBanner = update is not null && !_bannerDismissed && update.Tag != _app.Settings.SkippedVersion;
        if (showBanner && UpdateBanner.Visibility != Visibility.Visible)
        {
            BannerTitle.Text = $"Yeni sürüm hazır: {update!.Version.ToString(3)}";
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBanner.Opacity = 0;
            var move = new TranslateTransform(0, -12);
            UpdateBanner.RenderTransform = move;
            UpdateBanner.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(400)) { EasingFunction = Ease });
        }
        else if (!showBanner) UpdateBanner.Visibility = Visibility.Collapsed;

        if (_installing) return;

        Spin(_app.Checking);
        CheckBtn.IsEnabled = !_app.Checking;
        if (_app.Checking)
        {
            SetUpdateLook("", "#60A5FA");
            UpdateTitle.Text = "Denetleniyor…";
            UpdateSub.Text = "GitHub'daki son sürüme bakılıyor.";
        }
        else if (update is not null)
        {
            SetUpdateLook("", "#60A5FA");
            UpdateTitle.Text = $"Yeni sürüm: {update.Version.ToString(3)}";
            UpdateSub.Text = $"Şu an {Updater.CurrentText} kullanıyorsun · indirme {update.Size / 1048576.0:0.#} MB";
        }
        else if (_app.UpdateError is not null)
        {
            SetUpdateLook("", "#F5B13D");
            UpdateTitle.Text = "Denetlenemedi";
            UpdateSub.Text = $"{_app.UpdateError} İnternet bağlantını kontrol edip yeniden dene.";
        }
        else
        {
            SetUpdateLook(_app.LastCheck is null ? "" : "", _app.LastCheck is null ? "#9AA8A6" : "#3EE2A0");
            UpdateTitle.Text = _app.LastCheck is null ? "Henüz denetlenmedi" : "Güncelsin";
            UpdateSub.Text = _app.LastCheck is { } at ? $"Son sürümü kullanıyorsun · son denetim {at:HH:mm}" : "Açılıştan birkaç saniye sonra kendisi bakar.";
        }

        InstallBtn.Visibility = SkipBtn.Visibility = update is not null ? Visibility.Visible : Visibility.Collapsed;
        var notes = update?.Notes.Trim();
        NotesBox.Visibility = string.IsNullOrEmpty(notes) ? Visibility.Collapsed : Visibility.Visible;
        NotesText.Text = notes ?? "";
    }

    void SetUpdateLook(string glyph, string hex)
    {
        var color = Hex(hex);
        UpdateIcon.Text = glyph;
        UpdateIcon.Foreground = new SolidColorBrush(color);
        UpdateIconBox.Background = new SolidColorBrush(Color.FromArgb(36, color.R, color.G, color.B));
    }

    void Spin(bool on)
    {
        UpdateIconSpin.BeginAnimation(RotateTransform.AngleProperty,
            on ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever } : null);
    }

    async void Check_Click(object sender, RoutedEventArgs e) => await _app.CheckForUpdates(true);

    async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_installing || _app.Update is null) return;
        _installing = true;
        InstallBtn.IsEnabled = CheckBtn.IsEnabled = SkipBtn.IsEnabled = false;
        ProgressRow.Visibility = Visibility.Visible;
        SetUpdateLook("", "#60A5FA");
        UpdateTitle.Text = $"{_app.Update.Version.ToString(3)} indiriliyor…";
        var progress = new Progress<double>(value =>
        {
            ProgressFill.BeginAnimation(WidthProperty, new DoubleAnimation(ProgressRow.ActualWidth * value, TimeSpan.FromMilliseconds(150)));
            UpdateSub.Text = $"%{value * 100:0}";
            InstallText.Text = $"%{value * 100:0}";
        });
        try
        {
            await _app.InstallUpdate(progress);
            UpdateTitle.Text = "Yeniden başlatılıyor…";
        }
        catch
        {
            _installing = false;
            ProgressRow.Visibility = Visibility.Collapsed;
            InstallBtn.IsEnabled = CheckBtn.IsEnabled = SkipBtn.IsEnabled = true;
            InstallText.Text = "Yeniden dene";
            SetUpdateLook("", "#F5B13D");
            UpdateTitle.Text = "İndirme başarısız";
            UpdateSub.Text = "Bağlantı kesildi ya da dosya yarım indi. Yeniden deneyebilirsin.";
        }
    }

    void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_app.Update is null) return;
        _app.Settings.SkippedVersion = _app.Update.Tag;
        _app.SettingsChanged();
        _bannerDismissed = true;
        RenderUpdate();
    }

    void BannerLater_Click(object sender, RoutedEventArgs e)
    {
        _bannerDismissed = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => UpdateBanner.Visibility = Visibility.Collapsed;
        UpdateBanner.BeginAnimation(OpacityProperty, fade);
    }

    void BannerOpen_Click(object sender, RoutedEventArgs e) => Navigate("hakkinda");

    // ---------- Diğer ----------

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _app.Tick();
        DrawHistory();
    }

    void Link_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url }) Open(url);
    }

    void Report_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Open(BatteryService.CreateReport());
        }
        catch
        {
        }
    }

    static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
