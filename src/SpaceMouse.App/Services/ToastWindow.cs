using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SpaceMouse.App.Services;

/// <summary>Small non-intrusive message at the bottom of the screen (never takes focus, click-through).</summary>
public sealed class ToastWindow : Window
{
    private static ToastWindow? _current;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(2400) };

    public static void ShowToast(string text, bool isError)
    {
        _current?.Close();
        _current = new ToastWindow(text, isError);
        _current.Show();
    }

    private ToastWindow(string text, bool isError)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        var tb = new TextBlock { Text = text, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 440, FontWeight = FontWeights.Medium };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, isError ? "DangerBrush" : "SuccessBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot); row.Children.Add(tb);
        var card = new Border { Child = row, CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 12, 20, 12), BorderThickness = new Thickness(1), Margin = new Thickness(14),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.3, Direction = 270 }, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        Content = card;
        Opacity = 0;
        SourceInitialized += (_, _) => { OverlayHelper.MakeOverlay(this); OverlayHelper.PlaceBottomCenter(this, 520, 90, 40); };
        Loaded += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
                fade.Completed += (_, _) => { if (_current == this) _current = null; Close(); };
                BeginAnimation(OpacityProperty, fade);
            };
            _timer.Start();
        };
    }
}
