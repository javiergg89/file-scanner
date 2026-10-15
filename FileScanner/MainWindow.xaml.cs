using System.Diagnostics;
using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace FileScanner;

public sealed partial class MainWindow : Window
{
    static readonly string Home = App.Demo ? DemoData.Home : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    // Scanned at launch and when nothing is chosen: the system drive (C:\). Demo data lives under Home.
    static readonly string DefaultRoot = App.Demo ? Home : Path.GetPathRoot(Environment.SystemDirectory)!;

    readonly TunedAcrylicBackdrop _backdrop = new();
    readonly System.Collections.ObjectModel.ObservableCollection<DeleteJob> _jobs = [];
    // Delete queue turn: one job at a time, in confirmation order.
    readonly SemaphoreSlim _deleteTurn = new(1, 1);
    // Paths of each confirmed delete until it finishes.
    readonly List<IReadOnlyList<string>> _pendingDeletes = [];
    // A delete confirmation is being prepared or shown.
    bool _confirmingDelete;
    readonly List<CheckBox> _driveItems = [];
    DirNode? _root;
    // On-disk copy of the shown scan (one per set of scanned folders).
    string? _cacheFile;
    // The tree changed (watcher or deletes) since the copy was last saved.
    bool _cacheDirty;
    bool _scanning;
    // Another scan was requested during a running one: it runs afterwards with the latest scope.
    bool _rescanPending;
    CancellationTokenSource? _scanCancel;
    // Folders confirmed with "Escanear" (or picked with "Elegir carpeta…", drag and drop or launch), and those of the scan on screen.
    List<string> _scope = [DefaultRoot];
    List<string> _shownScope = [];
    // Last Recycle Bin size read; the empty dialog uses it without rereading.
    long _recycleBytes;
    DispatcherQueueTimer? _statusTimer;
    // Saves the copy 10 s after the last delete.
    DispatcherQueueTimer? _saveTimer;
    // The window is closing: the first Closing already saved or chose not to.
    bool _closing;

