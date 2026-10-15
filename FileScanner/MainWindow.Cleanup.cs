using System.Collections.ObjectModel;
using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FileScanner;

// Right panel: recommendations and the folder breakdown for choosing what to delete.
public sealed partial class MainWindow
{
    static readonly string ManualTitle = Strings.Cleanup.ManualTitle;

    readonly ObservableCollection<Recommendation> _safe = [];
    readonly ObservableCollection<Recommendation> _review = [];
    readonly ObservableCollection<Recommendation> _leftovers = [];
    readonly ObservableCollection<Recommendation> _manualItems = [];
    // Replaced whole on each level: adding 100,000 entries one by one fires 100,000 ListView notifications.
    ObservableCollection<EntryItem> _entries = [];
    readonly ObservableCollection<string> _detailCrumbs = [];
    // Manual picks are deleted with the folder itself, also when checked again in the breakdown.
    readonly Recommendation _manual = new(ManualTitle, Risk.Manual, "", []) { KeepFolders = false };

    // Open breakdown: the recommendation that owns the checks, its base folders and the path walked from them.
    Recommendation? _owner;
    IReadOnlyList<DirNode> _bases = [];
    // Loose files from manual picks, listed on the first level next to the base folders.
    IReadOnlyList<Entry> _looseFiles = [];
    readonly List<DirNode> _detailPath = [];
    // Each level read is numbered; only the latest result is used.
    int _levelVersion;
    bool _selectionRenderQueued;
    // A breakdown checkbox click is reading the disk.
    bool _toggling;

    void InitCleanup()
    {
        SafeList.ItemsSource = _safe;
        ReviewList.ItemsSource = _review;
        LeftoverList.ItemsSource = _leftovers;
        ManualList.ItemsSource = _manualItems;
        EntryList.ItemsSource = _entries;
        DetailCrumbs.ItemsSource = _detailCrumbs;
        WatchRecommendation(_manual);
        WatchRecommendation(_duplicateSelection);
        _manualItems.Add(_manual);
    }

    IEnumerable<Recommendation> AllRecommendations() => _safe.Concat(_review).Concat(_leftovers).Concat(_manualItems);

    // Recommendations plus the Duplicates tab selection: used for the top total, the green on the map and the cleanup after deleting.
    IEnumerable<Recommendation> AllSelections() => AllRecommendations().Append(_duplicateSelection);

