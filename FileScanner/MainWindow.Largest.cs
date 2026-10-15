using System.ComponentModel;
using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileScanner;

// Row in the Largest tab. IsChecked: true if picked here; null if another selection already deletes it
// (CoveredBy names which one); false otherwise.
public sealed partial class LargestItem(LargeFile file, string folder) : INotifyPropertyChanged
{
    public LargeFile File { get; } = file;
    public string Name { get; } = Path.GetFileName(file.Path);
    public string Folder { get; } = folder;
    public string SizeText => DirNode.Format(File.Size);
    public string DateText => File.LastWriteUtc.ToLocalTime().ToString(Strings.Views.DateFormat, CultureInfo.CurrentCulture);
    public bool? IsChecked { get; private set; }
    public string CoveredText { get; private set; } = "";

    // Always raise: a click flips the CheckBox, and it has to be set back to the model value even when that did not change.
    public void Update(bool chosen, string? coveredBy)
    {
        IsChecked = chosen ? true : coveredBy is null ? false : null;
        CoveredText = chosen || coveredBy is null ? "" : string.Format(CultureInfo.CurrentCulture, Strings.Views.IncludedIn, coveredBy);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CoveredText)));
    }

    // Narrator reads the ListView row name from ToString().
    public override string ToString() => $"{Name}, {SizeText}, {Folder}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

// Largest tab: the largest files under the path's folder, subfolders included.
// As in Types, the tree stores no file names: they are read from disk on a background thread each time the folder changes.
public sealed partial class MainWindow
{
    const int LargestCount = 100;
    CancellationTokenSource? _largestRead;
    // Folder and size of what the list shows or is reading; if both match, the disk is not read again.
    (DirNode Node, long Size)? _largestKey;
    List<LargestItem> _largest = [];

    bool LargestView => ViewBar.SelectedItem == LargestTab;

    async void ShowLargest(DirNode node)
    {
        var key = (node, node.Size);
        if (_largestKey == key) return;
        _largestKey = key;
        _largestRead?.Cancel();
        using var read = _largestRead = new CancellationTokenSource();
        var folders = DiskScanner.FileFolders(node);
        _largest = [];
        LargestList.ItemsSource = null;
        LargestStatus.Text = Strings.Views.ReadingFiles;
        LargestProgress.Visibility = Visibility.Visible;
        try
        {
            var files = await Task.Run(() => DiskScanner.Largest(folders, LargestCount, read.Token), read.Token);
            _largest = [.. files.Select(f => new LargestItem(f, Display(Path.GetDirectoryName(f.Path) ?? "")))];
            LargestList.ItemsSource = _largest;
            RefreshLargestStates();
            LargestStatus.Text = files.Count == 0
                ? Strings.Views.NoFiles
                : string.Format(CultureInfo.CurrentCulture, Strings.Views.LargestSummary,
                    files.Count == LargestCount
                        ? string.Format(CultureInfo.CurrentCulture, Strings.Views.LargestTop, LargestCount)
                        : Plural(files.Count, Strings.Views.FileOne, Strings.Views.FileMany),
                    DirNode.Format(files.Sum(f => f.Size)));
            Announce(LargestStatus.Text);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a read of another folder, or the tab was left: that code decides what is shown.
        }
        finally
        {
            if (_largestRead == read)
            {
                _largestRead = null;
                LargestProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    // Leaving the tab stops the disk read; coming back starts a new one.
    void CancelLargest()
    {
        if (_largestRead is null) return;
        _largestRead.Cancel();
        _largestKey = null;
    }

    void RefreshLargestStates()
    {
        foreach (var item in _largest)
        {
            string path = item.File.Path;
            item.Update(_manual.CoverageOf(path) == Coverage.Full, MarkedBy(path)?.Title);
        }
    }

    async void LargestCheck_Click(object sender, RoutedEventArgs e)
    {
        var item = (LargestItem)((FrameworkElement)sender).DataContext;
        await ToggleChosenAsync(item.File);
        RefreshLargestStates();
    }

    // Files picked here go to the manual selection ("Elegido por ti"), like those added from the chart.
    async Task ToggleChosenAsync(LargeFile file)
    {
        if (_manual.CoverageOf(file.Path) == Coverage.Full) await RemoveManualAsync(file.Path);
        // If another selection already deletes it, adding it here would count the same bytes twice.
        else if (MarkedBy(file.Path)?.Title is { } title)
            ShowStatus(InfoBarSeverity.Informational, string.Format(CultureInfo.CurrentCulture, Strings.Views.AlreadyIncluded, title));
        else _manual.AddFile(file.Path, file.Size);
    }

    void Largest_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: LargestItem item } source) ShowMenu(LargestMenu(item.File), source, args);
    }

    MenuFlyout LargestMenu(LargeFile file)
    {
        var menu = new MenuFlyout();
        menu.Items.Add(Item(Strings.Views.OpenItem, "", () => OpenFile(file.Path)));
        menu.Items.Add(Item(Strings.Views.ShowInExplorerItem, "", () => Shell.Reveal(file.Path)));
        menu.Items.Add(Item(Strings.Views.CopyPathItem, "", () => CopyText(file.Path)));
        if (_root?.FindNearest(file.Path) is { } folder)
            menu.Items.Add(Item(Strings.Views.ShowFolderInChartItem, "", () => ShowInChart(folder)));
        menu.Items.Add(new MenuFlyoutSeparator());
        bool chosen = _manual.CoverageOf(file.Path) == Coverage.Full;
        if (chosen || MarkedBy(file.Path) is null)
            menu.Items.Add(ItemAsync(chosen ? Strings.Views.RemoveFromPicksItem : Strings.Views.AddToPicksItem, chosen ? "" : "", async () =>
            {
                await ToggleChosenAsync(file);
                RefreshLargestStates();
            }));
        menu.Items.Add(ItemAsync(Strings.Views.DeleteItem, "", () => DeleteAsync([file.Path], file.Path, file.Size)));
        return menu;
    }
}
