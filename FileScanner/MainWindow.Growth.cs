using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FileScanner;

// Row in the "Ha crecido" group of Recomendaciones.
public sealed class GrowthItem(DirNode node, long grown, string displayPath)
{
    public DirNode Node { get; } = node;
    public string Name => Node.Name;
    public string DisplayPath { get; } = displayPath;
    public string GrowthText { get; } = "+" + DirNode.Format(grown);
    public string SizeText => DirNode.Format(Node.Size);

    // Narrator reads the ListView row name from ToString().
    public override string ToString() => string.Format(CultureInfo.CurrentCulture, Strings.Views.GrowthRow, Name, GrowthText, SizeText);
}

// Growth since the saved copy: computed when the copy loads (at startup or when the folders change) and reused
// by later rescans of the same scope, always against that copy.
public sealed partial class MainWindow
{
    const int GrowthCount = 10;
    // Previous size of what grew, by path, and when the compared copy was saved.
    Dictionary<string, long>? _grownBefore;
    DateTime _grownSinceUtc;

    void SetGrowth(Dictionary<string, long>? before, DateTime sinceUtc)
    {
        _grownBefore = before;
        _grownSinceUtc = sinceUtc;
    }

    // "+2,3 GB desde hace 3 días" if the folder grew enough; empty otherwise.
    string GrowthNote(DirNode node) =>
        !node.IsFiles && _grownBefore is { } before && before.TryGetValue(node.Path, out long old) && node.Size - old >= Growth.Threshold
            ? string.Format(CultureInfo.CurrentCulture, Strings.Views.GrowthNote, DirNode.Format(node.Size - old), Ago(_grownSinceUtc))
            : "";

    void RefreshGrowth()
    {
        var top = _root is { } root && _grownBefore is { } before ? Growth.Top(root, before, GrowthCount) : [];
        GrowthGroup.Visibility = top.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        GrowthList.ItemsSource = top.ConvertAll(t => new GrowthItem(t.Node, t.Grown, Display(t.Node.Path)));
        GrowthInfo.Text = string.Format(CultureInfo.CurrentCulture, Strings.Views.GrowthInfo, DirNode.Format(Growth.Threshold), Ago(_grownSinceUtc));
    }

    // Click, Enter or Space opens the folder in the chart on the left.
    void GrowthList_ItemClick(object sender, ItemClickEventArgs e)
    {
        ShowInChart(((GrowthItem)e.ClickedItem).Node);
    }

    void GrowthList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: GrowthItem item } source) ShowMenu(NodeMenu(item.Node), source, args);
    }
}
