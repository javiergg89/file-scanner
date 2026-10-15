using System.Collections.ObjectModel;
using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
using VirtualKey = Windows.System.VirtualKey;

namespace FileScanner;

// Left-side explorer: Map (treemap) and Rings (sunburst).
public sealed partial class MainWindow
{
    const int RingDepth = 5;
    // Columns of the legend grid; fewer in a narrow panel (Legend_SizeChanged).
    int _legendColumns = 5;
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas");
    static readonly Color CleanColor = Color.FromArgb(255, 0x3C, 0xA0, 0x5A);

    static readonly Dictionary<Category, (string Label, Color Color)> Categories = new()
    {
        // Legend order. Green is not used here: it marks what is selected for deletion.
        [Category.Code] = (Strings.Map.CategoryCode, Color.FromArgb(255, 0x8B, 0x7C, 0xF6)),
        [Category.Games] = (Strings.Map.CategoryGames, Color.FromArgb(255, 0xE1, 0x57, 0x59)),
        [Category.Media] = (Strings.Map.CategoryMedia, Color.FromArgb(255, 0xF2, 0x8E, 0x2B)),
        [Category.Browsers] = (Strings.Map.CategoryBrowsers, Color.FromArgb(255, 0xED, 0xC9, 0x48)),
        [Category.Communication] = (Strings.Map.CategoryCommunication, Color.FromArgb(255, 0x76, 0xB7, 0xB2)),
        [Category.Editors] = (Strings.Map.CategoryEditors, Color.FromArgb(255, 0xB0, 0x7A, 0xA1)),
        [Category.PackageCaches] = (Strings.Map.CategoryPackageCaches, Color.FromArgb(255, 0x4E, 0x79, 0xA7)),
        [Category.Sdk] = (Strings.Map.CategorySdk, Color.FromArgb(255, 0x3C, 0xB4, 0xD2)),
        [Category.AiModels] = (Strings.Map.CategoryAiModels, Color.FromArgb(255, 0xFF, 0x9D, 0xA7)),
        [Category.Virtualization] = (Strings.Map.CategoryVirtualization, Color.FromArgb(255, 0x9C, 0x75, 0x5F)),
        [Category.Cloud] = (Strings.Map.CategoryCloud, Color.FromArgb(255, 0xA0, 0xCB, 0xE8)),
        [Category.Temp] = (Strings.Map.CategoryTemp, Color.FromArgb(255, 0xB5, 0xB3, 0x5C)),
        [Category.Programs] = (Strings.Map.CategoryPrograms, Color.FromArgb(255, 0xE3, 0x77, 0xC2)),
        [Category.Other] = (Strings.Map.CategoryOther, Color.FromArgb(255, 0x9A, 0x9A, 0x9A)),
    };

    readonly ObservableCollection<DirNode> _crumbs = [];
    // Paths marked for deletion in any recommendation; drawn in green.
    readonly HashSet<string> _cleanable = new(StringComparer.OrdinalIgnoreCase);
    // Shapes use only a few color and alpha combinations, so one brush per color is shared.
    readonly Dictionary<Color, SolidColorBrush> _brushes = new();
    // Gap stroke between sectors, shared by all of them; changing its color is enough to switch theme.
    readonly SolidColorBrush _gap = new();
    // Folders whose children are visible in the chart, with their shape (which subfolders they had) at the last render.
    readonly Dictionary<DirNode, int> _drawnShapes = [];
    readonly AccessibilitySettings _accessibility = new();
    readonly UISettings _uiSettings = new();
    bool _highContrast;
    long _renderedSize;
    Size _renderedArea;
    DispatcherQueueTimer? _resizeTimer;
    bool _rescanningFolder;

    DirNode? ShownNode => _crumbs.Count > 0 ? _crumbs[^1] : null;
    bool RingsView => ViewBar.SelectedItem == RingsItem;
    // Rings or Map: the tabs that draw the chart.
    bool ChartView => !DuplicatesView && !TypesView && !LargestView && !FolderListShown;

