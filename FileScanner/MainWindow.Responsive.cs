using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FileScanner;

// Narrow windows: panels, tabs, legend and the path row give up room in steps instead of being clipped.
public sealed partial class MainWindow
{
    // Width of the tab bar with labels, measured while they are shown (it depends on the language).
    double _fullTabsWidth;
    bool _compactTabs;

    void Root_SizeChanged(object sender, SizeChangedEventArgs e) => AdaptLayout(e.NewSize.Width);

    // The right panel grows with the Windows text size (Accessibility > Text size) so its buttons fit, capped at 640,
    // and takes at most 38% of a narrow window so the chart keeps room. Below 1300 px the counts next to the
    // top figures are hidden: the four cells are about 270 px wide there and the figure itself needs most of it.
    void AdaptLayout(double width)
    {
        if (width <= 0) return;
        double wanted = Math.Min(440 * _uiSettings.TextScaleFactor, 640);
        SideColumn.Width = new GridLength(Math.Max(360, Math.Min(wanted, width * 0.38)));
        var details = width < 1300 ? Visibility.Collapsed : Visibility.Visible;
        FreeDelta.Visibility = SafeCount.Visibility = ReviewCount.Visibility = OutsideDetail.Visibility = details;
    }

    void ExplorerHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The search box shares the tabs row.
        SearchBox.Width = e.NewSize.Width < 640 ? 160 : 220;
        double available = e.NewSize.Width - SearchBox.Width - 8;
        if (!_compactTabs)
        {
            ViewBar.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            _fullTabsWidth = ViewBar.DesiredSize.Width;
            if (_fullTabsWidth > available) SetCompactTabs(true);
        }
        else if (_fullTabsWidth <= available) SetCompactTabs(false);
    }

    // Icon-only tabs keep their name as a tooltip; Narrator reads AutomationProperties.Name, set in XAML.
    void SetCompactTabs(bool compact)
    {
        _compactTabs = compact;
        foreach (var item in ViewBar.Items)
        {
            string name = AutomationProperties.GetName(item);
            item.Text = compact ? "" : name;
            ToolTipService.SetToolTip(item, compact ? name : null);
        }
    }

    // Path row: below 520 px the size and file count go (the chart centre and the footer show them too)
    // so the path keeps some room.
    void CrumbsRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        PathSize.Visibility = e.NewSize.Width < 520 ? Visibility.Collapsed : Visibility.Visible;
        FitRings();
    }

    // Rings view: the chart also takes the height of the size boxes row. The circle leaves the top corners free,
    // and the boxes, drawn on top, fit there. The treemap fills its whole area, so it stays below them.
    void FitRings()
    {
        double boxes = CrumbsRow.RowDefinitions[1].ActualHeight + CrumbsRow.RowSpacing;
        MapHost.Margin = new Thickness(0, RingsView ? -boxes : 0, 0, 0);
    }

    // About 140 px per legend entry: 5 columns in a wide panel, at least 3 (each extra row takes height from the chart).
    void Legend_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        int columns = Math.Clamp((int)(e.NewSize.Width / 140), 3, 5);
        if (columns == _legendColumns) return;
        SetLegendColumns(columns);
        FillLegend();
    }

    void SetLegendColumns(int columns)
    {
        _legendColumns = columns;
        Legend.ColumnDefinitions.Clear();
        for (int c = 0; c < columns; c++) Legend.ColumnDefinitions.Add(new ColumnDefinition());
    }
}
