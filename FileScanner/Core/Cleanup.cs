using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace FileScanner.Core;

public enum Risk { Safe, Review, Leftover, Manual }

public enum Coverage { None, Partial, Full }

// Something the script deletes. ContentsOnly empties the folder and keeps it (caches, Temp).
public sealed record Target(string Path, bool ContentsOnly, DirNode? Node, long FileSize)
{
    public long Size => Node?.Size ?? FileSize;
}

public sealed partial class Recommendation(string title, Risk risk, string displayPath, IReadOnlyList<DirNode> nodes) : INotifyPropertyChanged
{
    // Indexed by path: with thousands of targets (e.g. Temp with one file excluded), coverage is resolved by walking up the path.
    readonly Dictionary<string, Target> _targets = new(StringComparer.OrdinalIgnoreCase);
    // Folders with a target somewhere inside; they mark the partial state.
    readonly HashSet<string> _partial = new(StringComparer.OrdinalIgnoreCase);

    public string Title { get; } = title;
    public Risk Risk { get; } = risk;
    public IReadOnlyList<DirNode> Nodes { get; private set; } = nodes;
    public IEnumerable<Target> Targets => _targets.Values;
    public int TargetCount => _targets.Count;
    public string DisplayPath => Risk == Risk.Manual ? (_targets.Count == 1 ? Strings.Rules.ItemsOne : string.Format(CultureInfo.CurrentCulture, Strings.Rules.ItemsMany, _targets.Count)) : displayPath;
    public long Size => Risk == Risk.Manual ? SelectedSize : Nodes.Sum(n => n.Size);
    public long SelectedSize => _targets.Values.Sum(t => t.Size);
    public bool? IsChecked
    {
        get
        {
            if (_targets.Count == 0) return false;
            if (Risk == Risk.Manual || IsWhole) return true;
            return null;
        }
    }
    public string SizeText => IsChecked is null ? string.Format(CultureInfo.CurrentCulture, Strings.Rules.SizeOfTotal, DirNode.Format(SelectedSize), DirNode.Format(Size)) : DirNode.Format(Size);

    // true: checking the whole recommendation empties its folders and keeps them (caches, Temp). false: deletes them (program leftovers).
    public bool KeepFolders { get; init; } = true;

    bool IsWhole => _targets.Count == Nodes.Count && Nodes.All(n => _targets.TryGetValue(n.Path, out var t) && t.ContentsOnly == KeepFolders);

    public void SelectAll()
    {
        _targets.Clear();
        foreach (var n in Nodes) _targets[n.Path] = new Target(n.Path, KeepFolders, n, 0);
        Changed();
    }

    public void Clear()
    {
        _targets.Clear();
        Changed();
    }

    public Coverage CoverageOf(string path)
    {
        for (string? p = path; p is not null; p = System.IO.Path.GetDirectoryName(p))
            if (_targets.ContainsKey(p)) return Coverage.Full;
        return _partial.Contains(path) ? Coverage.Partial : Coverage.None;
    }

    // Checks or unchecks a breakdown item. contentsOnly: the item is a base folder that must be kept.
    // list returns a folder's contents; used when excluding something inside a fully checked folder.
    // disk returns a folder's entries on disk (Cleanup.DiskEntries by default); see Merge.
    public void Toggle(Entry entry, bool contentsOnly, Func<string, IReadOnlyList<Entry>> list, Func<string, IReadOnlyList<string>?>? disk = null)
    {
        if (CoverageOf(entry.Path) == Coverage.Full) Exclude(entry.Path, list);
        else
        {
            RemoveUnder(entry.Path);
            _targets[entry.Path] = new Target(entry.Path, contentsOnly, entry.Folder, entry.Size);
            Merge(entry.Path, list, disk ?? Cleanup.DiskEntries);
        }
        Changed();
    }

    // Topmost folder that covers path, or null. Callers use it to read ahead the levels Exclude needs.
    public string? CoverOf(string path)
    {
        for (string? p = path; p is not null; p = System.IO.Path.GetDirectoryName(p))
            if (_targets.ContainsKey(p)) return p;
        return null;
    }

    // Manual selection from the map: the whole folder, folder included.
    public void Add(DirNode node)
    {
        if (CoverageOf(node.Path) == Coverage.Full) return;
        RemoveUnder(node.Path);
        _targets[node.Path] = new Target(node.Path, false, node, 0);
        Changed();
    }

