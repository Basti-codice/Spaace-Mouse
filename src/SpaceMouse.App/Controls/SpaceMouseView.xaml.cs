using System.Windows.Shapes;
using SpaceMouse.Core.Input;


namespace SpaceMouse.App.Controls;

/// <summary>
/// The central SpaceMouse illustration: live cap movement, clickable axis/button hot spots and side tiles that show
/// the action assigned to every axis. Works without hardware (simply shows neutral values).
/// </summary>
public partial class SpaceMouseView : UserControl
{
    private sealed class Tile
    {
        public Border Root = null!;
        public Border Badge = null!;
        public TextBlock Title = null!, Summary = null!;
        public ScaleTransform NegScale = null!, PosScale = null!;
    }

    private readonly Dictionary<Axis, Tile> _tiles = new();
    private readonly Dictionary<string, Border> _btnTiles = new();
    private string? _selected;

    /// <summary>Raised with "Tx".."Rz", "Left" or "Right".</summary>
    public event Action<string>? TargetClicked;

    public bool Interactive { get; set; } = true;

    public SpaceMouseView()
    {
        InitializeComponent();
        foreach (var a in new[] { Axis.Tx, Axis.Ty, Axis.Tz }) LeftTiles.Children.Add(BuildTile(a));
        foreach (var a in new[] { Axis.Rx, Axis.Ry, Axis.Rz }) RightTiles.Children.Add(BuildTile(a));
        // button tiles under each side
        LeftTiles.Children.Add(BuildButtonTile(ButtonId.Left));
        RightTiles.Children.Add(BuildButtonTile(ButtonId.Right));

        foreach (var el in Stage.Children.OfType<FrameworkElement>().Where(e => e.Tag is string))
            el.MouseLeftButtonUp += (_, e) => { if (Interactive) { Click((string)el.Tag); e.Handled = true; } };
        Loaded += (_, _) => { ThemeService_Changed(); Services.ThemeService.Changed += ThemeService_Changed; };
        Unloaded += (_, _) => Services.ThemeService.Changed -= ThemeService_Changed;
        UpdateLive(InputFrame.Neutral);
        SelectTarget(null);
    }

    private void ThemeService_Changed() => ApplySelection();

    private void Click(string target) { SelectTarget(target); TargetClicked?.Invoke(target); }

    public string? Selected => _selected;

    public void SelectTarget(string? target)
    {
        _selected = target;
        ApplySelection();
    }

    private void ApplySelection()
    {
        foreach (var (a, t) in _tiles)
        {
            var sel = _selected == a.ToString();
            t.Root.BorderBrush = sel ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
            t.Root.Background = sel ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("SurfaceBrush");
            t.Root.BorderThickness = new Thickness(sel ? 1.5 : 1);
        }
        foreach (var (k, b) in _btnTiles)
        {
            var sel = _selected == k;
            b.BorderBrush = sel ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("BorderBrush");
            b.Background = sel ? (Brush)FindResource("AccentSoftBrush") : (Brush)FindResource("SurfaceBrush");
            b.BorderThickness = new Thickness(sel ? 1.5 : 1);
        }
        // halo around the selected hot spot on the illustration
        Halo.Visibility = Visibility.Collapsed;
        void Show(double x, double y, double w, double h)
        {
            Halo.Width = w; Halo.Height = h; Canvas.SetLeft(Halo, x); Canvas.SetTop(Halo, y); Halo.Visibility = Visibility.Visible;
        }
        switch (_selected)
        {
            case "Tx": Show(92, 100, 236, 80); break;
            case "Ty": Show(166, 20, 88, 240); break;
            case "Rz": Show(124, 54, 172, 172); break;
            case "Tz": Show(2, 48, 48, 48); break;
            case "Rx": Show(370, 48, 48, 48); break;
            case "Ry": Show(370, 98, 48, 48); break;
            case "Left": Show(80, 254, 130, 42); break;
            case "Right": Show(210, 254, 130, 42); break;
        }
    }

