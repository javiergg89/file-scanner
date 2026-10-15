using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FileScanner;

// Row in the List tab. BarWidth is the share of the total in pixels (FolderBarWidth = 100 %).
public sealed class FolderRow(DirNode node, long total)
{
    public DirNode Node { get; } = node;
    public string Name => Node.Name;
    public string Path => Node.Path;
    public string Glyph => Node.IsFiles ? "" : "";
    public string SizeText => DirNode.Format(Node.Size);
    public string ShareText => Share.ToString("P1", CultureInfo.CurrentCulture);
    public double BarWidth => Share * MainWindow.FolderBarWidth;
    public string FilesText => $"{Node.FileCount:N0}";
    // Folders that group drives ("Este equipo") have no date.
    public string DateText => Node.NewestWriteUtc == default ? "" : Node.NewestWriteUtc.ToLocalTime().ToString(Strings.Views.DateFormat, CultureInfo.CurrentCulture);
    double Share => total > 0 ? (double)Node.Size / total : 0;

    // Narrator reads the ListView row name from ToString().
    public override string ToString() => $"{Name}, {SizeText}, {ShareText}, {FilesText} {(Node.FileCount == 1 ? Strings.Views.FileOne : Strings.Views.FileMany)}, {DateText}";
}

// List tab: children of the path's folder in sortable columns. It reads only the visible level, not the subtree,
// so it needs no background thread even with millions of files in the tree.
public sealed partial class MainWindow
{
    public const double FolderBarWidth = 56;
    FolderColumn _listColumn = FolderColumn.Size;
    bool _listDescending = true;

    bool FolderListShown => ViewBar.SelectedItem == ListTab;

    void ShowFolderList(DirNode node)
    {
        _drawnShapes[node] = ShapeOf(node);
        FolderListView.ItemsSource = FolderList.Sorted(node.ChildrenOrEmpty, _listColumn, _listDescending)
            .Select(child => new FolderRow(child, node.Size)).ToList();
        FolderListEmpty.Visibility = node.ChildrenOrEmpty.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateListHeader();
    }

    void UpdateListHeader()
    {
        foreach (var (button, label, sortBy) in new[]
        {
            (SortName, Strings.Views.ColumnName, Strings.Views.SortByName),
            (SortSize, Strings.Views.ColumnSize, Strings.Views.SortBySize),
            (SortFiles, Strings.Views.ColumnFiles, Strings.Views.SortByFiles),
            (SortModified, Strings.Views.ColumnModified, Strings.Views.SortByModified),
        })
        {
            bool sorted = Enum.Parse<FolderColumn>((string)button.Tag) == _listColumn;
            button.Content = sorted ? $"{label} {(_listDescending ? "↓" : "↑")}" : label;
            AutomationProperties.SetName(button, sorted
                ? string.Format(CultureInfo.CurrentCulture, _listDescending ? Strings.Views.SortedDescending : Strings.Views.SortedAscending, label)
                : sortBy);
        }
    }

    void ListHeader_Click(object sender, RoutedEventArgs e)
    {
        (_listColumn, _listDescending) = FolderList.Toggle(_listColumn, _listDescending,
            Enum.Parse<FolderColumn>((string)((FrameworkElement)sender).Tag));
        if (ShownNode is { } node) ShowFolderList(node);
    }

    // Click, Enter or Space opens the folder, as in the chart. Loose files open the breakdown on the right.
    async void FolderList_ItemClick(object sender, ItemClickEventArgs e)
    {
        var node = ((FolderRow)e.ClickedItem).Node;
        await ActivateAsync(node);
        // The clicked row disappears after opening; focus goes back to the list for keyboard use.
        if (ShownNode == node) FolderListView.Focus(FocusState.Programmatic);
    }

    void FolderList_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: FolderRow row } source) ShowMenu(NodeMenu(row.Node), source, args);
    }
}
