using System.Windows.Media.Animation;
using SpaceMouse.App.Controls;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Engine;

namespace SpaceMouse.App.Views;

/// <summary>
/// Radial menu drawn around the cursor. Borderless, transparent, topmost, never activated and click-through,
/// so the real mouse and keyboard keep working. Created once at startup; stays hidden until the engine opens a menu.
/// </summary>
public sealed class QuickMenuOverlayWindow : Window
{
    private const double Dip = 460;
    private readonly RadialMenuControl _menu = new() { Width = 440, Height = 440 };
    private readonly ScaleTransform _scale = new(0.9, 0.9);
    private int _generation;

    public QuickMenuOverlayWindow(SpaceMouseEngine engine)
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; ResizeMode = ResizeMode.NoResize;
        Width = Dip; Height = Dip; Focusable = false;
        var grid = new Grid { Width = Dip, Height = Dip, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _scale };
        _menu.HorizontalAlignment = HorizontalAlignment.Center; _menu.VerticalAlignment = VerticalAlignment.Center;
        _menu.IsHitTestVisible = false;
        grid.Children.Add(_menu);
        Content = grid;

        engine.Menu.Opened += v => Post(() => OnOpened(v));
        engine.Menu.Changed += v => Post(() => _menu.SetMenu(v.Items, v.Selected, TitleOf(v)));
        engine.Menu.Closed += _ => Post(OnClosed);
    }

    private static string TitleOf(QuickMenuView v) => string.IsNullOrEmpty(v.Title) ? v.Menu.Name : v.Title;

    private void Post(Action a)
    {
        Dispatcher.BeginInvoke(new Action(() => { try { a(); } catch { /* an overlay must never crash the app */ } }));
    }

    private void OnOpened(QuickMenuView v)
    {
        ++_generation;
        _menu.SetMenu(v.Items, v.Selected, TitleOf(v));
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        if (!IsVisible) Show();
        OverlayHelper.MakeOverlay(this, clickThrough: true);
        OverlayHelper.ShowCentered(this, v.CursorX, v.CursorY, Dip, Dip);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var t = TimeSpan.FromMilliseconds(140);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, t));
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.9, 1, t) { EasingFunction = ease });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, t) { EasingFunction = ease });
    }

    private void OnClosed()
    {
        var gen = ++_generation;
        if (!IsVisible) return;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(110));
        fade.Completed += (_, _) => { if (gen == _generation) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }
}