    private Border BuildTile(Axis a)
    {
        var t = new Tile();
        var neg = new Rectangle { Fill = (Brush)FindResource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransformOrigin = new Point(1, 0.5), RadiusX = 2, RadiusY = 2 };
        var pos = new Rectangle { Fill = (Brush)FindResource("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Stretch, RenderTransformOrigin = new Point(0, 0.5), RadiusX = 2, RadiusY = 2 };
        t.NegScale = new ScaleTransform(0, 1); t.PosScale = new ScaleTransform(0, 1);
        neg.RenderTransform = t.NegScale; pos.RenderTransform = t.PosScale;
        var bar = new Grid { Height = 5, Margin = new Thickness(0, 8, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition());
        var bg = new Border { CornerRadius = new CornerRadius(2.5) }; bg.SetResourceReference(Border.BackgroundProperty, "SurfaceHoverBrush");
        Grid.SetColumnSpan(bg, 2);
        Grid.SetColumn(pos, 1);
        bar.Children.Add(bg); bar.Children.Add(neg); bar.Children.Add(pos);

        t.Badge = new Border { Width = 26, Height = 22, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock { Text = AxisInfo.Short(a), FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        t.Badge.SetResourceReference(Border.BackgroundProperty, "SurfaceAltBrush");
        t.Title = new TextBlock { Text = AxisInfo.Name(a), FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Summary = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        t.Summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(t.Badge); head.Children.Add(t.Title);
        var stack = new StackPanel();
        stack.Children.Add(head); stack.Children.Add(t.Summary); stack.Children.Add(bar);

        t.Root = new Border { Padding = new Thickness(12, 10, 12, 12), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 0, 8), Cursor = Cursors.Hand, Child = stack, BorderThickness = new Thickness(1) };
        t.Root.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        t.Root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        t.Root.MouseLeftButtonUp += (_, e) => { if (Interactive) Click(a.ToString()); };
        _tiles[a] = t;
        return t.Root;
    }

    private readonly Dictionary<ButtonId, TextBlock> _btnSummary = new();

    private Border BuildButtonTile(ButtonId b)
    {
        var title = new TextBlock { Text = b == ButtonId.Left ? "Left button" : "Right button", FontSize = 12, FontWeight = FontWeights.SemiBold };
        var sum = new TextBlock { FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        sum.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        _btnSummary[b] = sum;
        var st = new StackPanel(); st.Children.Add(title); st.Children.Add(sum);
        var root = new Border { Padding = new Thickness(12, 10, 12, 10), CornerRadius = new CornerRadius(12), Cursor = Cursors.Hand, Child = st, BorderThickness = new Thickness(1) };
        root.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush");
        root.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        root.MouseLeftButtonUp += (_, e) => { if (Interactive) Click(b.ToString()); };
        _btnTiles[b.ToString()] = root;
        return root;
    }

    /// <summary>Show what each axis / button currently does.</summary>
    public void SetSummaries(Func<Axis, string> axis, Func<ButtonId, string> button)
    {
        foreach (var (a, t) in _tiles) t.Summary.Text = axis(a);
        foreach (var (b, tb) in _btnSummary) tb.Text = button(b);
    }

    private static void Glow(System.Windows.Shapes.Path p, double v, bool positive)
    {
        var m = positive ? Math.Max(0, v) : Math.Max(0, -v);
        p.Opacity = 0.35 + 0.65 * Math.Min(1, m);
        p.Fill = m > 0.04 ? (Brush)Application.Current.Resources["AccentBrush"] : (Brush)Application.Current.Resources["MutedBrush"];
    }

    /// <summary>Update all live visuals from a normalised frame (cheap; call at ~60 Hz).</summary>
    public void UpdateLive(InputFrame f)
    {
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var muted = (Brush)Application.Current.Resources["MutedBrush"];
        CapMove.X = f[Axis.Tx] * 22; CapMove.Y = -f[Axis.Ty] * 22;
        var s = 1.0 - f[Axis.Tz] * 0.08; CapScale.ScaleX = s; CapScale.ScaleY = s;
        ShineMove.X = f[Axis.Ry] * 14; ShineMove.Y = -f[Axis.Rx] * 10;
        MarkRot.Angle = f[Axis.Rz] * 45;

        Glow(TxPos, f[Axis.Tx], true); Glow(TxNeg, f[Axis.Tx], false);
        Glow(TyPos, f[Axis.Ty], true); Glow(TyNeg, f[Axis.Ty], false);
        Glow(RzPos, f[Axis.Rz], true); Glow(RzNeg, f[Axis.Rz], false);
        var rz = Math.Abs(f[Axis.Rz]);
        RzRing.Opacity = 0.35 + 0.65 * rz; RzRing.Stroke = rz > 0.04 ? accent : muted;

        void Badge(Ellipse e, double v) { e.Fill = Math.Abs(v) > 0.04 ? accent : (Brush)Application.Current.Resources["SurfaceAltBrush"]; e.Opacity = 1; }
        Badge(BadgeTzBg, f[Axis.Tz]); Badge(BadgeRxBg, f[Axis.Rx]); Badge(BadgeRyBg, f[Axis.Ry]);

        BtnLeft.Fill = f.Left ? accent : (Brush)Application.Current.Resources["SurfaceBrush"];
        BtnRight.Fill = f.Right ? accent : (Brush)Application.Current.Resources["SurfaceBrush"];

        foreach (var (a, t) in _tiles)
        {
            var v = f[a];
            t.NegScale.ScaleX = Math.Max(0, -v);
            t.PosScale.ScaleX = Math.Max(0, v);
            t.Badge.Background = Math.Abs(v) > 0.04 ? accent : (Brush)Application.Current.Resources["SurfaceAltBrush"];
            ((TextBlock)t.Badge.Child).Foreground = Math.Abs(v) > 0.04 ? (Brush)Application.Current.Resources["OnAccentBrush"] : (Brush)Application.Current.Resources["TextBrush"];
        }
    }
}
