namespace SpaceMouse.App.Services;

/// <summary>
/// Two-column page that stacks vertically on narrow windows. In stacked mode the whole page scrolls
/// (outer ScrollViewer) and inner scroll viewers are switched off so there is exactly one scroll bar.
/// </summary>
public static class ResponsiveLayout
{
    public static void Attach(FrameworkElement host, Grid grid, FrameworkElement first, FrameworkElement second,
                              double breakpoint, ScrollViewer? outer, params ScrollViewer[] inner)
    {
        var cols = grid.ColumnDefinitions.Select(c => new ColumnDefinition { Width = c.Width, MinWidth = c.MinWidth, MaxWidth = c.MaxWidth }).ToList();
        var firstCol = Grid.GetColumn(first); var secondCol = Grid.GetColumn(second);
        bool? narrow = null;

        void Apply()
        {
            var n = host.ActualWidth > 0 && host.ActualWidth < breakpoint;
            if (narrow == n) return;
            narrow = n;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            if (n)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetColumn(first, 0); Grid.SetRow(first, 0);
                Grid.SetColumn(second, 0); Grid.SetRow(second, 1);
                second.Margin = new Thickness(0, 20, 0, 0);
                grid.VerticalAlignment = VerticalAlignment.Top;
            }
            else
            {
                foreach (var c in cols) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width, MinWidth = c.MinWidth, MaxWidth = c.MaxWidth });
                Grid.SetColumn(first, firstCol); Grid.SetRow(first, 0);
                Grid.SetColumn(second, secondCol); Grid.SetRow(second, 0);
                second.Margin = new Thickness(0);
                grid.VerticalAlignment = VerticalAlignment.Stretch;
            }
            if (outer != null) outer.VerticalScrollBarVisibility = n ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            foreach (var s in inner) s.VerticalScrollBarVisibility = n ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        }
        host.SizeChanged += (_, _) => Apply();
        host.Loaded += (_, _) => Apply();
    }
}