    // Manual selection of a single file (e.g. a duplicate copy). Its size is not in the tree, so it is stored here.
    public void AddFile(string path, long size)
    {
        if (CoverageOf(path) == Coverage.Full) return;
        _targets[path] = new Target(path, false, null, size);
        Changed();
    }

    // Removes path and anything checked under it. If a fully checked folder covered it,
    // the rest of that folder stays checked (list gives its contents).
    public void Remove(string path, Func<string, IReadOnlyList<Entry>> list)
    {
        if (CoverageOf(path) == Coverage.Full) Exclude(path, list);
        RemoveUnder(path);
        Changed();
    }

    // Checks the whole folder. Same rule as Merge: a base folder ends up as when checking the recommendation
    // (Temp is emptied, a program leftover is deleted); any other folder is deleted, folder included.
    // list lets the check move up to the parent folder when this one completes the parent's contents.
    public void SelectAllIn(DirNode folder, Func<string, IReadOnlyList<Entry>>? list = null, Func<string, IReadOnlyList<string>?>? disk = null)
    {
        if (CoverageOf(folder.Path) == Coverage.Full) return;
        RemoveUnder(folder.Path);
        bool isBase = Nodes.Any(n => Paths.Same(n.Path, folder.Path));
        _targets[folder.Path] = new Target(folder.Path, isBase && KeepFolders, folder, 0);
        if (list is not null) Merge(folder.Path, list, disk ?? Cleanup.DiskEntries);
        Changed();
    }

    // Replaces the checked single files in one pass without reading the disk (Duplicates: the caller already did).
    // AddFile or Remove per copy rebuilds the whole index on each call, which is quadratic with thousands of copies.
    public void SetFiles(IEnumerable<Target> files)
    {
        _targets.Clear();
        foreach (var f in files) _targets[f.Path] = f;
        Changed();
    }

    // Restores a previous selection on a new tree (after a rescan); drops what is no longer in it.
    public void Restore(IEnumerable<Target> targets, Func<string, DirNode?> find)
    {
        // Size and IsWhole read Nodes: after a deep rescan they must be the new nodes.
        Nodes = [.. Nodes.Select(n => find(n.Path)).OfType<DirNode>()];
        _targets.Clear();
        foreach (var t in targets)
        {
            // Single files are not in the tree: check the disk.
            if (t.Node is null) { if (File.Exists(t.Path)) _targets[t.Path] = t; }
            else if (find(t.Path) is { } node) _targets[t.Path] = t with { Node = node };
        }
        Changed();
    }

    // Removes targets that no longer exist on disk.
    // If nothing was removed, only sizes changed: a selection change notification would redraw the map for nothing.
    public void Prune(Func<Target, bool> gone)
    {
        var removed = _targets.Values.Where(gone).ToList();
        foreach (var t in removed) _targets.Remove(t.Path);
        if (removed.Count > 0) Changed();
        else NotifySizes();
    }

