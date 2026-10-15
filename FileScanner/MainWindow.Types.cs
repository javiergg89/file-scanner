using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace FileScanner;

// Row in the Types tab. BarWidth is the share of the total in pixels (TypeBarWidth = 100 %).
public sealed class TypeItem(string name, string sizeText, string shareText, string countText, double barWidth, Brush brush)
{
    public string Name { get; } = name;
    public string SizeText { get; } = sizeText;
    public string ShareText { get; } = shareText;
    public string CountText { get; } = countText;
    public double BarWidth { get; } = barWidth;
    public Brush Brush { get; } = brush;

    // Narrator reads the ListView row name from ToString().
    public override string ToString() => $"{Name}, {SizeText}, {ShareText}, {CountText}";
}

// Types tab: space per extension for the files under the folder shown in the path bar.
// The tree stores no file names: they are read from disk on a background thread each time the folder changes.
public sealed partial class MainWindow
{
    const double TypeBarWidth = 80;
    CancellationTokenSource? _typesRead;
    // Folder, size and high contrast of what the list shows or is reading; if all match, the disk is not read again.
    (DirNode Node, long Size, bool Contrast)? _typesKey;

    bool TypesView => ViewBar.SelectedItem == TypesItem;

    async void ShowTypes(DirNode node)
    {
        var key = (node, node.Size, _highContrast);
        if (_typesKey == key) return;
        _typesKey = key;
        _typesRead?.Cancel();
        using var read = _typesRead = new CancellationTokenSource();
        var folders = DiskScanner.FileFolders(node);
        TypeList.ItemsSource = null;
        TypesStatus.Text = Strings.Views.ReadingFiles;
        TypesProgress.Visibility = Visibility.Visible;
        try
        {
            var stats = await Task.Run(() => DiskScanner.ByExtension(folders, read.Token), read.Token);
            ShowTypeStats(stats);
            Announce(TypesStatus.Text);
        }
        catch (OperationCanceledException)
        {
            // Replaced by a read of another folder, or the tab was left: that code decides what is shown.
        }
        finally
        {
            if (_typesRead == read)
            {
                _typesRead = null;
                TypesProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    // Leaving the tab stops the disk read; coming back starts a new one.
    void CancelTypes()
    {
        if (_typesRead is null) return;
        _typesRead.Cancel();
        _typesKey = null;
    }

    void ShowTypeStats(List<TypeStat> stats)
    {
        long total = stats.Sum(s => s.Bytes), files = stats.Sum(s => s.Count);
        // Largest types get the legend colors in order; the rest share the "Otros" gray.
        var palette = Categories.Values.Select(v => v.Color).ToList();
        TypeList.ItemsSource = stats.Select((s, i) =>
        {
            double share = total > 0 ? (double)s.Bytes / total : 0;
            var color = _highContrast ? SystemColor(UIElementType.Highlight) : palette[Math.Min(i, palette.Count - 1)];
            return new TypeItem(s.Extension.Length > 0 ? s.Extension : Strings.Views.NoExtension, DirNode.Format(s.Bytes),
                share.ToString("P1", CultureInfo.CurrentCulture), Plural(s.Count, Strings.Views.FileOne, Strings.Views.FileMany), share * TypeBarWidth, BrushOf(color));
        }).ToList();
        TypesStatus.Text = stats.Count == 0
            ? Strings.Views.NoFiles
            : string.Format(CultureInfo.CurrentCulture, Strings.Views.TypesSummary, Plural(stats.Count, Strings.Views.TypeOne, Strings.Views.TypeMany),
                DirNode.Format(total), Plural(files, Strings.Views.FileOne, Strings.Views.FileMany));
    }
}
