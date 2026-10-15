using System.Collections.ObjectModel;
using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileScanner;

// Duplicates tab: finds files with the same content and lets the user mark the extra copies.
// Marked copies go into a manual selection, so the script and "Eliminar…" handle them like any other selection.
public sealed partial class MainWindow
{
    ObservableCollection<DuplicateGroupItem> _duplicates = [];
    // Separate from the Recommendations selections: this tab's buttons act only on the marked copies.
    readonly Recommendation _duplicateSelection = new(Strings.Duplicates.SelectionTitle, Risk.Manual, "", []);
    CancellationTokenSource? _duplicateSearch;
    // Files the last search could not read; null until the first search finishes.
    int? _duplicateSkipped;
    // An exclusion was removed after the search; what it hid comes back only with a new search.
    bool _exclusionRemoved;

    bool DuplicatesView => ViewBar.SelectedItem == DuplicatesItem;

    // Duplicates hides the breadcrumbs, map and legend; List, Types and Largest hide only the map and legend.
    void ApplyView()
    {
        DuplicateList.ItemsSource ??= _duplicates;
        bool duplicates = DuplicatesView, types = TypesView, largest = LargestView, list = FolderListShown;
        if (!types) CancelTypes();
        if (!largest) CancelLargest();
        DuplicatesPanel.Visibility = duplicates ? Visibility.Visible : Visibility.Collapsed;
        TypesPanel.Visibility = types ? Visibility.Visible : Visibility.Collapsed;
        LargestPanel.Visibility = largest ? Visibility.Visible : Visibility.Collapsed;
        FolderListPanel.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
        CrumbsRow.Visibility = SearchBox.Visibility = duplicates ? Visibility.Collapsed : Visibility.Visible;
        MapHost.Visibility = LegendPanel.Visibility = duplicates || types || largest || list ? Visibility.Collapsed : Visibility.Visible;
        FitRings();
        // RefreshDuplicateStates skips while the tab is hidden, so it runs here when the tab is shown.
        if (duplicates) RefreshDuplicateStates();
    }

    long MinDuplicateSize() => long.Parse((string)((ComboBoxItem)MinSizeBox.SelectedItem).Tag) << 20;