    public void NotifySizes()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Size)));
    }

    // Unchecks path inside a fully checked folder: replaces the folder with the rest of its contents, level by level.
    void Exclude(string path, Func<string, IReadOnlyList<Entry>> list)
    {
        if (CoverOf(path) is not { } coverPath) return;
        _targets.Remove(coverPath);
        if (Paths.Same(coverPath, path)) return;

        for (string? dir = coverPath; dir is not null;)
        {
            string? next = null;
            foreach (var child in list(dir))
            {
                if (Paths.Same(child.Path, path)) continue;
                if (Paths.IsUnder(path, child.Path)) next = child.Path;
                else _targets[child.Path] = new Target(child.Path, false, child.Folder, child.Size);
            }
            dir = next;
        }
    }

    // If checking path completes its parent folder, the check moves to the parent, level by level, up to the base.
    // Unchecking and rechecking an item then leaves the recommendation as it was and the script with one line per folder.
    // list is what the breakdown showed, which omits some entries (cloud-only files, new folders, links):
    // merge only if disk shows nothing unchecked either. Otherwise deleting the folder would take the unseen entries.
    void Merge(string path, Func<string, IReadOnlyList<Entry>> list, Func<string, IReadOnlyList<string>?> disk)
    {
        for (string? dir = System.IO.Path.GetDirectoryName(path); dir is not null; dir = System.IO.Path.GetDirectoryName(dir))
        {
            // No merging outside the bases: in manual selection it would climb to folders nobody checked.
            if (Nodes.FirstOrDefault(n => Paths.IsSameOrUnder(dir, n.Path)) is not { } baseNode) return;
            var entries = list(dir);
            if (entries.Count == 0 || !entries.All(e => _targets.ContainsKey(e.Path))) return;
            if (disk(dir) is not { } onDisk || !onDisk.All(_targets.ContainsKey)) return;
            RemoveUnder(dir);
            bool isBase = Paths.Same(dir, baseNode.Path);
            _targets[dir] = new Target(dir, isBase && KeepFolders, baseNode.Find(dir), 0);
            if (isBase) return;
        }
    }

    void RemoveUnder(string path)
    {
        foreach (var key in _targets.Keys.Where(k => Paths.IsUnder(k, path)).ToList()) _targets.Remove(key);
    }

    public void Changed()
    {
        _partial.Clear();
        foreach (var key in _targets.Keys)
        {
            // Walk up until an ancestor is already registered: everything above it is registered too.
            string? p = System.IO.Path.GetDirectoryName(key);
            while (p is not null && _partial.Add(p)) p = System.IO.Path.GetDirectoryName(p);
        }
        foreach (var name in new[] { nameof(IsChecked), nameof(SizeText), nameof(DisplayPath), nameof(Size) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class Cleanup
{
    // Safe: caches the tool downloads again by itself. Review: data that is costly to recover.
    // A "*" segment matches any subfolder (browser profiles, IDE versions).
    // KeepFolders false: the folder itself is deleted along with its contents.
    sealed record Rule(string Title, Risk Risk, string[] Paths, bool KeepFolders = true);

    static readonly Rule[] Rules =
    [
        // Default location of the user's TEMP; %TEMP% can be in 8.3 short form and would not match the tree.
        new(Strings.Rules.TempFiles, Risk.Safe, [@"%LOCALAPPDATA%\Temp"]),
        new(Strings.Rules.NpmCache, Risk.Safe, [@"%LOCALAPPDATA%\npm-cache"]),
        new(Strings.Rules.PipUvCaches, Risk.Safe, [@"%LOCALAPPDATA%\pip\Cache", @"%LOCALAPPDATA%\uv\cache"]),
        new(Strings.Rules.NuGetHttpCache, Risk.Safe, [@"%LOCALAPPDATA%\NuGet\v3-cache", @"%LOCALAPPDATA%\NuGet\plugins-cache"]),
        new(Strings.Rules.GradleCaches, Risk.Safe, [@"%USERPROFILE%\.gradle\caches", @"%USERPROFILE%\.gradle\wrapper\dists"]),
        new(Strings.Rules.YarnCaches, Risk.Safe, [@"%LOCALAPPDATA%\Yarn\Cache", @"%LOCALAPPDATA%\Yarn\Berry\cache"]),
        new(Strings.Rules.GoCaches, Risk.Safe, [@"%LOCALAPPDATA%\go-build", @"%USERPROFILE%\go\pkg\mod"]),
        new(Strings.Rules.TestBrowsers, Risk.Safe, [@"%USERPROFILE%\.cache\puppeteer", @"%LOCALAPPDATA%\ms-playwright"]),
        new(Strings.Rules.CrashDumps, Risk.Safe, [@"%LOCALAPPDATA%\CrashDumps"]),
        new(Strings.Rules.BrowserCaches, Risk.Safe,
        [
            @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Cache", @"%LOCALAPPDATA%\Google\Chrome\User Data\*\Code Cache",
            @"%LOCALAPPDATA%\Google\Chrome\User Data\*\GPUCache", @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Cache",
            @"%LOCALAPPDATA%\Microsoft\Edge\User Data\*\Code Cache", @"%LOCALAPPDATA%\BraveSoftware\Brave-Browser\User Data\*\Cache",
            @"%LOCALAPPDATA%\Mozilla\Firefox\Profiles\*\cache2",
        ]),
        new(Strings.Rules.AppCaches, Risk.Safe,
        [
            @"%APPDATA%\discord\Cache", @"%APPDATA%\discord\Code Cache", @"%APPDATA%\discord\GPUCache",
            @"%APPDATA%\Slack\Cache", @"%APPDATA%\Slack\Code Cache", @"%APPDATA%\Microsoft\Teams\Cache",
            @"%APPDATA%\Code\Cache", @"%APPDATA%\Code\CachedData", @"%APPDATA%\Code\CachedExtensionVSIXs", @"%APPDATA%\Code\Code Cache",
            @"%APPDATA%\Cursor\Cache", @"%APPDATA%\Cursor\CachedData", @"%APPDATA%\Cursor\CachedExtensionVSIXs", @"%APPDATA%\Cursor\Code Cache",
        ]),
        new(Strings.Rules.JetBrainsCaches, Risk.Safe,
            [@"%LOCALAPPDATA%\JetBrains\*\caches", @"%LOCALAPPDATA%\JetBrains\*\index", @"%LOCALAPPDATA%\JetBrains\*\log"]),
        new(Strings.Rules.ShaderCaches, Risk.Safe,
        [
            @"%LOCALAPPDATA%\D3DSCache", @"%LOCALAPPDATA%\NVIDIA\DXCache", @"%LOCALAPPDATA%\NVIDIA\GLCache", @"%LOCALAPPDATA%\AMD\DxCache",
            @"%LOCALAPPDATA%\AMD\DxcCache", @"%LOCALAPPDATA%\AMD\VkCache", @"%LOCALAPPDATA%\Intel\ShaderCache",
        ]),
        // Explorer holds the databases in use open: part of them is not deleted until sign-out.
        new(Strings.Rules.ThumbnailCache, Risk.Safe, [@"%LOCALAPPDATA%\Microsoft\Windows\Explorer"]),
        // Only found when scanning the whole drive; without administrator rights the delete fails.
        new(Strings.Rules.WindowsUpdateDownloads, Risk.Safe, [@"%SystemRoot%\SoftwareDistribution\Download"]),
        new(Strings.Rules.WindowsTemp, Risk.Safe, [@"%SystemRoot%\Temp"]),
        // The store files are hard-linked into node_modules folders: deleting it frees less than shown.
        new(Strings.Rules.PnpmStore, Risk.Review, [@"%LOCALAPPDATA%\pnpm\store", @"%USERPROFILE%\.pnpm-store"]),
        new(Strings.Rules.NuGetPackages, Risk.Review, [@"%USERPROFILE%\.nuget\packages"]),
        // May hold SNAPSHOT artifacts installed with mvn install that exist in no remote repository.
        new(Strings.Rules.MavenRepository, Risk.Review, [@"%USERPROFILE%\.m2\repository"]),
        new(Strings.Rules.RustToolchains, Risk.Review, [@"%USERPROFILE%\.rustup\toolchains"]),
        new(Strings.Rules.AndroidSdk, Risk.Review, [@"%LOCALAPPDATA%\Android\Sdk", @"%USERPROFILE%\.android\avd"]),
        new(Strings.Rules.OllamaModels, Risk.Review, [@"%USERPROFILE%\.ollama\models"]),
        new(Strings.Rules.HuggingFaceModels, Risk.Review, [@"%USERPROFILE%\.cache\huggingface\hub"]),
        // They regenerate, but recompiling the shaders takes hours.
        new(Strings.Rules.GameEngineCaches, Risk.Review,
            [@"%LOCALAPPDATA%\UnrealEngine\Common\DerivedDataCache", @"%LOCALAPPDATA%\UnrealEngine\Common\Zen", @"%LOCALAPPDATA%\Unity\cache"]),
        new(Strings.Rules.CargoRegistry, Risk.Review, [@"%USERPROFILE%\.cargo\registry"]),
        new(Strings.Rules.VisualStudioInstallerCache, Risk.Review,
            [@"%ProgramData%\Microsoft\VisualStudio\Packages"]),
        // Owned by TrustedInstaller: Remove-Item fails even as administrator.
        new(Strings.Rules.WindowsOld, Risk.Review, [@"%SystemDrive%\Windows.old"], KeepFolders: false),
        new(Strings.Rules.Downloads, Risk.Review, [@"%USERPROFILE%\Downloads"]),
    ];

    // root can be the home folder or the whole drive (if Program Files or ProgramData were also scanned).
    // leftoverParents: folders whose subfolders are checked as possible leftovers of uninstalled programs.
    public static List<Recommendation> Recommend(DirNode root, string home, DateTime nowUtc,
        IReadOnlyList<InstalledApp> installed, IReadOnlyList<string> leftoverParents)
    {
        var list = new List<Recommendation>();
        // Folders already recommended. One inside another, or containing it, would count the same space twice
        // and appear twice in the script (npm-cache as a rule and as a leftover, node_modules inside Downloads).
        var claimed = new List<DirNode>();
        bool Free(DirNode n) => !claimed.Exists(c => Paths.IsSameOrUnder(n.Path, c.Path) || Paths.IsSameOrUnder(c.Path, n.Path));

        foreach (var rule in Rules)
        {
            var nodes = rule.Paths
                .SelectMany(p => Resolve(root, Environment.ExpandEnvironmentVariables(p)))
                .Where(n => n.Size > 0)
                .ToList();
            if (nodes.Count == 0) continue;
            claimed.AddRange(nodes);
            // Recommendations start unchecked: the user decides what to delete.
            list.Add(new Recommendation(rule.Title, rule.Risk, string.Join(", ", nodes.Select(n => Tilde(home, n.Path))), nodes) { KeepFolders = rule.KeepFolders });
        }

        // Home folder only: under Program Files, node_modules belong to installed programs.
        var homeNode = root.Find(home);
        var cutoff = nowUtc.AddDays(-30);
        List<DirNode> Idle(string name, Func<DirNode, bool> isBuild) => [.. (homeNode?.Named(name) ?? [])
            .Where(n => n.Size > 0 && isBuild(n) && IsProject(home, n) && ProjectWriteUtc(n) < cutoff && Free(n))];

        var idle = Idle("node_modules", _ => true);
        if (idle.Count > 0)
        {
            claimed.AddRange(idle);
            list.Add(new Recommendation(Strings.Rules.IdleNodeModules, Risk.Review, IdleFolders(idle.Count), idle));
        }

        // Build outputs that a build regenerates. Recognized by folder structure, without reading files:
        // Cargo target (debug or release with deps or incremental), Maven target (classes) and .NET obj (Debug or Release).
        var builds = Idle("target", n => n.ChildrenOrEmpty.Any(c => NameIs(c.Name, "debug", "release") && c.ChildrenOrEmpty.Any(g => NameIs(g.Name, "deps", "incremental")))
                || n.ChildrenOrEmpty.Any(c => NameIs(c.Name, "classes")))
            .Concat(Idle("obj", n => n.ChildrenOrEmpty.Any(c => NameIs(c.Name, "Debug", "Release"))))
            .ToList();
        if (builds.Count > 0)
        {
            claimed.AddRange(builds);
            list.Add(new Recommendation(Strings.Rules.IdleBuilds, Risk.Review, IdleFolders(builds.Count), builds));
        }

        // The folder itself is deleted: it is what remains of a program that is no longer installed.
        foreach (var folder in Leftovers.Find(root, leftoverParents, installed, nowUtc).Where(Free))
            list.Add(new Recommendation(folder.Name, Risk.Leftover,
                string.Format(CultureInfo.CurrentCulture, Strings.Rules.LeftoverPath, Tilde(home, folder.Path), folder.NewestWriteUtc.ToLocalTime()), [folder]) { KeepFolders = false });

        return [.. list.OrderBy(r => r.Risk).ThenByDescending(r => r.Size)];
    }

    static string IdleFolders(int n) => n == 1 ? Strings.Rules.IdleFoldersOne : string.Format(CultureInfo.CurrentCulture, Strings.Rules.IdleFoldersMany, n);

    static bool NameIs(string name, params string[] names) => names.Contains(name, StringComparer.OrdinalIgnoreCase);

    // Tree folders that match the path; each "*" segment expands to the subfolders at that level.
    static IEnumerable<DirNode> Resolve(DirNode root, string pattern)
    {
        int star = pattern.IndexOf(@"\*", StringComparison.Ordinal);
        if (star < 0) return root.Find(pattern) is { } node ? [node] : [];
        string rest = pattern[(star + 2)..];
        return (root.Find(pattern[..star])?.ChildrenOrEmpty ?? []).Where(c => !c.IsFiles).SelectMany(c => Resolve(root, c.Path + rest));
    }

    // Last change in the project that holds the folder: its siblings (src, Cargo.toml...) and the folder itself.
    // ponytail: editing a file without creating or renaming anything does not change its folder's date; reading git log would be more reliable.
    static DateTime ProjectWriteUtc(DirNode folder) => folder.Parent is not { } project
        ? folder.LastWriteUtc
        : project.ChildrenOrEmpty.Where(c => c != folder).Select(c => c.NewestWriteUtc).Append(project.LastWriteUtc).Append(folder.LastWriteUtc).Max();

    // Excludes AppData (global npm modules) and dot folders (.vscode, .cursor...): their node_modules belong to installed tools.
    static bool IsProject(string home, DirNode node) =>
        !Tilde(home, node.Path).Split('\\').Any(s => s.StartsWith('.') || s.Equals("AppData", StringComparison.OrdinalIgnoreCase));

    // Paths under the home folder start with "~"; the rest are full paths.
    static string Tilde(string home, string path) =>
        Paths.IsSameOrUnder(path, home) ? "~" + path[home.TrimEnd('\\').Length..] : path;

    public static string BuildScript(IEnumerable<Recommendation> items)
    {
        var sb = new StringBuilder("# ").Append(Strings.Rules.ScriptHeader).Append('\n').Append(RemoveTree);
        foreach (var r in items.Where(r => r.TargetCount > 0))
        {
            sb.Append("\n# ").Append(r.Title).Append(" (").Append(DirNode.Format(r.SelectedSize)).Append(")\n");
            foreach (var t in r.Targets.OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase))
            {
                // Same check as DeleteAsync after expanding: never delete a protected folder or empty one that contains one
                // (emptying the home folder would take AppData, Documents and Desktop).
                if (t.ContentsOnly ? Paths.HoldsProtected(t.Path) : Paths.IsProtected(t.Path)) continue;
                string path = Quote(t.Path);
                // No node means a single file: there is nothing to walk.
                sb.Append(t switch
                {
                    { ContentsOnly: true } => $"Remove-Tree '{path}' -ContentsOnly\n",
                    { Node: null } => $"Remove-Item -LiteralPath '{path}' -Force\n",
                    _ => $"Remove-Tree '{path}'\n",
                });
            }
        }
        return sb.ToString();
    }

    // Windows PowerShell 5.1 Remove-Item -Recurse follows junctions and symbolic links and deletes their target
    // (e.g. another package's code linked into a workspace node_modules). The scanner skips links, so it does not see them.
    // This function walks one level at a time and removes links without following them; it works the same in PowerShell 7.
    // The folder is deleted with Directory.Delete without recursion: if an entry could not be deleted (file in use) or
    // listed, it fails with an error and leaves the folder. Remove-Item without -Recurse would prompt, and accepting deletes recursively.
    const string RemoveTree = """

        function Remove-Tree([string]$Path, [switch]$ContentsOnly) {
            foreach ($item in Get-ChildItem -LiteralPath $Path -Force) {
                if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { $item.Delete() }
                elseif ($item.PSIsContainer) { Remove-Tree $item.FullName }
                else { Remove-Item -Force -LiteralPath $item.FullName }
            }
            if (-not $ContentsOnly) { [IO.Directory]::Delete($Path, $false) }
        }

        """;

    // Contents of a PowerShell single-quoted string. PowerShell also ends the string at the typographic
    // quotes ‘ ’ ‚ ‛ (U+2018 to U+201B); a file name with one of them would inject commands.
    // Each one is doubled, like the apostrophe, as CodeGeneration.EscapeSingleQuotedStringContent does.
    static string Quote(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            if (c is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(c);
            sb.Append(c);
        }
        return sb.ToString();
    }

    // Concrete paths to delete: the contents of folders that get emptied, and the other targets as they are.
    public static List<string> ExpandTargets(IEnumerable<Recommendation> items) => ExpandTargets(items.SelectMany(r => r.Targets));

    // Reads the disk: a full Temp or cache has hundreds of thousands of entries. Can be called off the UI thread
    // with a copy of the targets.
    public static List<string> ExpandTargets(IEnumerable<Target> targets)
    {
        var paths = new List<string>();
        foreach (var t in targets)
        {
            if (!t.ContentsOnly) paths.Add(t.Path);
            else
            {
                try { paths.AddRange(Directory.EnumerateFileSystemEntries(t.Path)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Folder no longer exists or access is denied: nothing to delete in it.
                }
            }
        }
        return [.. paths.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    // All entries of a folder on disk, including hidden, system and cloud-only ones.
    // null if it could not be read in full. A folder that no longer exists has nothing to lose: empty list.
    public static IReadOnlyList<string>? DiskEntries(string dir)
    {
        try
        {
            return [.. Directory.EnumerateFileSystemEntries(dir, "*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false })];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
