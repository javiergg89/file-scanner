using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileScanner;

// Live update: files deleted or created outside the app (Explorer, a terminal) show up without a rescan.
public sealed partial class MainWindow
{
    readonly HashSet<string> _dirty = new(StringComparer.OrdinalIgnoreCase);
    // Renames within one folder are applied to the tree without rereading its contents.
    readonly List<(string Old, string New)> _renames = [];
    readonly Lock _dirtyLock = new();
    readonly List<FileSystemWatcher> _watchers = [];
    // Used to recreate the watchers when one stops working.
    string[] _watchRoots = [];
    DispatcherQueueTimer? _flushTimer;
    bool _flushing;

    // One watcher per scanned folder: the user profile and, if chosen, Program Files or ProgramData.
    void StartWatching(IEnumerable<string> roots)
    {
        StopWatching();
        _watchRoots = [.. roots];
        foreach (var root in _watchRoots.Where(Directory.Exists))
        {
            if (Watch(root) is { } watcher) _watchers.Add(watcher);
            // The folder vanished after Directory.Exists (drive removed) or does not support change notifications (network).
            else ShowStatus(InfoBarSeverity.Warning, string.Format(CultureInfo.CurrentCulture, Strings.Views.WatchUnavailable, root));
        }

        _flushTimer ??= CreateFlushTimer();
        _flushTimer.Start();
    }