    void InitMap()
    {
        Crumbs.ItemsSource = _crumbs;
        SetLegendColumns(_legendColumns);
        FillLegend();
        InitAgeColors();

        // Tab enters the chart once; the arrow keys move between first-level shapes.
        MapCanvas.TabFocusNavigation = KeyboardNavigationMode.Once;
        MapCanvas.XYFocusKeyboardNavigation = XYFocusKeyboardNavigationMode.Enabled;

        // Dragging the window edge fires dozens of SizeChanged events per second; render once it stops.
        _resizeTimer = DispatcherQueue.CreateTimer();
        _resizeTimer.Interval = TimeSpan.FromMilliseconds(120);
        _resizeTimer.IsRepeating = false;
        _resizeTimer.Tick += (_, _) => Render();

        ApplyContrast();
        // ColorValuesChanged also fires on high contrast changes; subscribing to AccessibilitySettings.HighContrastChanged
        // throws 0x80070490 in a desktop app. The event arrives on another thread. The chart redraws in ApplyTheme, which runs next.
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyContrast);
    }

    // Legend for the current color mode: categories or age bands.
    void FillLegend()
    {
        Legend.Children.Clear();
        Legend.RowDefinitions.Clear();
        IEnumerable<(string Label, Color Color)> groups = AgeColors ? Ages : Categories.Values;
        int i = 0;
        foreach (var (label, color) in groups)
        {
            if (i % _legendColumns == 0) Legend.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var item = LegendItem(label, color);
            Grid.SetRow(item, i / _legendColumns);
            Grid.SetColumn(item, i % _legendColumns);
            Legend.Children.Add(item);
            i++;
        }
    }

    // In high contrast everything uses system colors, so the color legend is hidden.
    void ApplyContrast()
    {
        _highContrast = _accessibility.HighContrast;
        Legend.Visibility = ColorModeBox.Visibility = _highContrast ? Visibility.Collapsed : Visibility.Visible;
    }

    Color SystemColor(UIElementType type) => _uiSettings.UIElementColor(type);

    SolidColorBrush BrushOf(Color color)
    {
        if (!_brushes.TryGetValue(color, out var brush)) _brushes[color] = brush = new SolidColorBrush(color);
        return brush;
    }

    void Navigate(DirNode node)
    {
        _crumbs.Clear();
        for (var n = node; n is not null; n = n.Parent) _crumbs.Insert(0, n);
        UpButton.IsEnabled = _crumbs.Count > 1;
        // Trimmed names hide part of the path; the tooltip shows it whole.
        ToolTipService.SetToolTip(Crumbs, node.Path);
        Render();
    }

    // "Mostrar en el gráfico" from any tab: if neither Rings nor Map is shown, switch to Rings.
    // A delete or rescan may have removed the node from the tree after the menu opened.
    void ShowInChart(DirNode node)
    {
        if (_root is not { } root || !node.IsAttachedTo(root)) return;
        if (!ChartView) ViewBar.SelectedItem = RingsItem;
        Navigate(node);
    }

    void GoUp()
    {
        if (_crumbs.Count > 1) Navigate(_crumbs[^2]);
    }

    void Up_Click(object sender, RoutedEventArgs e) => GoUp();

    void Crumbs_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args) => Navigate(_crumbs[args.Index]);

    void ViewBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        ApplyView();
        Render();
    }

    void MapHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Rounded(e.NewSize) == _renderedArea) return;
        _resizeTimer?.Stop();
        _resizeTimer?.Start();
    }

    static Size Rounded(Size size) => new(Math.Round(size.Width), Math.Round(size.Height));

    void Render()
    {
        MapCanvas.Children.Clear();
        _drawnShapes.Clear();
        // The chart is hidden on the Duplicates tab; it draws when Rings or Map is selected again.
        if (DuplicatesView || ShownNode is not { } current) return;
        RefreshCleanable();
        UpdatePathSize(current);
        _renderedSize = current.Size;
        if (TypesView)
        {
            ShowTypes(current);
            return;
        }
        if (LargestView)
        {
            ShowLargest(current);
            return;
        }
        if (FolderListShown)
        {
            ShowFolderList(current);
            return;
        }
        _renderedArea = Rounded(new Size(MapHost.ActualWidth, MapHost.ActualHeight));
        if (RingsView) RenderRings(current, MapHost.ActualWidth, MapHost.ActualHeight);
        else RenderTreemap(current, MapHost.ActualWidth, MapHost.ActualHeight);
    }

    // Recomputes the paths marked in any selection. Returns true if they differ from the last render.
    bool RefreshCleanable()
    {
        var now = AllSelections().SelectMany(r => r.Targets).Select(t => t.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (now.SetEquals(_cleanable)) return false;
        _cleanable.Clear();
        _cleanable.UnionWith(now);
        return true;
    }

    bool IsClean(DirNode node) => !node.IsFiles && _cleanable.Contains(node.Path);

    Color ColorOf(DirNode node) => IsClean(node) ? CleanColor : GroupOf(node).Color;

    void UpdatePathSize(DirNode node) => PathSize.Text = $"{DirNode.Format(node.Size)} · {FileCount(node)}";

    static string FileCount(DirNode node) => Plural(node.FileCount, Strings.Map.FileOne, Strings.Map.FileMany);

    // A folder's subfolders, by identity. Loose files count only by presence: their node is recreated
    // on every rescan even when nothing changed.
    static int ShapeOf(DirNode node)
    {
        var hash = new HashCode();
        foreach (var child in node.ChildrenOrEmpty) hash.Add(child.IsFiles ? null : child, ReferenceEqualityComparer.Instance);
        return hash.ToHashCode();
    }

    // A redraw creates thousands of elements. After disk changes, redraw only if the difference is visible
    // (subfolders of a visible folder changed, or the shown folder's size changed by more than 0.5%).
    // ponytail: a child can grow visibly while the total stays under 0.5%; it shows on the next render.
    bool NeedsRender(DirNode shown, IReadOnlyList<DirNode> changed) =>
        changed.Any(n => _drawnShapes.TryGetValue(n, out int shape) && shape != ShapeOf(n))
        || Math.Abs(shown.Size - _renderedSize) > shown.Size / 200;

    // Click or Enter opens the folder. Right click, Shift+F10 or the Menu key opens the context menu.
    // The tooltip shows path, size, file count and, if any, growth since the saved snapshot.
    // The node goes in Tag and the handlers are shared: a lambda that captures its own element forms a
    // .NET-COM cycle that WinUI never frees, and with thousands of shapes per render memory grows without bound.
    void Wire(FrameworkElement element, DirNode node)
    {
        element.Tag = node;
        string growth = GrowthNote(node);
        ToolTipService.SetToolTip(element, $"{node.Path}\n{DirNode.Format(node.Size)} · {FileCount(node)}{AgeNote(node)}{(growth.Length > 0 ? "\n" + growth : "")}");
        element.Tapped += Element_Tapped;
        element.ContextRequested += Element_ContextRequested;
    }

    // First chart level: a control that takes focus and that Narrator reads. Inner levels are not focusable:
    // with hundreds of shapes, tabbing through them is useless.
    ContentControl Focusable(FrameworkElement content, DirNode node)
    {
        var host = new ContentControl { Content = content, IsTabStop = true, UseSystemFocusVisuals = true };
        string marked = IsClean(node) ? ", " + Strings.Map.MarkedForDeletion : "";
        AutomationProperties.SetName(host,
            $"{node.Name}, {GroupOf(node).Label}, {DirNode.Format(node.Size)}, {FileCount(node)}{marked}{(GrowthNote(node) is { Length: > 0 } growth ? ", " + growth : "")}");
        host.KeyDown += Element_KeyDown;
        return host;
    }

    async void Element_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        await ActivateAsync((DirNode)((FrameworkElement)sender).Tag);
    }

    async void Element_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)) return;
        e.Handled = true;
        var node = (DirNode)((FrameworkElement)sender).Tag;
        await ActivateAsync(node);
        // Opening a folder redraws and removes the focused element, so focus moves to the first new shape.
        if (ShownNode == node && MapCanvas.Children.OfType<ContentControl>().FirstOrDefault() is { } first)
            first.Focus(FocusState.Keyboard);
    }

    async Task ActivateAsync(DirNode node)
    {
        if (!node.IsFiles && node.ChildrenOrEmpty.Count > 0) Navigate(node);
        else if (node.IsFiles) await OpenBrowseAsync(node.Parent!);
    }

    void Element_ContextRequested(UIElement sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        var element = (FrameworkElement)sender;
        var menu = NodeMenu((DirNode)element.Tag);
        // A keyboard request has no position; anchor the menu to the element.
        if (e.TryGetPosition(element, out var point)) menu.ShowAt(element, point);
        else menu.ShowAt(element);
    }

    void Center_Tapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        GoUp();
    }

    static void Segment_PointerEntered(object sender, PointerRoutedEventArgs e) => ((UIElement)sender).Opacity = 0.7;

    static void Segment_PointerExited(object sender, PointerRoutedEventArgs e) => ((UIElement)sender).Opacity = 1;

    MenuFlyout NodeMenu(DirNode node)
    {
        var folder = node.IsFiles ? node.Parent! : node;
        var menu = new MenuFlyout();
        if (!node.IsFiles && node.ChildrenOrEmpty.Count > 0) menu.Items.Add(Item(Strings.Map.EnterItem, "", () => Navigate(node)));
        menu.Items.Add(Item(Strings.Map.OpenInExplorerItem, "", () => Shell.Open(folder.Path)));
        menu.Items.Add(Item(Strings.Map.CopyPathItem, "", () => CopyText(folder.Path)));
        menu.Items.Add(ItemAsync(Strings.Map.ExportCsvItem, "", () => ExportCsvAsync(folder)));
        // Partial folder (C:\Users with only the user folder scanned): deleting or rescanning it would touch unscanned content.
        if (folder.IsPartial) return menu;
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(ItemAsync(node.IsFiles ? Strings.Map.ChooseFilesItem : Strings.Map.ChooseHereItem, "", () => OpenBrowseAsync(folder)));
        // The user folder, AppData, Windows and whole drives are not offered for deletion (see Paths.IsProtected).
        bool deletable = !node.IsFiles && !Paths.IsProtected(node.Path);
        // A path already covered by another selection is not offered: its bytes would count twice.
        if (deletable && _manual.CoverageOf(node.Path) == Coverage.Full)
            menu.Items.Add(ItemAsync(Strings.Map.RemoveChosenItem, "", () => RemoveManualAsync(node.Path)));
        else if (deletable && !IsMarked(node.Path))
            menu.Items.Add(Item(Strings.Map.AddChosenItem, "", () => AddManual(node)));
        var rescan = ItemAsync(Strings.Map.RescanFolderItem, "", () => RescanFolderAsync(folder));
        // Only one scan at a time, full or single-folder.
        rescan.IsEnabled = !_scanning && !_rescanningFolder;
        menu.Items.Add(rescan);
        if (deletable)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(ItemAsync(Strings.Map.DeleteItem, "", () => DeleteAsync([node.Path], node.Path, node.Size)));
        }
        return menu;
    }

    void RenderTreemap(DirNode current, double width, double height)
    {
        _drawnShapes[current] = ShapeOf(current);
        foreach (var tile in Treemap.Layout(current.ChildrenOrEmpty, 0, 0, width, height))
        {
            // Large folders show a second level inside, as in the reference design.
            bool nested = !tile.Node.IsFiles && tile.W > 140 && tile.H > 100;
            AddTile(tile, inner: false, nested);
            if (!nested) continue;
            _drawnShapes[tile.Node] = ShapeOf(tile.Node);
            foreach (var child in Treemap.Layout(tile.Node.ChildrenOrEmpty, tile.X + 6, tile.Y + 30, tile.W - 12, tile.H - 36))
                AddTile(child, inner: true, nested: false);
        }
    }

    void AddTile(Tile t, bool inner, bool nested)
    {
        double w = t.W - 4, h = t.H - 4;
        if (w < 3 || h < 3) return;
        var node = t.Node;
        bool clean = IsClean(node);
        // High contrast: window background and text-color border; marked tiles use the system highlight color.
        var (fill, stroke) = _highContrast
            ? (BrushOf(SystemColor(UIElementType.Window)), BrushOf(SystemColor(clean ? UIElementType.Highlight : UIElementType.WindowText)))
            : (BrushOf(WithAlpha(ColorOf(node), inner ? (byte)0x48 : (byte)0x30)), BrushOf(WithAlpha(ColorOf(node), 0xC0)));

        // The Grid paints its own background and border. Only marked tiles get a Rectangle on top: the dashed border
        // tells them apart without relying on color.
        var tileGrid = new Grid { Width = w, Height = h, CornerRadius = new CornerRadius(6), Background = fill };
        if (clean) tileGrid.Children.Add(new Rectangle { RadiusX = 6, RadiusY = 6, Stroke = stroke, StrokeThickness = 2, StrokeDashArray = [4, 2] });
        else
        {
            tileGrid.BorderBrush = stroke;
            tileGrid.BorderThickness = new Thickness(1);
        }
        // Text is not hit-testable: clicks still reach the Grid and there are fewer elements to hit-test.
        if (w > 44 && h > 22)
            tileGrid.Children.Add(new TextBlock { Text = node.Name, FontSize = 12, Margin = new Thickness(8, 5, 8, 0), TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false });
        if (nested && w > 180)
            tileGrid.Children.Add(new TextBlock
            {
                Text = DirNode.Format(node.Size), FontFamily = Mono, FontSize = 12, Margin = new Thickness(8, 5, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.7, IsHitTestVisible = false,
            });
        if (!nested && w > 50 && h > 48)
            tileGrid.Children.Add(new TextBlock
            {
                Text = DirNode.Format(node.Size), FontFamily = Mono, FontWeight = FontWeights.SemiBold,
                FontSize = w * h > 40000 ? 22 : 13, Margin = new Thickness(8, 0, 8, 6),
                VerticalAlignment = VerticalAlignment.Bottom, TextTrimming = TextTrimming.CharacterEllipsis, IsHitTestVisible = false,
            });

        FrameworkElement element = inner ? tileGrid : Focusable(tileGrid, node);
        Wire(element, node);
        Canvas.SetLeft(element, t.X + 2);
        Canvas.SetTop(element, t.Y + 2);
        MapCanvas.Children.Add(element);
    }

    void RenderRings(DirNode current, double width, double height)
    {
        double cx = width / 2, cy = height / 2;
        double outer = Math.Min(width, height) / 2 - 6, hole = outer * 0.26;
        if (outer < 40) return;
        double ring = (outer - hole) / RingDepth;
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        _gap.Color = GapColor(dark);
        // Border of sectors marked for deletion: dashed and text-colored, so it does not rely on green alone.
        var marked = BrushOf(_highContrast ? SystemColor(UIElementType.WindowText)
            : dark ? Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0xE0, 0x00, 0x00, 0x00));

        _drawnShapes[current] = ShapeOf(current);
        var skipped = new HashSet<DirNode>();
        foreach (var arc in Sunburst.Layout(current, RingDepth, 0.5))
        {
            double r0 = hole + (arc.Depth - 1) * ring, r1 = r0 + ring;
            // An arc under 3 px on the outer edge is invisible. Its children are skipped too, or they would float detached.
            if (arc.Sweep * Math.PI / 180 * r1 < 3 || arc.Node.Parent is { } parent && skipped.Contains(parent))
            {
                skipped.Add(arc.Node);
                continue;
            }
            if (arc.Depth < RingDepth) _drawnShapes[arc.Node] = ShapeOf(arc.Node);

            bool clean = IsClean(arc.Node);
            // The first ring sits inside a focusable control, so the wedge is drawn relative to its bounding box.
            var box = arc.Depth == 1 ? WedgeBounds(cx, cy, r0, r1, arc.Start, arc.Sweep) : new Rect(0, 0, 0, 0);
            var path = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = Wedge(cx - box.X, cy - box.Y, r0, r1, arc.Start, arc.Sweep),
                Fill = _highContrast
                    ? BrushOf(SystemColor(clean ? UIElementType.Highlight : UIElementType.Window))
                    : BrushOf(WithAlpha(ColorOf(arc.Node), (byte)(0xE6 - (arc.Depth - 1) * 0x24))),
                Stroke = clean ? marked : _gap,
                StrokeThickness = clean ? 2 : 1,
            };
            if (clean) path.StrokeDashArray = [3, 2];

            FrameworkElement element = arc.Depth == 1 ? Focusable(path, arc.Node) : path;
            element.PointerEntered += Segment_PointerEntered;
            element.PointerExited += Segment_PointerExited;
            Wire(element, arc.Node);
            Canvas.SetLeft(element, box.X);
            Canvas.SetTop(element, box.Y);
            MapCanvas.Children.Add(element);
        }

        // Center: total size of the current folder. Click to go up one level.
        var center = new Grid { Width = hole * 2, Height = hole * 2 };
        var centerFill = _highContrast ? SystemColor(UIElementType.Window) : WithAlpha(GroupOf(current).Color, 0x50);
        center.Children.Add(new Ellipse { Fill = BrushOf(centerFill), Stroke = _gap });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 2 };
        label.Children.Add(new TextBlock { Text = current.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = hole * 1.6, TextTrimming = TextTrimming.CharacterEllipsis });
        // In a small chart the hole is narrower than the text: the Viewbox scales it down instead of letting it spill over the rings.
        label.Children.Add(new Viewbox
        {
            StretchDirection = StretchDirection.DownOnly,
            MaxWidth = hole * 1.7,
            Child = new TextBlock { Text = DirNode.Format(current.Size), FontFamily = Mono, FontWeight = FontWeights.SemiBold, FontSize = Math.Clamp(hole / 4, 14, 30) },
        });
        if (current.Parent is not null)
            label.Children.Add(new TextBlock { Text = Strings.Map.ClickToGoUp, FontSize = 11, Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Center });
        center.Children.Add(label);
        center.Tag = current;
        center.Tapped += Center_Tapped;
        center.ContextRequested += Element_ContextRequested;
        Canvas.SetLeft(center, cx - hole);
        Canvas.SetTop(center, cy - hole);
        MapCanvas.Children.Add(center);
    }

    // Point at distance r from the center; angles are degrees clockwise from 12 o'clock.
    static Point At(double cx, double cy, double r, double angle)
    {
        double rad = (angle - 90) * Math.PI / 180;
        return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
    }

    // Ring sector between radii r0 and r1.
    static PathGeometry Wedge(double cx, double cy, double r0, double r1, double start, double sweep)
    {
        sweep = Math.Min(sweep, 359.99);
        bool large = sweep > 180;
        var figure = new PathFigure { StartPoint = At(cx, cy, r1, start), IsClosed = true };
        figure.Segments.Add(new ArcSegment { Point = At(cx, cy, r1, start + sweep), Size = new Size(r1, r1), IsLargeArc = large, SweepDirection = SweepDirection.Clockwise });
        figure.Segments.Add(new LineSegment { Point = At(cx, cy, r0, start + sweep) });
        figure.Segments.Add(new ArcSegment { Point = At(cx, cy, r0, start), Size = new Size(r0, r0), IsLargeArc = large, SweepDirection = SweepDirection.Counterclockwise });
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    // Wedge bounding box: its four corners plus the outer-edge points on each axis (0°, 90°...) it crosses.
    static Rect WedgeBounds(double cx, double cy, double r0, double r1, double start, double sweep)
    {
        List<Point> points = [At(cx, cy, r0, start), At(cx, cy, r0, start + sweep), At(cx, cy, r1, start), At(cx, cy, r1, start + sweep)];
        for (double a = Math.Ceiling(start / 90) * 90; a < start + sweep; a += 90) points.Add(At(cx, cy, r1, a));
        double left = points.Min(p => p.X), top = points.Min(p => p.Y);
        return new Rect(left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
    }

    // Color swatch and name. The name wraps in a narrow column ("Documentos y multimedia" did not fit at 1640 px),
    // so the swatch sits on the first line.
    static Grid LegendItem(string label, Color color)
    {
        var item = new Grid { ColumnSpacing = 6 };
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        item.ColumnDefinitions.Add(new ColumnDefinition());
        item.Children.Add(new Rectangle { Width = 9, Height = 9, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
        // One line per entry: a wrapped name makes its row taller and takes height from the chart. The tooltip shows it whole.
        var text = new TextBlock { Text = label, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTipService.SetToolTip(item, label);
        Grid.SetColumn(text, 1);
        item.Children.Add(text);
        return item;
    }

    static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    // Full rescan of one folder from the context menu. It can take a while; ScanRing shows progress.
    async Task RescanFolderAsync(DirNode folder)
    {
        if (_scanning || _rescanningFolder || _root is null) return;
        _rescanningFolder = true;
        ScanRing.IsActive = true;
        ScanInfo.Text = string.Format(CultureInfo.CurrentCulture, Strings.Map.Rescanning, folder.Name);
        try
        {
            // Snapshot the children here on the UI thread; the tree is never read from the thread pool.
            var known = DiskScanner.Known(folder);
            var path = folder.Path;
            var children = await Task.Run(() => DiskScanner.Rescan(path, known, deep: true), CancellationToken.None);
            // If a full scan started meanwhile, the tree and the progress ring belong to it.
            if (_scanning || _root is null) return;
            // A folder that left the tree meanwhile (deleted from the app or by the watcher) has already subtracted its
            // size from its ancestors; applying the rescan now would change them again.
            if (!folder.IsAttachedTo(_root))
            {
                ScanInfo.Text = string.Format(CultureInfo.CurrentCulture, Strings.Map.RescanDiscarded, folder.Name);
                return;
            }
            DiskScanner.Apply(folder, children);
            ScanInfo.Text = string.Format(CultureInfo.CurrentCulture, Strings.Map.FolderUpdated, folder.Name, DirNode.Format(folder.Size));
            RebindSelections();
            await AfterTreeChangedAsync([folder]);
        }
        finally
        {
            _rescanningFolder = false;
            if (!_scanning) ScanRing.IsActive = false;
        }
    }

    // A deep rescan creates new nodes for every subfolder. Selections that point at old nodes are resolved
    // again by path; otherwise their sizes would stay at the pre-rescan values.
    void RebindSelections()
    {
        if (_root is not { } root) return;
        foreach (var rec in AllSelections())
        {
            if (rec.Targets.Any(t => t.Node is { } n && !n.IsAttachedTo(root)) || rec.Nodes.Any(n => !n.IsAttachedTo(root)))
                rec.Restore([.. rec.Targets], root.Find);
        }
        // A recommendation whose folders no longer exist (a leftover deleted in File Explorer) leaves the list:
        // with zero folders there is nothing to show or delete.
        foreach (var list in new[] { _safe, _review, _leftovers })
            foreach (var rec in list.Where(r => r.Nodes.Count == 0).ToList())
            {
                list.Remove(rec);
                if (_owner == rec) CloseDetail();
            }
    }
}