    // The same button starts the search and cancels it while it runs.
    async void FindDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_duplicateSearch is { } running)
        {
            await running.CancelAsync();
            return;
        }
        using var search = _duplicateSearch = new CancellationTokenSource();
        FindDuplicatesButton.Content = Strings.Duplicates.CancelButton;
        // A new search starts with no marks and an empty list: old marks can point to copies that are gone,
        // and marks made during the search would be checked against the old groups.
        _duplicateSelection.Clear();
        _duplicateSkipped = null;
        _exclusionRemoved = false;
        ShowDuplicates([], replace: true);
        DuplicatesProgress.IsIndeterminate = true;
        DuplicatesProgress.Value = 0;
        DuplicatesProgress.Visibility = Visibility.Visible;
        DuplicatesStatus.Text = Strings.Duplicates.FindingFiles;
        var progress = new Progress<DuplicateProgress>(ShowDuplicateProgress);
        // Threshold read at start: the result belongs to it even if the user changes it mid-search.
        string minMb = (string)((ComboBoxItem)MinSizeBox.SelectedItem).Tag;
        try
        {
            var targets = DiskScanner.Targets(ScanRoots());
            var result = await Duplicates.FindAsync(targets, MinDuplicateSize(), _exclusions, progress, search.Token);
            _duplicateSkipped = result.Skipped;
            // FindAsync used the exclusions from the start; this also applies those added during the search.
            ShowDuplicates(WithoutExclusions(result.Groups), replace: true, string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.NoneFound, minMb));
            Announce(DuplicatesStatus.Text);
        }
        catch (OperationCanceledException)
        {
            DuplicatesStatus.Text = Strings.Duplicates.SearchCanceled;
            Announce(DuplicatesStatus.Text);
        }
        finally
        {
            _duplicateSearch = null;
            FindDuplicatesButton.Content = Strings.Duplicates.FindButton;
            DuplicatesProgress.Visibility = Visibility.Collapsed;
        }
    }

    void ShowDuplicateProgress(DuplicateProgress p)
    {
        DuplicatesProgress.IsIndeterminate = p.Total == 0;
        DuplicatesProgress.Value = p.Total > 0 ? 100.0 * p.Done / p.Total : 0;
        if (p.Total == 0) DuplicatesStatus.Text = string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.ProgressFiles, p.Stage, p.Done);
        else if (p.InBytes) DuplicatesStatus.Text = string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.ProgressBytes, p.Stage, DirNode.Format(p.Done), DirNode.Format(p.Total));
        else DuplicatesStatus.Text = string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.ProgressCount, p.Stage, p.Done, p.Total);
    }

    // replace: a new result; the whole collection is swapped at once. Otherwise groups is what remains of the shown groups,
    // in the same order, after a delete or exclusion: only changed groups are removed or rebuilt, so the scroll position stays.
    // emptyText: text to show when no group is left.
    void ShowDuplicates(IReadOnlyList<DuplicateGroup> groups, bool replace, string? emptyText = null)
    {
        if (replace)
        {
            _duplicates = [.. groups.Select(g => new DuplicateGroupItem(g, Display))];
            DuplicateList.ItemsSource = _duplicates;
        }
        else
        {
            int next = 0;
            for (int i = 0; i < _duplicates.Count;)
            {
                var old = _duplicates[i].Group;
                if (next < groups.Count && groups[next].Hash == old.Hash)
                {
                    if (groups[next].Files.Count != old.Files.Count) _duplicates[i] = new DuplicateGroupItem(groups[next], Display);
                    next++;
                    i++;
                }
                else _duplicates.RemoveAt(i);
            }
        }
        MarkExtraButton.IsEnabled = _duplicates.Count > 0;
        ShowDuplicatesSummary(emptyText ?? Strings.Duplicates.NothingToShow);
        RefreshDuplicateStates();
    }

    // Summary of the shown groups; rebuilt after each delete or exclusion.
    void ShowDuplicatesSummary(string emptyText)
    {
        if (_duplicateSkipped is not { } skipped) return;
        string text = _duplicates.Count == 0
            ? emptyText
            : string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.Summary,
                Plural(_duplicates.Count, Strings.Duplicates.GroupOne, Strings.Duplicates.GroupMany), DirNode.Format(_duplicates.Sum(g => g.Group.Wasted)));
        if (skipped > 0) text += " · " + (skipped == 1 ? Strings.Duplicates.SkippedOne : string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.SkippedMany, skipped));
        if (_exclusionRemoved) text += ". " + Strings.Duplicates.SearchAgain;
        DuplicatesStatus.Text = text;
    }

    static string Display(string path) => Paths.IsSameOrUnder(path, Home) ? "~" + path[Home.Length..] : path;

    // Deleted by any selection: this tab's or a recommendation (for example the whole Downloads folder).
    // This is what counts when checking that a group keeps at least one copy.
    bool IsMarked(string path) => MarkedBy(path) is not null;

    Recommendation? MarkedBy(string path) => AllSelections().FirstOrDefault(r => r.CoverageOf(path) == Coverage.Full);

    bool InDuplicateSelection(string path) => _duplicateSelection.CoverageOf(path) == Coverage.Full;

    // Runs on every selection change and walks every copy, so it returns early while the tab is hidden.
    void RefreshDuplicateStates()
    {
        if (!DuplicatesView) return;
        foreach (var group in _duplicates) RefreshDuplicateGroup(group);
        int count = _duplicateSelection.TargetCount;
        DuplicateSelectionText.Text = count == 0
            ? Strings.Duplicates.NoCopiesSelected
            : string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.SelectedSummary,
                DirNode.Format(_duplicateSelection.SelectedSize), Plural(count, Strings.Duplicates.CopyOne, Strings.Duplicates.CopyMany));
        DuplicateClearButton.IsEnabled = DuplicateCopyButton.IsEnabled = DuplicateDeleteButton.IsEnabled = count > 0;
    }

    void RefreshDuplicateGroup(DuplicateGroupItem group)
    {
        foreach (var file in group.Files)
        {
            string path = file.File.Path;
            file.Update(InDuplicateSelection(path), AllRecommendations().FirstOrDefault(r => r.CoverageOf(path) == Coverage.Full)?.Title);
        }
    }

    // Stays on disk: no selection marks it and no unfinished queued delete covers it.
    bool Survives(string path, IReadOnlySet<string> pending) => !IsMarked(path) && !UnderAny(path, pending) && File.Exists(path);

    void DuplicateCheck_Click(object sender, RoutedEventArgs e)
    {
        var item = (DuplicateFileItem)((FrameworkElement)sender).DataContext;
        if (InDuplicateSelection(item.File.Path)) _duplicateSelection.Remove(item.File.Path, ListFolder);
        // Checked here instead of CoveredBy, which can predate a mark made in Recommendations:
        // if a recommendation already covers the copy, adding it here would count it twice.
        else if (MarkedBy(item.File.Path)?.Title is { } title)
            ShowStatus(InfoBarSeverity.Informational, string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.AlreadyDeletedBy, title));
        else if (item.Group.Files.Exists(f => f != item && Survives(f.File.Path, PendingDeletes()))) _duplicateSelection.AddFile(item.File.Path, item.File.Size);
        else ShowStatus(InfoBarSeverity.Warning, Strings.Duplicates.KeepOneCopy + " " + WhyOthersGo(item));
        // The click already toggled the CheckBox; this resets it to the model state. OnSelectionChanged refreshes the rest.
        RefreshDuplicateGroup(item.Group);
    }

    // Why no other copy survives: which selections delete the others, or that they are no longer on disk.
    string WhyOthersGo(DuplicateFileItem item)
    {
        var titles = item.Group.Files.Where(f => f != item)
            .Select(f => MarkedBy(f.File.Path)?.Title)
            .OfType<string>().Distinct().ToList();
        return titles.Count == 0
            ? Strings.Duplicates.OthersGone
            : string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.OthersDeletedBy,
                string.Join(", ", titles.Select(t => string.Format(CultureInfo.CurrentCulture, Strings.Duplicates.Quoted, t))));
    }

    // In each group, leaves the oldest surviving copy unmarked and marks the rest.
    void MarkExtra_Click(object sender, RoutedEventArgs e)
    {
        var added = new List<Target>();
        var pending = PendingDeletes();
        foreach (var group in _duplicates)
        {
            var keep = group.Files.Find(f => Survives(f.File.Path, pending));
            if (keep is null) continue;
            added.AddRange(group.Files.Where(f => f != keep && !IsMarked(f.File.Path))
                .Select(f => new Target(f.File.Path, false, null, f.File.Size)));
        }
        // OnSelectionChanged refreshes the list.
        if (added.Count > 0) _duplicateSelection.SetFiles([.. _duplicateSelection.Targets, .. added]);
    }

    void DuplicateClear_Click(object sender, RoutedEventArgs e)
    {
        _duplicateSelection.Clear();
        RefreshDuplicateStates();
    }

    void DuplicateCopy_Click(object sender, RoutedEventArgs e)
    {
        if (CopyText(Cleanup.BuildScript([_duplicateSelection])))
            ShowStatus(InfoBarSeverity.Success, Strings.Duplicates.ScriptCopied);
    }

    async void DuplicateDelete_Click(object sender, RoutedEventArgs e) =>
        await DeleteTargetsAsync([.. _duplicateSelection.Targets], Strings.Duplicates.DeleteTitle);

    // True if path or one of its parent folders is in folders. Walks up the path like CoverageOf: comparing every copy with
    // every path froze the window with thousands of copies and tens of thousands of deleted paths.
    static bool UnderAny(string path, IReadOnlySet<string> folders)
    {
        if (folders.Count == 0) return false;
        for (string? p = path; p is not null; p = Path.GetDirectoryName(p))
            if (folders.Contains(p)) return true;
        return false;
    }

    // First copy of a group that this delete would leave with no other copy on disk, or null.
    // Checked at delete time as well as at mark time: a recommendation (Downloads) can contain the kept copy,
    // files deleted outside the app stay listed until the next search, and a queued delete can remove another copy.
    // The groups are read here on the UI thread; disk access runs off it.
    async Task<string?> LastCopyLostAsync(IReadOnlyList<string> paths)
    {
        if (_duplicates.Count == 0) return null;
        var deleting = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var gone = PendingDeletes();
        gone.UnionWith(deleting);
        var groups = _duplicates.Select(g => g.Group.Files).ToList();
        return await Task.Run(() => groups
            .Where(files => files.Any(f => UnderAny(f.Path, deleting)))
            .Where(files => !files.Any(f => !UnderAny(f.Path, gone) && File.Exists(f.Path)))
            .Select(files => files[0].Path)
            .FirstOrDefault(), CancellationToken.None);
    }

    // After a delete from the app: drops deleted copies (including those inside a deleted folder)
    // and groups left with one copy. Disk checks run off the UI thread.
    async Task ForgetDeletedDuplicatesAsync(IReadOnlySet<string> deleted)
    {
        if (_duplicates.Count == 0) return;
        var shown = _duplicates;
        var groups = shown.Select(g => g.Group).ToList();
        var remaining = await Task.Run(() => groups
            .Select(g => g with { Files = [.. g.Files.Where(f => !UnderAny(f.Path, deleted) && File.Exists(f.Path))] })
            .Where(g => g.Files.Count > 1)
            .ToList(), CancellationToken.None);
        // A new search replaced the list meanwhile; this result belongs to groups no longer shown.
        if (shown != _duplicates) return;
        // Only groups still in the list (an exclusion may have removed some meanwhile), in list order.
        var still = _duplicates.Select(g => g.Group.Hash).ToHashSet(StringComparer.Ordinal);
        ShowDuplicates([.. remaining.Where(g => still.Contains(g.Hash))], replace: false);
    }
}
