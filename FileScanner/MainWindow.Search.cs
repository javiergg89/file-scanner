using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FileScanner;

// Folder search suggestion. Node is null in the "no results" row.
public sealed class SearchHit(DirNode? node, string name, string folder, string sizeText)
{
    public DirNode? Node { get; } = node;
    public string Name { get; } = name;
    public string Folder { get; } = folder;
    public string SizeText { get; } = sizeText;

    // Narrator reads the suggestion from ToString().
    public override string ToString() => SizeText.Length == 0 ? Name : $"{Name}, {SizeText}, {Folder}";
}

// Path bar search: the largest scanned folders whose name contains the text.
public sealed partial class MainWindow
{
    const int SearchCount = 20;
    // Tree folders captured when the search starts; the search walks them on a background thread.
    List<DirNode>? _searchFolders;
    DirNode? _searchRoot;
    CancellationTokenSource? _search;

    // Captured again on each focus, since the tree may have changed (rescan, deletions).
    void Search_GotFocus(object sender, RoutedEventArgs e) => _searchFolders = null;

    void Search_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CrumbsRow.Visibility != Visibility.Visible) return;
        args.Handled = true;
        SearchBox.Focus(FocusState.Keyboard);
    }

    async void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _search?.Cancel();
        string text = sender.Text;
        if (_root is not { } root || text.Trim().Length == 0)
        {
            sender.ItemsSource = null;
            return;
        }
        if (_searchFolders is null || _searchRoot != root)
        {
            _searchFolders = FolderSearch.Folders(root);
            _searchRoot = root;
        }
        var folders = _searchFolders;
        using var search = _search = new CancellationTokenSource();
        try
        {
            var found = await Task.Run(() => FolderSearch.Find(folders, text, SearchCount, search.Token), search.Token);
            // Folders may have been deleted during the search; only those still in the tree are offered.
            List<SearchHit> hits = [.. found.Where(n => _root is not null && n.IsAttachedTo(_root))
                .Select(n => new SearchHit(n, n.Name, Display(Path.GetDirectoryName(n.Path) ?? ""), DirNode.Format(n.Size)))];
            sender.ItemsSource = hits.Count > 0 ? hits : [new SearchHit(null, Strings.Views.NoMatchingFolders, "", "")];
        }
        catch (OperationCanceledException)
        {
            // Replaced by the search for newer text.
        }
        finally
        {
            if (_search == search) _search = null;
        }
    }

    // Enter with no suggestion picked opens the first one.
    void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var hit = args.ChosenSuggestion as SearchHit ?? (sender.ItemsSource as List<SearchHit>)?.FirstOrDefault();
        if (hit?.Node is not { } node || _root is null || !node.IsAttachedTo(_root)) return;
        sender.ItemsSource = null;
        Navigate(node);
        Announce(string.Format(CultureInfo.CurrentCulture, Strings.Views.FolderOpened, node.Name, DirNode.Format(node.Size)));
    }
}
