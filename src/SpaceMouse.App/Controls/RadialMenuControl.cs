using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SpaceMouse.App.Services;
using SpaceMouse.Core.Engine;
using MenuItem = SpaceMouse.Core.Profiles.MenuItem;

namespace SpaceMouse.App.Controls;

/// <summary>
/// Circular menu renderer. Item i sits at angle i*360/n clockwise from the top (sector i is centred on item i),
/// exactly like <see cref="QuickMenuController"/> selects. Transparent background: usable on an overlay window.
/// </summary>
public sealed class RadialMenuControl : UserControl
{
    private const double Size = 440, C = 220, RIn = 92, ROut = 206, RLabel = 150;

    private sealed class Seg
    {
        public Shape Shape = null!;
        public TextBlock Icon = null!, Label = null!;
        public ScaleTransform Scale = null!;
    }

    private readonly Canvas _canvas = new() { Width = Size, Height = Size };
    private readonly List<Seg> _segs = new();
    private readonly TextBlock _centerMain = new(), _centerSub = new();
    private IReadOnlyList<MenuItem>? _items;
    private int _sel = -1;
    private string _title = "";

    public event Action<int>? ItemHovered;
    public event Action<int>? ItemClicked;

    public RadialMenuControl()
    {
        Width = 360; Height = 360;
        _canvas.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = 0.28, Direction = 270 };
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = _canvas };
        SetMenu(Array.Empty<MenuItem>(), -1, "");
    }

    public int Selected => _sel;

    public void SetMenu(IReadOnlyList<MenuItem> items, int selected, string title)
    {
        title ??= "";
        if (ReferenceEquals(items, _items) && items.Count == _segs.Count && title == _title)
        {
            SetSelected(selected);
            return;
        }
        _items = items; _title = title; _sel = selected;
        Rebuild();
    }

    public void SetSelected(int index)
    {
        if (_items == null) return;
        if (index < -1 || index >= _items.Count) index = -1;
        _sel = index;
        ApplySelection(animate: true);
    }

    private static Point P(double r, double deg)
    {
        var t = deg * Math.PI / 180;
        return new Point(C + r * Math.Sin(t), C - r * Math.Cos(t));
    }

    private static Geometry Sector(double a1, double a2)
    {
        var span = a2 - a1;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(ROut, a1), true, true);
            ctx.ArcTo(P(ROut, a2), new Size(ROut, ROut), 0, span > 180, SweepDirection.Clockwise, true, false);
            ctx.LineTo(P(RIn, a2), true, false);
            ctx.ArcTo(P(RIn, a1), new Size(RIn, RIn), 0, span > 180, SweepDirection.Counterclockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private void Rebuild()
    {
        _canvas.Children.Clear(); _segs.Clear();
        var items = _items ?? Array.Empty<MenuItem>();
        var n = items.Count;

        for (var i = 0; i < n; i++)
        {
            var idx = i;
            var item = items[i];
            var center = i * 360.0 / n;
            Geometry geo;
            if (n == 1)
                geo = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Point(C, C), ROut, ROut), new EllipseGeometry(new Point(C, C), RIn, RIn));
            else
            {
                var half = 180.0 / n; const double gap = 1.4;
                geo = Sector(center - half + gap, center + half - gap);
            }
            var scale = new ScaleTransform(1, 1, C, C);
            var path = new System.Windows.Shapes.Path { Data = geo, StrokeThickness = 1, Opacity = 0.95, Cursor = Cursors.Hand, RenderTransform = scale };
            path.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
            path.MouseEnter += (_, _) => ItemHovered?.Invoke(idx);
            path.MouseLeftButtonUp += (_, _) => ItemClicked?.Invoke(idx);
            _canvas.Children.Add(path);

            var pos = P(RLabel, center);
            var small = n > 8;
            var icon = new TextBlock
            {
                Text = IconMap.Glyph(item.Icon), FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
                FontSize = small ? 20 : 24, Width = 90, TextAlignment = TextAlignment.Center, IsHitTestVisible = false,
            };
            var label = new TextBlock
            {
                Text = item.HasChildren ? item.Label + " ›" : item.Label, FontSize = small ? 10.5 : 12, FontWeight = FontWeights.Medium,
                Width = small ? 70 : 90, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false,
            };
            Canvas.SetLeft(icon, pos.X - 45); Canvas.SetTop(icon, pos.Y - (small ? 22 : 26));
            Canvas.SetLeft(label, pos.X - label.Width / 2); Canvas.SetTop(label, pos.Y + (small ? 4 : 5));
            _canvas.Children.Add(icon); _canvas.Children.Add(label);
            _segs.Add(new Seg { Shape = path, Icon = icon, Label = label, Scale = scale });
        }

        var disc = new Ellipse { Width = (RIn - 10) * 2, Height = (RIn - 10) * 2, StrokeThickness = 1, IsHitTestVisible = false, Opacity = 0.95 };
        disc.SetResourceReference(Shape.FillProperty, "SurfaceBrush");
        disc.SetResourceReference(Shape.StrokeProperty, "BorderBrush");
        Canvas.SetLeft(disc, C - disc.Width / 2); Canvas.SetTop(disc, C - disc.Height / 2);
        _canvas.Children.Add(disc);

        _centerMain.FontSize = 17; _centerMain.FontWeight = FontWeights.SemiBold; _centerMain.Width = 140; _centerMain.TextAlignment = TextAlignment.Center;
        _centerMain.TextTrimming = TextTrimming.CharacterEllipsis; _centerMain.IsHitTestVisible = false;
        _centerMain.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        _centerSub.FontSize = 11.5; _centerSub.Width = 140; _centerSub.TextAlignment = TextAlignment.Center; _centerSub.IsHitTestVisible = false;
        _centerSub.TextTrimming = TextTrimming.CharacterEllipsis;
        _centerSub.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        Canvas.SetLeft(_centerMain, C - 70); Canvas.SetTop(_centerMain, C - 22);
        Canvas.SetLeft(_centerSub, C - 70); Canvas.SetTop(_centerSub, C + 6);
        _canvas.Children.Add(_centerMain); _canvas.Children.Add(_centerSub);

        ApplySelection(animate: false);
    }

    private void ApplySelection(bool animate)
    {
        var items = _items;
        for (var i = 0; i < _segs.Count; i++)
        {
            var s = _segs[i]; var sel = i == _sel;
            s.Shape.SetResourceReference(Shape.FillProperty, sel ? "AccentBrush" : "SurfaceBrush");
            s.Icon.SetResourceReference(TextBlock.ForegroundProperty, sel ? "OnAccentBrush" : "MutedBrush");
            s.Label.SetResourceReference(TextBlock.ForegroundProperty, sel ? "OnAccentBrush" : "TextBrush");
            var target = sel ? 1.06 : 1.0;
            if (animate)
            {
                var a = new DoubleAnimation(target, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() };
                s.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                s.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
            }
            else
            {
                s.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                s.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                s.Scale.ScaleX = s.Scale.ScaleY = target;
            }
        }
        if (items != null && _sel >= 0 && _sel < items.Count)
        {
            var it = items[_sel];
            _centerMain.Text = it.Label;
            _centerSub.Text = it.Id == QuickMenuController.BackId ? "Go back" : it.HasChildren ? "Submenu" : _title;
        }
        else
        {
            _centerMain.Text = _title;
            _centerSub.Text = items == null || items.Count == 0 ? "No items" : "Point at an item";
        }
    }
}