    // launchFolder: folder passed at launch (Explorer context menu); replaces the system drive.
    public MainWindow(Settings settings, string? launchFolder = null)
    {
        _settings = settings;
        InitializeComponent();
        SystemBackdrop = _backdrop;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Taskbar and Alt+Tab icon. The .exe carries the same one (ApplicationIcon).
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        FitToScreen();
        ApplyTextScale();
        _uiSettings.TextScaleFactorChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyTextScale);
        InitMap();
        InitCleanup();
        InitScope();
        if (launchFolder is not null) ChooseFolder(launchFolder);
        InitExclusions();
        QueueList.ItemsSource = _jobs;
        InitSettings();
        AppWindow.Closing += AppWindow_Closing;
        Closed += Window_Closed;
    }

    // Initial size fits the screen work area (1640×940 does not fit a 1366×768 laptop), centered.
    // The minimum size stops footers and cards from being cut off in a narrow window.
    void FitToScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(1640, area.Width), height = Math.Min(940, area.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - width) / 2, area.Y + (area.Height - height) / 2, width, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min(1100, area.Width);
            presenter.PreferredMinimumHeight = Math.Min(680, area.Height);
        }
    }

    // A Windows text size change resizes the right panel (see AdaptLayout in MainWindow.Responsive.cs).
    void ApplyTextScale() => AdaptLayout(Root.ActualWidth);

    // On close, saves the tree if the watcher or a delete changed it since the last copy. Close waits for the write
    // with the window hidden: compressing millions of nodes on the UI thread shows "Not responding".
    async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closing) return;
        if (StartClosing() is not { } save) return;
        args.Cancel = true;
        await save;
        Close();
    }

    // Stops scan, watcher and save timer. With unsaved changes it hides the window and returns the write of the copy.
    // The language restart calls it too, before Close().
    Task? StartClosing()
    {
        _closing = true;
        _scanCancel?.Cancel();
        StopWatching();
        _saveTimer?.Stop();
        if (!_cacheDirty || _root is null || _cacheFile is null || _scanning) return null;
        AppWindow.Hide();
        // Snapshot on the UI thread, as in SaveCacheAsync; compression and write run in the background.
        var snapshot = ScanCache.Snapshot(_root, DateTime.UtcNow);
        string file = _cacheFile;
        // On failure only the next launch's fast load is lost, and there is no window left to warn in.
        return Task.Run(() => ScanCache.WriteSnapshot(file, snapshot), CancellationToken.None);
    }

    void Window_Closed(object sender, WindowEventArgs args)
    {
        _scanCancel?.Cancel();
        StopWatching();
        _saveTimer?.Stop();
    }

    // One checkbox per drive (fixed or removable) in "Qué escanear", next to the program folders.
    void InitScope()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable && d.IsReady))
        {
            string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "" : $" ({drive.VolumeLabel})";
            var item = new CheckBox
            {
                Content = string.Format(CultureInfo.CurrentCulture, Strings.Window.DriveItem, drive.Name.TrimEnd('\\') + label, DirNode.Format(drive.TotalSize - drive.TotalFreeSpace)),
                Tag = drive.RootDirectory.FullName,
            };
            DrivePanel.Children.Add(item);
            _driveItems.Add(item);
        }
    }

    // Each "Qué escanear" checkbox with its folder.
    IEnumerable<(CheckBox Box, string Path)> ScopeChoices()
    {
        string[] fixedPaths = [Home, .. Leftovers.SystemRoots()];
        return new[] { ScopeHome, ScopeProgramFiles, ScopeProgramFilesX86, ScopeProgramData }.Select((box, i) => (box, fixedPaths[i]))
            .Concat(_driveItems.Concat(FolderPanel.Children.OfType<CheckBox>()).Select(box => (box, (string)box.Tag)));
    }

    // On open, the checkboxes show the confirmed scope: boxes ticked without pressing "Escanear" are reset.
    void ScopeFlyout_Opening(object sender, object e)
    {
        foreach (var (box, path) in ScopeChoices()) box.IsChecked = _scope.Contains(path, StringComparer.OrdinalIgnoreCase);
    }

    async void ScopeScan_Click(object sender, RoutedEventArgs e)
    {
        ScopeFlyout.Hide();
        _scope = [.. ScopeChoices().Where(c => c.Box.IsChecked == true).Select(c => c.Path)];
        // With nothing ticked, scans the system drive, as at launch.
        if (_scope.Count == 0) _scope = [DefaultRoot];
        UpdateScopeText();
        await ScanAsync();
    }

    void UpdateScopeText() => ScopeText.Text = ScanScope.Label(_scope, Home);

    async void Root_Loaded(object sender, RoutedEventArgs e) => await ScanAsync();

    async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    void CancelScan_Click(object sender, RoutedEventArgs e) => _scanCancel?.Cancel();

    // Folders confirmed in "Qué escanear".
    List<string> ScanRoots() => [.. _scope];

    // Shows the saved copy of the last scan first (if there is one for these folders), then rescans the whole
    // disk in the background and replaces it. Without a copy, shows progress until done.
    // ponytail: the rescan reads the whole disk; skipping unchanged parts needs the NTFS USN journal, which requires admin.
    async Task ScanAsync()
    {
        // One scan at a time: the buttons are disabled, but an event can arrive before they repaint.
        if (_scanning)
        {
            _rescanPending = true;
            return;
        }
        if (App.Demo)
        {
            if (_root is null) await ShowDemoAsync();
            else DemoBlocked();
            return;
        }
        _scanning = true;
        RescanButton.IsEnabled = ScopeButton.IsEnabled = false;
        using var cancel = _scanCancel = new CancellationTokenSource();
        try
        {
            await ScanCoreAsync(cancel.Token);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            ScanCancelled();
        }
        finally
        {
            _scanCancel = null;
            _scanning = false;
            ScanRing.IsActive = false;
            CancelScanButton.Visibility = BusyPanel.Visibility = Visibility.Collapsed;
            RescanButton.IsEnabled = ScopeButton.IsEnabled = true;
        }
        if (_rescanPending)
        {
            _rescanPending = false;
            await ScanAsync();
            return;
        }
        await UpdateRecycleBinAsync();
    }

    async Task ScanCoreAsync(CancellationToken token)
    {
        var roots = ScanRoots();
        var targets = DiskScanner.Targets(roots);
        // A chosen folder that no longer exists (deleted, drive or network disconnected) falls back to the system drive.
        if (targets.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, string.Format(CultureInfo.CurrentCulture, Strings.Window.FolderNotFound, roots[0]));
            _scope = roots = [DefaultRoot];
            UpdateScopeText();
            targets = DiskScanner.Targets(roots);
        }
        var cacheFile = ScanCache.FileFor(targets);
        // Program folders count for leftovers if they were scanned, also as part of a whole drive.
        var leftoverParents = Leftovers.Parents(Home, Leftovers.SystemRoots().Where(r => targets.Exists(t => Paths.IsSameOrUnder(r, t))));
        var installed = Task.Run(Leftovers.ReadInstalled, CancellationToken.None);
        // The watcher starts before the scan: changes made meanwhile are applied at the end.
        StartWatching(targets);
        if (_root is null)
        {
            LoadingRing.IsActive = true;
            LoadingText.Text = Strings.Window.Scanning;
        }

        bool sameScope = cacheFile == _cacheFile;
        // With other folders, the previous growth belonged to the old ones.
        if (!sameScope) SetGrowth(null, default);
        var cached = sameScope ? null : await Task.Run(() => ScanCache.Load(cacheFile), CancellationToken.None);
        if (cached is { } copy)
        {
            // The background rescan shows at the bottom (ScanRing); the center panel would cover the copy.
            BusyPanel.Visibility = Visibility.Collapsed;
            _cacheFile = cacheFile;
            _shownScope = roots;
            Show(copy.Root, await installed, leftoverParents);
            ScanInfo.Text = string.Format(CultureInfo.CurrentCulture, Strings.Window.SavedUpdating, Ago(copy.SavedUtc));
        }
        else if (!sameScope || _root is null)
        {
            _crumbs.Clear();
            MapCanvas.Children.Clear();
            BusyPanel.Visibility = Visibility.Visible;
        }
        else ScanInfo.Text = Strings.Window.Updating;
        ScanRing.IsActive = true;
        CancelScanButton.Visibility = Visibility.Visible;

        var progress = new Progress<string>(path => BusyText.Text = path);
        var clock = Stopwatch.StartNew();
        var root = await Task.Run(() => DiskScanner.ScanRoots(roots, progress, token), CancellationToken.None).WaitAsync(token);
        var elapsed = clock.Elapsed;
        // On the UI thread: a delete also changes the shown copy on this thread. Only walks nodes of 100 MB or more.
        if (cached is { } previous) SetGrowth(Growth.Compare(previous.Root, root), previous.SavedUtc);
        _cacheFile = cacheFile;
        _shownScope = roots;
        Show(root, await installed, leftoverParents);
        ScanInfo.Text = $"{Plural(root.FileCount, Strings.Window.FileOne, Strings.Window.FileMany)} · {elapsed.TotalSeconds:0.0} s";
        Announce(string.Format(CultureInfo.CurrentCulture, Strings.Window.ScanFinished, ScanInfo.Text));
        BusyPanel.Visibility = CancelScanButton.Visibility = Visibility.Collapsed;
        ScanRing.IsActive = false;

        // Snapshot here, on the UI thread: a delete that finishes meanwhile also changes the tree on this thread,
        // so it cannot change it mid-walk. Only compression and write run in the background.
        // Changes made during the write mark the copy dirty again.
        _cacheDirty = false;
        var snapshot = ScanCache.Snapshot(root, DateTime.UtcNow);
        if (!await Task.Run(() => ScanCache.WriteSnapshot(cacheFile, snapshot), CancellationToken.None))
        {
            _cacheDirty = true;
            WarnCacheNotSaved();
        }
    }

    // Demo mode: the invented tree, with no scan, watcher, scan cache or installed programs.
    async Task ShowDemoAsync()
    {
        var root = DemoData.Build(DateTime.UtcNow);
        _shownScope = [.. _scope];
        BusyPanel.Visibility = CancelScanButton.Visibility = Visibility.Collapsed;
        // Enabled as after a real scan; ScanAsync turns their actions into the demo notice.
        RescanButton.IsEnabled = ScopeButton.IsEnabled = true;
        Show(root, [], Leftovers.Parents(Home, []));
        ScanInfo.Text = Plural(root.FileCount, Strings.Window.FileOne, Strings.Window.FileMany);
        await UpdateRecycleBinAsync();
    }

    // Demo mode shows invented paths: actions that would touch the disk, the shell or the clipboard only say so.
    bool DemoBlocked()
    {
        if (App.Demo) ShowStatus(InfoBarSeverity.Informational, Strings.Window.DemoDisabled);
        return App.Demo;
    }

    void OpenInExplorer(string path)
    {
        if (!DemoBlocked()) Shell.Open(path);
    }

    void RevealInExplorer(string path)
    {
        if (!DemoBlocked()) Shell.Reveal(path);
    }

    // Cancel leaves what was on screen: the saved copy of the new scope, or the previous scan with its scope.
    void ScanCancelled()
    {
        _scope = [.. _shownScope];
        UpdateScopeText();
        if (_root is null)
        {
            StopWatching();
            LoadingRing.IsActive = false;
            LoadingText.Text = Strings.Window.ScanCancelledStart;
            ScanInfo.Text = Strings.Window.ScanCancelled;
        }
        else
        {
            StartWatching(DiskScanner.Targets(_shownScope));
            if (ShownNode is null) Navigate(_root);
            ScanInfo.Text = Strings.Window.ScanCancelledStale;
        }
        Announce(ScanInfo.Text);
    }

    void WarnCacheNotSaved() =>
        ShowStatus(InfoBarSeverity.Warning, Strings.Window.CacheNotSaved);

    // Replaces the shown tree; the folder open in the explorer and the ticked items stay.
    void Show(DirNode root, IReadOnlyList<InstalledApp> installed, IReadOnlyList<string> leftoverParents)
    {
        string? shownPath = ShownNode?.Path;
        _root = root;
        LoadingPanel.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = false;
        Sections.Visibility = Visibility.Visible;
        LoadRecommendations(root, installed, leftoverParents);
        Navigate(shownPath is null ? root : root.FindNearest(shownPath) ?? root);
        UpdateTotals();
        RefreshGrowth();
    }

    static string Ago(DateTime utc) => (DateTime.UtcNow - utc) switch
    {
        { TotalMinutes: < 1 } => Strings.Window.AgoNow,
        { TotalHours: < 1 } span => string.Format(CultureInfo.CurrentCulture, Strings.Window.AgoMinutes, (int)span.TotalMinutes),
        { TotalDays: < 1 } span => string.Format(CultureInfo.CurrentCulture, Strings.Window.AgoHours, (int)span.TotalHours),
        { TotalDays: < 2 } => Strings.Window.AgoDayOne,
        var span => string.Format(CultureInfo.CurrentCulture, Strings.Window.AgoDaysMany, (int)span.TotalDays),
    };

    // "1 elemento", "3 elementos".
    static string Plural(long n, string one, string many) => $"{n:N0} {(n == 1 ? one : many)}";

    // Accessible names for each row's buttons (x:Bind in the templates): with several rows they must differ.
    public static string OpenLabel(string title) => string.Format(CultureInfo.CurrentCulture, Strings.Window.OpenLabel, title);

    public static string EnterLabel(string name) => string.Format(CultureInfo.CurrentCulture, Strings.Window.EnterLabel, name);

    // Targets minus those another target covers: a folder ticked in two selections (Temp and all of AppData\Local,
    // or a duplicate inside Downloads) counts once.
    static List<Target> Outermost(IEnumerable<Target> targets)
    {
        var kept = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        // Shortest path first: an ancestor is always seen before its contents.
        foreach (var t in targets.OrderBy(t => t.Path.Length))
        {
            bool covered = false;
            for (string? p = t.Path; p is not null && !covered; p = Path.GetDirectoryName(p)) covered = kept.ContainsKey(p);
            if (!covered) kept[t.Path] = t;
        }
        return [.. kept.Values];
    }

    void UpdateTotals()
    {
        if (_root is null) return;
        // The Recommendations footer counts only its own items; the top total also adds the Duplicates copies.
        var selected = AllRecommendations().Where(r => r.TargetCount > 0).ToList();
        long selectedSize = Outermost(selected.SelectMany(r => r.Targets)).Sum(t => t.Size);
        // Demo mode does not show the real drive.
        var drive = App.Demo ? null : new DriveInfo(Path.GetPathRoot(Home)!);
        string homeRoot = drive?.RootDirectory.FullName ?? Path.GetPathRoot(Home)!;
        long driveSize = drive?.TotalSize ?? DemoData.DriveSize, driveFree = drive?.AvailableFreeSpace ?? DemoData.DriveFree;
        // "Libre tras la limpieza" is for the user profile drive; items ticked on other drives are listed separately.
        var byDrive = Outermost(AllSelections().SelectMany(r => r.Targets))
            .GroupBy(t => Path.GetPathRoot(t.Path) ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Size), StringComparer.OrdinalIgnoreCase);
        long here = byDrive.GetValueOrDefault(homeRoot);
        var elsewhere = byDrive.Where(d => !d.Key.Equals(homeRoot, StringComparison.OrdinalIgnoreCase)).ToList();
        long safeSize = _safe.Sum(r => r.Size), reviewSize = _review.Concat(_leftovers).Sum(r => r.Size);

        FreeText.Text = DirNode.Format(driveFree + here);
        FreeDelta.Text = elsewhere.Count == 0
            ? "+" + DirNode.Format(here)
            : string.Join(" · ", elsewhere.Select(d => string.Format(CultureInfo.CurrentCulture, Strings.Window.FreeOnDrive, DirNode.Format(d.Value), d.Key.TrimEnd('\\'))).Prepend(string.Format(CultureInfo.CurrentCulture, Strings.Window.FreeHere, DirNode.Format(here))));
        SafeText.Text = SafeTotal.Text = DirNode.Format(safeSize);
        SafeCount.Text = Plural(_safe.Count, Strings.Window.ItemOne, Strings.Window.ItemMany);
        ReviewText.Text = DirNode.Format(reviewSize);
        ReviewTotal.Text = DirNode.Format(_review.Sum(r => r.Size));
        LeftoverTotal.Text = DirNode.Format(_leftovers.Sum(r => r.Size));
        ReviewCount.Text = Plural(_review.Count + _leftovers.Count, Strings.Window.ItemOne, Strings.Window.ItemMany);
        SafeEmpty.Visibility = _safe.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ReviewEmpty.Visibility = _review.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LeftoverGroup.Visibility = _leftovers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // Scanned size on the user profile drive: the root itself if it is that folder or drive, or its branch under "Este equipo".
        long scannedHere = _root.Find(homeRoot)?.Size ?? (Paths.IsSameOrUnder(_root.Path, homeRoot) ? _root.Size : 0);
        OutsideText.Text = DirNode.Format(Math.Max(0, driveSize - driveFree - scannedHere));
        SelectionText.Text = selected.Count == 0
            ? Strings.Window.NothingSelected
            : string.Format(CultureInfo.CurrentCulture, Strings.Window.SelectedSummary, DirNode.Format(selectedSize), Plural(selected.Count, Strings.Window.GroupOne, Strings.Window.GroupMany));
        CopyButton.IsEnabled = DeleteButton.IsEnabled = ClearButton.IsEnabled = selected.Count > 0;
        SelectButton.IsEnabled = InDetail || _safe.Count > 0;
        ManualGroup.Visibility = _manual.TargetCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // SHQueryRecycleBin walks the Recycle Bin of every drive and can take seconds, so it runs off the UI thread.
    async Task UpdateRecycleBinAsync()
    {
        long bytes = _recycleBytes = App.Demo ? 0 : await Task.Run(Shell.RecycleBinBytes, CancellationToken.None);
        RecycleText.Text = bytes > 0 ? string.Format(CultureInfo.CurrentCulture, Strings.Window.RecycleBinSize, DirNode.Format(bytes)) : Strings.Window.RecycleBinEmpty;
        RecycleButton.IsEnabled = bytes > 0;
    }

    // Gap between ring sectors per theme (system text color in high contrast).
    Color GapColor(bool dark) => _highContrast ? SystemColor(UIElementType.WindowText)
        : dark ? Color.FromArgb(0x90, 0x10, 0x10, 0x10) : Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF);

    // Mouse back button.
    async void Root_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsXButton1Pressed) return;
        e.Handled = true;
        await GoBackAsync();
    }

    async void Back_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // While typing in the search box, Backspace deletes text and does not go up a folder.
        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox) return;
        args.Handled = true;
        await GoBackAsync();
    }

    // Backspace and the mouse back button go up one level in the active panel (breakdown or chart).
    async Task GoBackAsync()
    {
        if (DetailPanel.Visibility == Visibility.Visible) await DetailUpAsync();
        else if (!DuplicatesView) GoUp();
    }

    static MenuFlyoutItem Item(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    // Async menu item: awaited inside the handler so an error is not left unobserved.
    static MenuFlyoutItem ItemAsync(string text, string glyph, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += async (_, _) => await action();
        return item;
    }

    // Opens a file from a menu and reports a failure; uncaught, it would close the app
    // (an exception in a XAML event handler ends the process).
    void OpenFile(string path)
    {
        if (DemoBlocked()) return;
        try
        {
            Shell.Launch(path);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, string.Format(CultureInfo.CurrentCulture, Strings.Window.OpenFailed, Path.GetFileName(path), ex.Message));
        }
    }

    // Clipboard.SetContent throws COMException if another program has the clipboard open; uncaught, it closes the app.
    // Returns false then, so the caller does not announce the copy.
    bool CopyText(string text)
    {
        if (DemoBlocked()) return false;
        var package = new DataPackage();
        package.SetText(text);
        try
        {
            Clipboard.SetContent(package);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            ShowStatus(InfoBarSeverity.Error, Strings.Window.CopyFailed);
            return false;
        }
    }

    // action: optional button inside the notice (e.g. copy a command).
    void ShowStatus(InfoBarSeverity severity, string message, string? actionText = null, Action? action = null)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.ActionButton = action is null ? null : ActionButton(actionText ?? "", action);
        StatusBar.IsOpen = true;
        // Success notices close after 5 s; errors and warnings stay until the user closes them.
        _statusTimer ??= CreateStatusTimer();
        _statusTimer.Stop();
        if (severity == InfoBarSeverity.Success) _statusTimer.Start();
        // InfoBar is announced only when it opens, so a second notice in a row would be silent without this.
        Announce(message);
    }

    static Button ActionButton(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }

    DispatcherQueueTimer CreateStatusTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => StatusBar.IsOpen = false;
        return timer;
    }

    // Narrator reads the text even when focus is elsewhere. Only for one-off changes (scan end, notices);
    // continuous progress (BusyText) would flood it. Raised from SelectionText because it is always visible.
    void Announce(string text) =>
        FrameworkElementAutomationPeer.CreatePeerForElement(SelectionText)?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, text, "status");

    // What will be deleted, shown before confirming: up to 5 paths and a count of the rest. Items from emptying a
    // folder (Temp, caches) show once as that folder with "(contenido)".
    StackPanel DeletePreview(IReadOnlyList<string> paths, string what, long bytes)
    {
        var emptied = AllSelections().SelectMany(r => r.Targets).Where(t => t.ContentsOnly)
            .Select(t => t.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var shown = paths
            .Select(p => Path.GetDirectoryName(p) is { } dir && emptied.Contains(dir) ? string.Format(CultureInfo.CurrentCulture, Strings.Window.FolderContents, Display(dir)) : Display(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var lines = shown.Take(5).ToList();
        if (shown.Count > 5) lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.Window.AndMore, shown.Count - 5));

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = $"{what}\n{Plural(paths.Count, Strings.Window.ItemOne, Strings.Window.ItemMany)} · {DirNode.Format(bytes)}", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 160,
            Content = new TextBlock { Text = string.Join("\n", lines), FontFamily = Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
        });
        panel.Children.Add(new TextBlock { Text = Strings.Window.RecycleBinNote, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    // Deletes the ticked items of some selections. The contents of folders being emptied are read off the UI thread
    // (a full Temp or cache has hundreds of thousands of entries). An item ticked in two selections counts once in the size.
    async Task DeleteTargetsAsync(IReadOnlyList<Target> targets, string what)
    {
        if (targets.Count == 0 || _confirmingDelete || DemoBlocked()) return;
        long bytes = Outermost(targets).Sum(t => t.Size);
        _confirmingDelete = true;
        List<string> paths;
        try
        {
            paths = await Task.Run(() => Cleanup.ExpandTargets(targets), CancellationToken.None);
        }
        finally
        {
            _confirmingDelete = false;
        }
        await DeleteAsync(paths, what, bytes);
    }

    // Paths of confirmed deletes still in progress; Duplicates no longer counts them as remaining copies.
    HashSet<string> PendingDeletes() => _pendingDeletes.SelectMany(p => p).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Confirms and deletes. Recycle Bin is the default; permanent delete frees the space at once.
    async Task DeleteAsync(IReadOnlyList<string> paths, string what, long bytes)
    {
        // No second confirmation while one is prepared or shown: WinUI crashes the app with two ContentDialogs open at once.
        if (paths.Count == 0 || _confirmingDelete || DemoBlocked()) return;
        // Every delete goes through here (Recommendations, Duplicates and menus), so this one check covers all of them.
        if (paths.FirstOrDefault(Paths.IsProtected) is { } vital)
        {
            ShowStatus(InfoBarSeverity.Error, string.Format(CultureInfo.CurrentCulture, Strings.Window.DeleteProtected, vital));
            return;
        }
        _confirmingDelete = true;
        ContentDialogResult result;
        try
        {
            if (await LastCopyLostAsync(paths) is { } lost)
            {
                ShowStatus(InfoBarSeverity.Error, string.Format(CultureInfo.CurrentCulture, Strings.Window.DeleteLastCopy, Path.GetFileName(lost)));
                return;
            }
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.ActualTheme,
                Title = Strings.Window.DeleteTitle,
                Content = DeletePreview(paths, what, bytes),
                PrimaryButtonText = Strings.Window.MoveToRecycleBin,
                SecondaryButtonText = Strings.Window.DeletePermanently,
                CloseButtonText = Strings.Window.Cancel,
                DefaultButton = ContentDialogButton.Primary,
            };
            result = await dialog.ShowAsync();
        }
        finally
        {
            _confirmingDelete = false;
        }
        if (result == ContentDialogResult.None) return;

        // Queued: one delete after another, and the app stays usable to add more.
        var job = new DeleteJob(what, paths, bytes, toRecycleBin: result == ContentDialogResult.Primary);
        _jobs.Add(job);
        _pendingDeletes.Add(paths);
        UpdateQueueButton();
        await _deleteTurn.WaitAsync(CancellationToken.None);
        try
        {
            job.Set(JobState.Running);
            UpdateQueueButton();
            var outcome = await Shell.DeleteWithOutcomeAsync(paths, job.ToRecycleBin);
            var left = outcome.Left;
            var stillThere = new HashSet<string>(left, StringComparer.OrdinalIgnoreCase);
            var deleted = paths.Where(p => !stillThere.Contains(p)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await ForgetDeletedDuplicatesAsync(deleted);
            await ForgetDeletedAsync([.. deleted]);
            job.Finish(outcome);
            // If the user cancelled in the Windows dialog, what remains is not a permission failure.
            if (left.Count > 0 && outcome.Aborted)
                ShowStatus(InfoBarSeverity.Informational, string.Format(CultureInfo.CurrentCulture, Strings.Window.DeleteCancelledLeft, left.Count, paths.Count));
            else if (left.Count > 0) WarnNotDeleted(left, paths.Count);
        }
        finally
        {
            _pendingDeletes.Remove(paths);
            _deleteTurn.Release();
        }
        UpdateQueueButton();
        await UpdateRecycleBinAsync();
    }

    void WarnNotDeleted(List<string> left, int total)
    {
        string message = string.Format(CultureInfo.CurrentCulture, Strings.Window.NotDeleted, left.Count, total, left[0]);
        // Program Files, ProgramData and Windows can only be modified as administrator; the app runs unelevated.
        var systemDirs = Leftovers.SystemRoots().Append(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (!left.Exists(p => systemDirs.Any(dir => Paths.IsSameOrUnder(p, dir))))
        {
            ShowStatus(InfoBarSeverity.Warning, message);
            return;
        }
        message += "\n" + Strings.Window.NeedsAdmin;
        // "Copiar script" has only the ticked items; items deleted from a context menu are not in it.
        if (left.TrueForAll(p => AllSelections().Any(r => r.CoverageOf(p) == Coverage.Full)))
            ShowStatus(InfoBarSeverity.Warning, message + " " + Strings.Window.UseCopyScript);
        else
            ShowStatus(InfoBarSeverity.Warning, message + " " + Strings.Window.CopyCommandHint, Strings.Window.CopyCommandButton, () => CopyNotDeleted(left));
    }

    // Remove-Item command for what could not be deleted, with the same format and escaping as the cleanup script.
    void CopyNotDeleted(List<string> left)
    {
        var failed = new Recommendation(Strings.Window.NotDeletedTitle, Risk.Manual, "", []);
        // Folders use Remove-Tree: Remove-Item without -Recurse prompts, and accepting the prompt follows junctions.
        foreach (var path in left)
        {
            if (_root?.Find(path) is { IsFiles: false } node) failed.Add(node);
            else if (Directory.Exists(path)) failed.Add(new DirNode { Name = Path.GetFileName(path), Path = path });
            else failed.AddFile(path, File.Exists(path) ? new FileInfo(path).Length : 0);
        }
        if (CopyText(Cleanup.BuildScript([failed])))
            ShowStatus(InfoBarSeverity.Success, Strings.Window.CommandCopied);
    }

    // Removes items just deleted from the app from the tree, without waiting for the watcher. Folders are removed
    // directly (their size is known); for loose files only their folder is reread, without subfolders.
    async Task ForgetDeletedAsync(IReadOnlyList<string> deleted)
    {
        if (_root is null || deleted.Count == 0) return;
        var changed = new List<DirNode>();
        var fileFolders = new HashSet<DirNode>();
        foreach (var path in deleted)
        {
            if (_root.Find(path) is { IsFiles: false, IsPartial: false, Parent: { } parent } folder)
            {
                DiskScanner.Remove(folder);
                changed.Add(parent);
            }
            else if (Path.GetDirectoryName(path) is { } dir && _root.Find(dir) is { IsPartial: false } owner)
            {
                fileFolders.Add(owner);
            }
        }
        // Known subfolders are read here, on the UI thread; only the disk reads run in the background.
        var jobs = fileFolders.Select(f => (Folder: f, f.Path, Known: DiskScanner.Known(f))).ToList();
        var reread = await Task.Run(() => jobs.Select(j => (j.Folder, Result: DiskScanner.Rescan(j.Path, j.Known, deep: false))).ToList(), CancellationToken.None);
        foreach (var (folder, result) in reread)
        {
            if (!folder.IsAttachedTo(_root)) continue;
            DiskScanner.Apply(folder, result);
            changed.Add(folder);
        }
        _cacheDirty = true;
        await AfterTreeChangedAsync(changed, forceRender: true);
        ScheduleCacheSave();
    }

    // Deletes in a row share one save: each save snapshots the whole tree on the UI thread.
    // AppWindow_Closing saves whatever is still unsaved at close.
    void ScheduleCacheSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = DispatcherQueue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromSeconds(10);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += async (_, _) => await SaveCacheAsync();
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // Saves the copy of the current tree. Snapshot on the UI thread; compression and write run in the background.
    async Task SaveCacheAsync()
    {
        if (_root is null || _cacheFile is null || _scanning) return;
        _cacheDirty = false;
        var snapshot = ScanCache.Snapshot(_root, DateTime.UtcNow);
        string file = _cacheFile;
        if (!await Task.Run(() => ScanCache.WriteSnapshot(file, snapshot), CancellationToken.None))
        {
            _cacheDirty = true;
            WarnCacheNotSaved();
        }
    }

    void UpdateQueueButton()
    {
        int running = _jobs.Count(j => j.State == JobState.Running);
        int waiting = _jobs.Count(j => j.State == JobState.Waiting);
        int failed = _jobs.Count(j => j.State == JobState.Failed);
        int cancelled = _jobs.Count(j => j.State == JobState.Cancelled);
        QueueButton.Visibility = _jobs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        QueueRing.IsActive = running > 0;
        QueueRing.Visibility = running > 0 ? Visibility.Visible : Visibility.Collapsed;
        string text = (running, waiting, failed) switch
        {
            ( > 0, > 0, _) => string.Format(CultureInfo.CurrentCulture, Strings.Window.QueueDeletingWaiting, waiting),
            ( > 0, _, _) => Strings.Window.QueueDeleting,
            (_, > 0, _) => string.Format(CultureInfo.CurrentCulture, Strings.Window.QueueWaiting, waiting),
            (_, _, > 0) => failed == 1 ? Strings.Window.QueueWarningsOne : string.Format(CultureInfo.CurrentCulture, Strings.Window.QueueWarningsMany, failed),
            _ when cancelled > 0 => Strings.Window.QueueCancelled,
            _ => Strings.Window.QueueFinished,
        };
        if (text == QueueText.Text) return;
        QueueText.Text = text;
        if (_jobs.Count > 0) Announce(text);
    }

    void ClearFinished_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in _jobs.Where(j => j.IsFinished).ToList()) _jobs.Remove(job);
        // With no jobs the button hides; the open flyout would stay empty with nothing to anchor it.
        if (_jobs.Count == 0) QueueButton.Flyout.Hide();
        UpdateQueueButton();
    }

    // No await before ShowAsync: during an await another click could open a second dialog,
    // and WinUI crashes the app with two ContentDialogs open at once.
    async void EmptyRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (DemoBlocked()) return;
        RecycleButton.IsEnabled = false;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = Strings.Window.EmptyRecycleTitle,
            Content = string.Format(CultureInfo.CurrentCulture, Strings.Window.EmptyRecycleContent, DirNode.Format(_recycleBytes)),
            PrimaryButtonText = Strings.Window.EmptyRecycleButton,
            CloseButtonText = Strings.Window.Cancel,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            RecycleButton.IsEnabled = _recycleBytes > 0;
            return;
        }
        int hr = await Task.Run(Shell.EmptyRecycleBin, CancellationToken.None);
        if (hr == 0) ShowStatus(InfoBarSeverity.Success, Strings.Window.RecycleBinEmptied);
        else ShowStatus(InfoBarSeverity.Error, string.Format(CultureInfo.CurrentCulture, Strings.Window.EmptyRecycleFailed, hr));
        await UpdateRecycleBinAsync();
    }
}