    FileSystemWatcher? Watch(string root)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(root)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 64 * 1024,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
            };
            watcher.Created += (_, e) => MarkDirty(e.FullPath);
            watcher.Deleted += (_, e) => MarkDirty(e.FullPath);
            watcher.Changed += (_, e) => MarkDirty(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                MarkDirty(e.OldFullPath);
                MarkDirty(e.FullPath);
                if (string.Equals(Path.GetDirectoryName(e.OldFullPath), Path.GetDirectoryName(e.FullPath), StringComparison.OrdinalIgnoreCase))
                    lock (_dirtyLock) _renames.Add((e.OldFullPath, e.FullPath));
            };
            watcher.Error += (sender, e) =>
            {
                var error = e.GetException();
                DispatcherQueue.TryEnqueue(() => OnWatcherError((FileSystemWatcher)sender, error));
            };
            watcher.EnableRaisingEvents = true;
            return watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            watcher?.Dispose();
            return null;
        }
    }

    void OnWatcherError(FileSystemWatcher watcher, Exception error)
    {
        // Buffer overflow: the watcher keeps running, but events were lost and the tree sizes may be wrong.
        if (error is InternalBufferOverflowException)
        {
            ShowStatus(InfoBarSeverity.Warning, Strings.Views.WatchOverflow);
            return;
        }

        // Any other error (folder deleted, drive removed) stops the watcher for good, so all watchers are recreated.
        // If it was already stopped or replaced (new scan, window closed), there is nothing to recreate.
        if (!_watchers.Contains(watcher)) return;
        ShowStatus(InfoBarSeverity.Warning, string.Format(CultureInfo.CurrentCulture, Strings.Views.WatchRestarted, error.Message));
        StartWatching(_watchRoots);
    }

    DispatcherQueueTimer CreateFlushTimer()
    {
        // Batches event bursts (a large delete raises thousands) and applies them at most once every 1.5 s.
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1.5);
        timer.Tick += async (_, _) => await FlushAsync();
        return timer;
    }

    void StopWatching()
    {
        _flushTimer?.Stop();
        foreach (var watcher in _watchers) watcher.Dispose();
        _watchers.Clear();
    }

    // App data folder (cache, exclusions, crash.log). Writes there are not user changes.
    static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileScanner");

    // Marks the folder that contains the path, since that folder is the one to reread.
    // The app's own writes are ignored: they are tiny and the next scan counts them.
    void MarkDirty(string path)
    {
        if (Paths.IsSameOrUnder(path, AppDataDir) || Path.GetDirectoryName(path) is not { } dir) return;
        lock (_dirtyLock) _dirty.Add(dir);
    }

    async Task FlushAsync()
    {
        if (_scanning || _flushing || _root is null) return;
        List<string> dirs;
        List<(string Old, string New)> renames;
        lock (_dirtyLock)
        {
            if (_dirty.Count == 0 && _renames.Count == 0) return;
            dirs = [.. _dirty];
            _dirty.Clear();
            renames = [.. _renames];
            _renames.Clear();
        }

        _flushing = true;
        try
        {
            // Renames go first so the parent's Rescan finds the folder under its new name and keeps it.
            foreach (var (old, nw) in renames)
            {
                if (_root.Find(old) is not { IsFiles: false, IsPartial: false } n || _root.Find(nw) is not null) continue;
                DiskScanner.Rename(n, nw);
                // Selections hold the old path and would delete whatever is created later under that name (a regenerated obj).
                // They are cleared; the user can select the folder again under its new name.
                foreach (var rec in AllSelections().Where(r => r.Targets.Any(t => Paths.IsSameOrUnder(t.Path, old))))
                    rec.Prune(t => Paths.IsSameOrUnder(t.Path, old));
            }

            // Nearest folder present in the tree. Rescan keeps existing subfolders, so a parent and a child from the
            // same batch can be applied in any order and sizes still add up.
            // Intermediate folders (C:\Users) are not reread: that would add folders the user chose not to scan.
            var nodes = dirs.Select(d => _root.FindNearest(d)).OfType<DirNode>().Where(n => !n.IsPartial).Distinct().ToList();
            // Known subfolders are copied here on the UI thread, because Rescan runs off it and must not read the tree.
            var jobs = nodes.Select(n => (Node: n, Path: n.Path, Known: DiskScanner.Known(n))).ToList();
            var results = await Task.Run(() => jobs.Select(j => (j.Node, Result: DiskScanner.Rescan(j.Path, j.Known, deep: false))).ToList(), CancellationToken.None);
            // A deleted folder leaves the tree when its parent is applied; applying it afterwards would subtract its size twice.
            foreach (var (node, result) in results)
                if (node.IsAttachedTo(_root)) DiskScanner.Apply(node, result);
            // A folder deleted and created again is a different node, so selections are resolved by path again.
            RebindSelections();
            await AfterTreeChangedAsync(nodes);
        }
        finally
        {
            _flushing = false;
        }
    }

    // After a tree change: drops selections of paths that no longer exist and refreshes only the affected visible part.
    // forceRender: after a delete made from the app, always redraw, even when the difference is small.
    async Task AfterTreeChangedAsync(IReadOnlyList<DirNode> changed, bool forceRender = false)
    {
        if (_root is null) return;
        _cacheDirty = true;
        // Only selections under a changed folder are checked on disk, off the UI thread:
        // with thousands of duplicates selected that is thousands of disk queries.
        List<string> changedPaths = [.. changed.Select(n => n.Path)];
        List<string> candidates = [.. AllSelections().SelectMany(r => r.Targets).Select(t => t.Path)];
        var gone = await Task.Run(() => candidates
            .Where(p => changedPaths.Exists(c => Paths.IsSameOrUnder(p, c)) && !File.Exists(p) && !Directory.Exists(p))
            .ToHashSet(StringComparer.OrdinalIgnoreCase), CancellationToken.None);
        foreach (var rec in AllSelections()) rec.Prune(t => gone.Contains(t.Path));

        // If the shown folder is gone, move up to the nearest one still in the tree.
        if (ShownNode is { } current && !current.IsAttachedTo(_root)) Navigate(_root.FindNearest(current.Path) ?? _root);
        else if (ShownNode is { } shown)
        {
            if (forceRender || NeedsRender(shown, changed)) Render();
            else UpdatePathSize(shown);
        }

        // The breakdown is reread only if the folder it shows changed (its files are read from disk).
        if (_owner is not null && DetailPanel.Visibility == Visibility.Visible
            && _detailPath.LastOrDefault() is { } open && changed.Contains(open))
            await ShowLevelAsync();

        UpdateTotals();
        RefreshGrowth();
    }
}
