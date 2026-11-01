using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PilKorumasi;

public sealed class ToastWindow : Window
{
    public ToastWindow(string title, string body, AlertKind kind, Action? onClick = null)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.Height;
        Width = 360;
        ResizeMode = ResizeMode.NoResize;

        var color = kind switch
        {
            AlertKind.Lower => Color.FromRgb(242, 85, 90),
            AlertKind.Hot => Color.FromRgb(245, 165, 36),
            AlertKind.Update => Color.FromRgb(96, 165, 250),
            _ => Color.FromRgb(61, 220, 151),
        };
        var glyph = kind switch { AlertKind.Lower => "", AlertKind.Hot => "", AlertKind.Update => "", _ => "" };

        var close = new Button
        {
            Content = "",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(139, 144, 165)),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(6),
        };
        close.Click += (_, _) => Fade();

        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = new SolidColorBrush(color),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
            VerticalAlignment = VerticalAlignment.Top,
        };

        var texts = new StackPanel { Margin = new Thickness(14, 0, 4, 0) };
        texts.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(new TextBlock { Text = body, Foreground = new SolidColorBrush(Color.FromRgb(170, 175, 192)), FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(icon);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        Grid.SetColumn(close, 2);
        grid.Children.Add(close);

        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(16),
            Background = new LinearGradientBrush(Color.FromArgb(245, 24, 30, 36), Color.FromArgb(245, 14, 20, 24), 90),
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.5 },
            Child = grid,
        };

        MouseLeftButtonUp += (_, _) =>
        {
            if (onClick is not null) onClick();
            else ((App)Application.Current).ShowWindow();
            Fade();
        };

        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 8;
            Top = area.Bottom - ActualHeight - 8;
            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(260)));
            BeginAnimation(TopProperty, new DoubleAnimation(Top + 24, Top, TimeSpan.FromMilliseconds(360)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Fade();
            };
            timer.Start();
        };
    }

    void Fade()
    {
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }
}