    void WatchRecommendation(Recommendation rec) => rec.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(Recommendation.IsChecked)) OnSelectionChanged();
    };

    // Several recommendations can change in one operation; they are batched into one redraw.
    void OnSelectionChanged()
    {
        if (_selectionRenderQueued) return;
        _selectionRenderQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _selectionRenderQueued = false;
            UpdateTotals();
            // Checked items are green with a dashed border in the rings and the map, so redraw if that changed.
            // ponytail: redraws the whole chart; repainting in place means replicating AddTile and RenderRings shape by shape.
            if (RefreshCleanable() && ChartView) Render();
            RefreshDuplicateStates();
            RefreshLargestStates();
            RefreshEntryStates();
        });
    }

    // Identifies a recommendation across scans. Leftovers use their folder because two can share a name.
    static string KeyOf(Recommendation rec) => rec.Risk == Risk.Leftover ? rec.Nodes.FirstOrDefault()?.Path ?? rec.Title : rec.Title;

    // After a scan: new rules, with the user's earlier checks restored.
    void LoadRecommendations(DirNode root, IReadOnlyList<InstalledApp> installed, IReadOnlyList<string> leftoverParents)
    {
        var previous = AllRecommendations().GroupBy(KeyOf, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.SelectMany(r => r.Targets).ToList(), StringComparer.OrdinalIgnoreCase);
        // The open breakdown is stored as paths and the old tree is released: a single node would retain all of it.
        var open = _owner is null ? null : new
        {
            Key = KeyOf(_owner),
            Bases = _bases.Select(b => b.Path).ToList(),
            Files = _looseFiles,
            Path = _detailPath.Select(d => d.Path).ToList(),
        };
        _levelVersion++;
        _bases = [];
        _detailPath.Clear();
        _safe.Clear();
        _review.Clear();
        _leftovers.Clear();
        foreach (var rec in Cleanup.Recommend(root, Home, DateTime.UtcNow, installed, leftoverParents))
        {
            if (previous.TryGetValue(KeyOf(rec), out var targets)) rec.Restore(targets, root.Find);
            WatchRecommendation(rec);
            (rec.Risk switch { Risk.Safe => _safe, Risk.Leftover => _leftovers, _ => _review }).Add(rec);
        }
        _manual.Restore(previous.GetValueOrDefault(ManualTitle, []), root.Find);

        // The background pass after loading the saved scan must not move the user out of the current folder.
        var owner = open is null ? null : AllRecommendations().FirstOrDefault(r => string.Equals(KeyOf(r), open.Key, StringComparison.OrdinalIgnoreCase));
        IReadOnlyList<DirNode> bases = open is null ? [] : [.. open.Bases.Select(root.Find).OfType<DirNode>()];
        if (open is null || owner is null || bases.Count + open.Files.Count == 0)
        {
            CloseDetail();
            return;
        }
        _owner = owner;
        _bases = bases;
        _looseFiles = open.Files;
        foreach (var path in open.Path)
        {
            if (root.Find(path) is not { } dir) break;
            _detailPath.Add(dir);
        }
        if (SingleBase && _detailPath.Count == 0) _detailPath.Add(bases[0]);
        // ListEntries catches disk errors, so the task cannot fail and is not awaited here.
        _ = ShowLevelAsync();
    }

    static Recommendation RecOf(object sender) => (Recommendation)((FrameworkElement)sender).DataContext;

    void RecCheck_Click(object sender, RoutedEventArgs e)
    {
        var rec = RecOf(sender);
        if (rec.IsChecked == true) rec.Clear();
        else if (rec.Risk != Risk.Manual) rec.SelectAll();
        ((CheckBox)sender).IsChecked = rec.IsChecked;
    }

    async void RecOpen_Click(object sender, RoutedEventArgs e) => await OpenDetailAsync(RecOf(sender));

    async void Rec_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await OpenDetailAsync(RecOf(sender));

    // Footer buttons act on what is shown: the whole list or the folder open in the breakdown.
    bool InDetail => _owner is not null && DetailPanel.Visibility == Visibility.Visible;

    // Folders the open breakdown level covers: the current folder, or the bases on the first level.
    IReadOnlyList<DirNode> ShownFolders => _detailPath.Count > 0 ? [_detailPath[^1]] : _bases;

    IReadOnlyList<Entry> ShownFiles => _detailPath.Count > 0 ? [] : _looseFiles;

    // With one base folder and nothing else on the first level, the breakdown opens that folder directly.
    bool SingleBase => _bases.Count == 1 && _looseFiles.Count == 0;

    // Copy and Delete always act on every recommendation; in the breakdown the button text says so.
    void UpdateFooterButtons()
    {
        SelectButton.Content = InDetail ? Strings.Cleanup.CheckAllButton : Strings.Cleanup.CheckSafeButton;
        CopyButton.Content = InDetail ? Strings.Cleanup.CopyScriptAllButton : Strings.Cleanup.CopyScriptButton;
        DeleteButtonText.Text = InDetail ? Strings.Cleanup.DeleteAllCheckedButton : Strings.Cleanup.DeleteButton;
        SelectButton.IsEnabled = InDetail || _safe.Count > 0;
    }

    async void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (InDetail) await SelectAllShownAsync(_owner!);
        else
        {
            foreach (var r in _safe) r.SelectAll();
            foreach (var r in _review.Concat(_leftovers)) r.Clear();
        }
    }

    // On the first level bases are checked as their checkbox does: whole if the recommendation does not keep folders.
    async Task SelectAllShownAsync(Recommendation owner)
    {
        if (_toggling || _root is not { } root) return;
        bool whole = !owner.KeepFolders && _detailPath.Count == 0;
        var picked = new List<DirNode>();
        foreach (var folder in ShownFolders)
        {
            // Same rule as SelectAllIn: only a base of a recommendation with KeepFolders is emptied.
            bool contentsOnly = !whole && owner.KeepFolders && owner.Nodes.Any(n => Paths.Same(n.Path, folder.Path));
            if (!RefuseVital(folder.Path, contentsOnly)) picked.Add(folder);
        }
        _toggling = true;
        try
        {
            if (whole) foreach (var folder in picked) owner.Add(folder);
            else
            {
                // SelectAllIn merges up to the base and reads every level on the way.
                var (list, disk) = await ReadLevelsAsync(owner, root, picked.Select(f => f.Path));
                if (_owner != owner) return;
                foreach (var folder in picked) owner.SelectAllIn(folder, list, disk);
            }
            foreach (var file in ShownFiles) owner.AddFile(file.Path, file.Size);
        }
        finally
        {
            _toggling = false;
        }
        RefreshEntryStates();
    }

    async void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        if (!InDetail)
        {
            foreach (var r in AllRecommendations()) r.Clear();
            return;
        }
        if (_toggling || _owner is not { } owner || _root is not { } root) return;
        var paths = ShownFolders.Select(f => f.Path).Concat(ShownFiles.Select(f => f.Path)).ToList();
        _toggling = true;
        try
        {
            var (list, _) = await ReadLevelsAsync(owner, root, paths);
            if (_owner != owner) return;
            foreach (var path in paths) owner.Remove(path, list);
        }
        finally
        {
            _toggling = false;
        }
        RefreshEntryStates();
    }

    void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (CopyText(Cleanup.BuildScript(AllRecommendations())))
            ShowStatus(InfoBarSeverity.Success, Strings.Cleanup.ScriptCopied);
    }

    async void DeleteSelection_Click(object sender, RoutedEventArgs e)
    {
        await DeleteTargetsAsync([.. AllRecommendations().SelectMany(r => r.Targets)], Strings.Cleanup.AllCheckedTitle);
    }

    void AddManual(DirNode node) => _manual.Add(node);

    // Removing something inside a chosen folder reads the levels in between, like a breakdown click.
    async Task RemoveManualAsync(string path)
    {
        if (_root is not { } root) return;
        var (list, _) = await ReadLevelsAsync(_manual, root, [path]);
        _manual.Remove(path, list);
    }

    // Breakdown of any folder from the map; checked items go to the manual selection.
    Task OpenBrowseAsync(DirNode folder) => OpenDetailAsync(_manual, [folder]);

    // Manual picks can include loose files (no node); they are listed next to the folders.
    Task OpenDetailAsync(Recommendation rec) => rec.Risk == Risk.Manual
        ? OpenDetailAsync(rec, [.. rec.Targets.Select(t => t.Node).OfType<DirNode>()],
            [.. rec.Targets.Where(t => t.Node is null).Select(t => new Entry(Path.GetFileName(t.Path), t.Path, t.Size, null))])
        : OpenDetailAsync(rec, rec.Nodes);

    Task OpenDetailAsync(Recommendation owner, IReadOnlyList<DirNode> bases, IReadOnlyList<Entry>? files = null)
    {
        _owner = owner;
        _bases = bases;
        _looseFiles = files ?? [];
        _detailPath.Clear();
        if (SingleBase) _detailPath.Add(bases[0]);
        ListPanel.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Visible;
        UpdateFooterButtons();
        // The button that opened it gets hidden, so focus moves to the breakdown.
        return ShowLevelAsync(focus: true);
    }

    void CloseDetail()
    {
        var owner = _owner;
        bool hadFocus = HasFocus(DetailPanel);
        _owner = null;
        _levelVersion++;
        // Drop tree references: after a scan they would retain the old tree whole.
        _bases = [];
        _looseFiles = [];
        _detailPath.Clear();
        _detailCrumbs.Clear();
        _entries.Clear();
        DetailBusy.IsActive = false;
        DetailEmpty.Visibility = Visibility.Collapsed;
        DetailPanel.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Visibility.Visible;
        UpdateFooterButtons();
        if (hadFocus && owner is not null) FocusRecommendation(owner);
    }

    // Focus goes back to the row of the recommendation that was open, or to the top of the list.
    void FocusRecommendation(Recommendation rec)
    {
        var list = rec.Risk switch { Risk.Safe => SafeList, Risk.Leftover => LeftoverList, Risk.Manual => ManualList, _ => ReviewList };
        var target = list.ContainerFromItem(rec) is DependencyObject row ? FocusManager.FindFirstFocusableElement(row) : null;
        ((target ?? FocusManager.FindFirstFocusableElement(ListPanel)) as UIElement)?.Focus(FocusState.Programmatic);
    }

    bool HasFocus(DependencyObject scope)
    {
        for (var d = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
            if (d == scope) return true;
        return false;
    }

    void DetailBack_Click(object sender, RoutedEventArgs e) => CloseDetail();

    // Up one level in the breakdown; from the first level, back to the list.
    async Task DetailUpAsync()
    {
        if (_detailPath.Count > (SingleBase ? 1 : 0))
        {
            _detailPath.RemoveAt(_detailPath.Count - 1);
            await ShowLevelAsync();
        }
        else CloseDetail();
    }

    async void DetailCrumbs_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        // The first crumb is the title and leads to the base folder list (or the single base).
        // With one base, crumb k is _detailPath[k]; with several, crumb k is _detailPath[k - 1].
        int keep = SingleBase ? args.Index + 1 : args.Index;
        _detailPath.RemoveRange(keep, _detailPath.Count - keep);
        await ShowLevelAsync();
    }

    // focus: move focus to the breakdown. Also done when focus was on a row, because clearing the list loses it.
    async Task ShowLevelAsync(bool focus = false)
    {
        if (_owner is null) return;
        int version = ++_levelVersion;
        _detailCrumbs.Clear();
        _detailCrumbs.Add(_owner.Title);
        foreach (var folder in _detailPath.Skip(SingleBase ? 1 : 0)) _detailCrumbs.Add(folder.Name);
        if (SingleBase) _detailCrumbs[0] = $"{_owner.Title}: {_bases[0].Name}";

        focus |= HasFocus(EntryList);
        _entries.Clear();
        DetailEmpty.Visibility = Visibility.Collapsed;
        DetailBusy.IsActive = true;
        var dir = _detailPath.LastOrDefault();
        // Subfolders are copied here: the live tree is only touched from the UI thread.
        var folders = dir is null ? null : DiskScanner.Folders(dir);
        IReadOnlyList<Entry> entries = dir is null
            ? [.. _bases.Select(b => new Entry(b.Path, b.Path, b.Size, b)), .. _looseFiles]
            : await Task.Run(() => DiskScanner.ListEntries(dir.Path, folders!));
        // If another level (or the same one again) was requested during the read, this result is stale.
        if (version != _levelVersion) return;
        DetailBusy.IsActive = false;
        EntryList.ItemsSource = _entries = [.. entries.Select(entry => new EntryItem(entry))];
        DetailEmpty.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshEntryStates();
        if (focus) ((_entries.Count > 0 ? EntryList : FocusManager.FindFirstFocusableElement(DetailPanel)) as UIElement)?.Focus(FocusState.Programmatic);
    }

    void RefreshEntryStates()
    {
        if (_owner is null) return;
        foreach (var item in _entries) item.Update(_owner.CoverageOf(item.Entry.Path));
    }

    // Exclude and Merge read each level between a path and the folder that covers it (or its base).
    // For Temp or a cache that is hundreds of thousands of files, so the levels are read off the UI thread,
    // with subfolders copied first because the tree is only touched on the UI thread.
    async Task<(Func<string, IReadOnlyList<Entry>> List, Func<string, IReadOnlyList<string>?> Disk)> ReadLevelsAsync(
        Recommendation owner, DirNode root, IEnumerable<string> paths)
    {
        var levels = new Dictionary<string, List<DirNode>>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            string? top = owner.CoverOf(path) ?? owner.Nodes.FirstOrDefault(n => Paths.IsSameOrUnder(path, n.Path))?.Path;
            for (string? d = Path.GetDirectoryName(path); top is not null && d is not null && Paths.IsSameOrUnder(d, top); d = Path.GetDirectoryName(d))
                if (!levels.ContainsKey(d)) levels[d] = root.Find(d) is { } n ? DiskScanner.Folders(n) : [];
        }
        var read = await Task.Run(() => levels.ToDictionary(l => l.Key,
            l => (Entries: (IReadOnlyList<Entry>)DiskScanner.ListEntries(l.Key, l.Value), Disk: Cleanup.DiskEntries(l.Key)),
            StringComparer.OrdinalIgnoreCase), CancellationToken.None);
        return (p => read.TryGetValue(p, out var r) ? r.Entries : ListFolder(p),
                p => read.TryGetValue(p, out var r) ? r.Disk : Cleanup.DiskEntries(p));
    }

    IReadOnlyList<Entry> ListFolder(string path) =>
        _root?.Find(path) is { } node ? DiskScanner.ListEntries(node) : [];

    static EntryItem ItemOf(object sender) => (EntryItem)((FrameworkElement)sender).DataContext;

    async void EntryCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_owner is not { } owner || _root is not { } root) return;
        var entry = ItemOf(sender).Entry;
        // On the first level base folders are emptied, not deleted (Temp, caches).
        // Leftovers and manual picks are deleted whole (KeepFolders false).
        bool isBase = _bases.Any(b => Paths.Same(b.Path, entry.Path));
        bool contentsOnly = isBase && owner.KeepFolders;
        // A click while the previous one is still reading is ignored: applied out of order they would invert the check.
        if (_toggling || (owner.CoverageOf(entry.Path) != Coverage.Full && RefuseVital(entry.Path, contentsOnly)))
        {
            RefreshEntryStates();
            return;
        }
        _toggling = true;
        try
        {
            var (list, disk) = await ReadLevelsAsync(owner, root, [entry.Path]);
            // If the breakdown was closed or another one opened meanwhile, the click is stale.
            if (_owner != owner) return;
            owner.Toggle(entry, contentsOnly, list, disk);
        }
        finally
        {
            _toggling = false;
        }
        RefreshEntryStates();
    }

    // Same rule as BuildScript and DeleteAsync: a vital folder is never deleted whole, and a folder holding one is never emptied.
    // The warning comes at check time so the selection never shows something that will not be deleted.
    bool RefuseVital(string path, bool contentsOnly)
    {
        if (!(contentsOnly ? Paths.HoldsProtected(path) : Paths.IsProtected(path))) return false;
        ShowStatus(InfoBarSeverity.Warning, string.Format(CultureInfo.CurrentCulture, Strings.Cleanup.VitalRefused, path));
        return true;
    }

    async void EntryOpen_Click(object sender, RoutedEventArgs e) => await EnterEntryAsync(ItemOf(sender).Entry);

    async void Entry_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await EnterEntryAsync(ItemOf(sender).Entry);

    async Task EnterEntryAsync(Entry entry)
    {
        if (entry.Folder is not { } node) return;
        _detailPath.Add(node);
        await ShowLevelAsync();
    }

}
