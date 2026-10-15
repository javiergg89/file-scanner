using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileScanner.Core;

public sealed class DirNode
{
    public static readonly string FilesName = Strings.Core.FilesNode;

    // Name and path change when the folder is renamed on disk (DiskScanner.Rename); its contents are not reread.
    public required string Name { get; set; }
    public required string Path { get; set; }
    // Reassigned when a partial rescan is applied (new children are built on a temporary node).
    public DirNode? Parent { get; set; }
    // Aggregate node for the loose files of the parent folder.
    public bool IsFiles { get; init; }
    // On "Archivos" nodes, the write time of the newest file. Reread when the folder is rescanned.
    public DateTime LastWriteUtc { get; set; }
    // Newest change in the folder, its files or any subfolder.
    public DateTime NewestWriteUtc { get; set; }
    // Intermediate folder that holds only what was scanned (e.g. C:\Users with only the user profile):
    // its size is not the real one, and it is never rescanned or deleted whole.
    public bool IsPartial { get; set; }
    public long Size { get; set; }
    public long FileCount { get; set; }
    // The folder is the root of a git repository.
    public bool HasRepo { get; set; }
    // The folder or a subfolder is a repository.
    public bool ContainsRepo { get; set; }

    List<DirNode>? _children;
    // Created with the first child: "Archivos" nodes and leaf folders, most of the tree, never need it.
    public List<DirNode> Children => _children ??= [];
    // Iterates the children without creating the list on nodes that have none.
    public IReadOnlyList<DirNode> ChildrenOrEmpty => (IReadOnlyList<DirNode>?)_children ?? [];

    // DiskScanner hands over its own list without a copy; an empty list means no children.
    internal void SetChildList(List<DirNode> list) => _children = list.Count > 0 ? list : null;

    public override string ToString() => Name;

    // Deepest node in the tree that contains the path (the path's own node if it is in the tree).
    public DirNode? FindNearest(string path)
    {
        // "Este equipo" (empty path) groups drives: search in the drive of the path.
        if (Path.Length == 0) return ChildrenOrEmpty.Select(c => c.FindNearest(path)).FirstOrDefault(n => n is not null);
        if (!Paths.IsSameOrUnder(path, Path)) return null;
        var node = this;
        foreach (var segment in path[Path.TrimEnd('\\').Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = node.ChildrenOrEmpty.FirstOrDefault(c => !c.IsFiles && c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (next is null) break;
            node = next;
        }
        return node;
    }

    public DirNode? Find(string path) => FindNearest(path) is { } node && Paths.Same(node.Path, path) ? node : null;

    // A node removed from the tree still has its Parent; it is attached only if every parent still lists it as a child.
    public bool IsAttachedTo(DirNode root)
    {
        var n = this;
        for (; n.Parent is not null; n = n.Parent)
            if (!n.Parent.ChildrenOrEmpty.Contains(n)) return false;
        return n == root;
    }

    public bool IsWithin(DirNode ancestor)
    {
        for (var n = this; n is not null; n = n.Parent)
            if (n == ancestor) return true;
        return false;
    }

    // Folders with that name, without descending into them (e.g. nested node_modules).
    public IEnumerable<DirNode> Named(string name)
    {
        var stack = new Stack<DirNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!node.IsFiles && node.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                yield return node;
                continue;
            }
            foreach (var child in node.ChildrenOrEmpty) stack.Push(child);
        }
    }

    public static string Format(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };
}

public static class Paths
{
    public static bool Same(string a, string b) => a.TrimEnd('\\').Equals(b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static bool IsUnder(string path, string parent)
    {
        parent = parent.TrimEnd('\\');
        return path.Length > parent.Length + 1 && path[parent.Length] == '\\' && path.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSameOrUnder(string path, string parent) => Same(path, parent) || IsUnder(path, parent);

    // Folders the app never deletes whole: deleting one, or a folder that contains one, breaks Windows or the user account.
    // Their contents can be deleted (AppData\Local\Temp, a program inside Program Files).
    static readonly string[] Protected = [.. new[]
    {
        Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData,
        Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments, Environment.SpecialFolder.MyPictures,
        Environment.SpecialFolder.MyVideos, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.Windows,
        Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.CommonApplicationData,
    }.Select(Environment.GetFolderPath).Where(p => p.Length > 0).Concat(
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
    ])];

    // Drive root, one of the folders above, or a folder that contains one.
    public static bool IsProtected(string path) =>
        path.TrimEnd('\\').Length <= 2 || Protected.Any(p => IsSameOrUnder(p, path));

    // Emptying it would break something: it is a drive root or contains a protected folder. Downloads can be emptied; the user profile cannot.
    public static bool HoldsProtected(string path) =>
        path.TrimEnd('\\').Length <= 2 || Protected.Any(p => IsUnder(p, path));
}

// A folder item as shown in the breakdown: a subfolder from the tree or a file read from disk.
public sealed record Entry(string Name, string Path, long Size, DirNode? Folder)
{
    public bool IsDirectory => Folder is not null;
}

// Folder children reread from disk, to apply on the UI thread. LastWriteUtc is null if the folder no longer exists.
public sealed record Rescanned(List<DirNode> Children, bool HasRepo, DateTime? LastWriteUtc);

public static partial class DiskScanner
{
    [LibraryImport("kernel32.dll", EntryPoint = "GetCompressedFileSizeW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetCompressedFileSize(string path, out uint high);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, FileShare share, nint security, FileMode mode, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out LinkInfo info);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, in SeekPenaltyQuery input, int inputSize, out SeekPenalty output, int outputSize, out int returned, nint overlapped);

    // BY_HANDLE_FILE_INFORMATION: only the fields that are read, at their offsets.
    [StructLayout(LayoutKind.Explicit, Size = 52)]
    struct LinkInfo
    {
        [FieldOffset(28)] public uint Volume;
        [FieldOffset(40)] public uint Links;
        [FieldOffset(44)] public uint IndexHigh;
        [FieldOffset(48)] public uint IndexLow;
    }

    // STORAGE_PROPERTY_QUERY with PropertyId = StorageDeviceSeekPenaltyProperty and QueryType = PropertyStandardQuery.
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    struct SeekPenaltyQuery
    {
        [FieldOffset(0)] public int PropertyId;
    }

    // DEVICE_SEEK_PENALTY_DESCRIPTOR.
    [StructLayout(LayoutKind.Explicit, Size = 12)]
    struct SeekPenalty
    {
        [FieldOffset(8)] public byte Incurs;
    }

    const uint FileReadAttributes = 0x80;
    const uint OpenReparsePoint = 0x00200000;
    const uint QueryStorageProperty = 0x2D1400;
    const int StorageDeviceSeekPenaltyProperty = 7;

    // AttributesToSkip replaces the default (Hidden | System), so hidden folders such as AppData are counted.
    // Reparse points are filtered in Include: OneDrive files and WOF-compressed files have them too.
    // Cloud-only entries are skipped: listing or opening them makes OneDrive download them.
    static readonly EnumerationOptions Options = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Offline
            | (FileAttributes)0x00040000 /* RECALL_ON_OPEN */ | (FileAttributes)0x00400000 /* RECALL_ON_DATA_ACCESS */,
        RecurseSubdirectories = false,
    };

    // A reparse point does not always take up its Length: a cloud-only OneDrive placeholder takes up nothing.
    const FileAttributes Allocated = FileAttributes.SparseFile | FileAttributes.Compressed | FileAttributes.ReparsePoint | FileAttributes.Offline;

    const long MinLinkBytes = 64 * 1024;

    static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    // An entry read without creating a FileInfo: the name is only materialized for folders.
    readonly record struct Raw(string? DirName, DateTime WriteUtc, long Bytes, bool IsGit);

    // Folders that are links (junctions, symlinks) are skipped: they would cause loops and double counting.
    // Other reparse-point folders are walked, such as OneDrive folders already downloaded.
    static bool Include(ref FileSystemEntry e) =>
        !e.IsDirectory || (e.Attributes & FileAttributes.ReparsePoint) == 0 || !IsLink(e.ToFullPath());

    static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Without reading the target there is no way to know where it leads: treat it as a link and skip it.
            return true;
        }
    }

    // Without the \\?\ prefix, native calls fail on paths of 260 characters or more.
    static string LongPath(string path)
    {
        if (path.Length < 260) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    // Windows (WinSxS and System32) and pnpm (its store and node_modules\.pnpm) fill their folders with hard links.
    // Files are opened to detect repeated links only there: doing it on the whole disk would slow the scan.
    // ponytail: hard links under 64 KB (MinLinkBytes) are still counted once per link.
    static bool MayHaveLinks(string dir) =>
        Paths.IsSameOrUnder(dir, WindowsDir)
        || dir.Contains(@"\.pnpm", StringComparison.OrdinalIgnoreCase)
        || dir.Contains(@"\pnpm\store", StringComparison.OrdinalIgnoreCase);

    // True if the file has several hard links and another one was already counted in this walk.
    static bool IsRepeatedLink(string path, ConcurrentDictionary<(uint, ulong), byte> seen)
    {
        using var file = CreateFile(LongPath(path), FileReadAttributes, FileShare.ReadWrite | FileShare.Delete, 0, FileMode.Open, OpenReparsePoint, 0);
        return !file.IsInvalid && GetFileInformationByHandle(file, out var info) && info.Links > 1
            && !seen.TryAdd((info.Volume, ((ulong)info.IndexHigh << 32) | info.IndexLow), 0);
    }

    // Sparse files (e.g. Google caches) declare hundreds of GB they do not occupy, so their size on disk comes from
    // GetCompressedFileSizeW. It is queried only for the Allocated attributes; the rest use Length.
    // links is null outside folders with hard links (MayHaveLinks) and in the breakdown, which shows each file at full size.
    static long FileSize(ref FileSystemEntry e, ConcurrentDictionary<(uint, ulong), byte>? links)
    {
        // A file symlink has no data: its target is counted where it lives.
        if ((e.Attributes & FileAttributes.ReparsePoint) != 0 && e.Length == 0) return 0;
        // Opening small files too made the scan of a user profile with pnpm 60% slower,
        // and files of 64 KB or more hold most of the repeated space (about 0.8 of every 1 GB measured).
        if (e.Length < MinLinkBytes) links = null;
        if ((e.Attributes & Allocated) == 0 && links is null) return e.Length;
        string path = e.ToFullPath();
        if (links is not null && IsRepeatedLink(path, links)) return 0;
        if ((e.Attributes & Allocated) == 0) return e.Length;
        uint low = GetCompressedFileSize(LongPath(path), out uint high);
        if (low == uint.MaxValue && Marshal.GetLastPInvokeError() != 0) return e.Length;
        return ((long)high << 32) | low;
    }

    static readonly ConcurrentDictionary<string, int> ReadersByDrive = new(StringComparer.OrdinalIgnoreCase);

    // Concurrent readers per drive. On a hard disk, more than two turn reading into head seeks.
    static int Readers(string path) =>
        ReadersByDrive.GetOrAdd(System.IO.Path.GetPathRoot(path) ?? "", root => HasSeekPenalty(root) ? 2 : Environment.ProcessorCount * 2);

    // Duplicates also uses it to read one file at a time on a hard disk.
    internal static bool HasSeekPenalty(string root)
    {
        // Lettered drives only. On a network drive, or if Windows does not know, assume no seek penalty.
        if (root is not [_, ':', ..]) return false;
        using var volume = CreateFile(@"\\.\" + root[..2], 0, FileShare.ReadWrite, 0, FileMode.Open, 0, 0);
        if (volume.IsInvalid) return false;
        var query = new SeekPenaltyQuery { PropertyId = StorageDeviceSeekPenaltyProperty };
        return DeviceIoControl(volume, QueryStorageProperty, in query, Marshal.SizeOf<SeekPenaltyQuery>(), out var penalty, Marshal.SizeOf<SeekPenalty>(), out _, 0)
            && penalty.Incurs != 0;
    }

    public static DirNode Scan(string path, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var root = new DirNode { Name = System.IO.Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } name ? name : path, Path = path };
        Fill([root], progress, 0, new(), token);
        return root;
    }

    public static readonly string ComputerName = Strings.Core.ComputerNode;

    // Folders that would be scanned: the requested ones that exist, minus those inside another
    // (with "C: completa", the user profile and Program Files are redundant). Drives are written "C:\".
    public static List<string> Targets(IEnumerable<string> roots)
    {
        var all = roots
            .Select(p => p.Length > 3 ? p.TrimEnd('\\') : p)
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return all.Where(t => !all.Exists(other => Paths.IsUnder(t, other))).ToList();
    }

    // Scans the requested folders. With one, it is the root. With several on one drive, the root is the drive
    // and intermediate folders (C:\Users) hold only what was scanned. With several drives, the root is "Este equipo".
    public static DirNode ScanRoots(IEnumerable<string> roots, IProgress<string>? progress = null, CancellationToken token = default)
    {
        var targets = Targets(roots);
        if (targets.Count == 1) return Scan(targets[0], progress, token);

        var drives = new List<DirNode>();
        var leaves = new List<List<DirNode>>();
        foreach (var group in targets.GroupBy(t => System.IO.Path.GetPathRoot(t)!, StringComparer.OrdinalIgnoreCase))
        {
            var drive = new DirNode { Name = group.Key.TrimEnd('\\'), Path = group.Key, IsPartial = true };
            leaves.Add([.. group.Select(target => Chain(drive, target))]);
            drives.Add(drive);
        }
        // Each drive has its own readers, so a slow hard disk does not hold back the SSD.
        // GetResult rethrows the first exception unwrapped, so cancellation surfaces as OperationCanceledException.
        Task.WhenAll(leaves.Select(group => Task.Run(() => Fill(group, progress, 0, new(), token), CancellationToken.None))).GetAwaiter().GetResult();
        foreach (var drive in drives) Total(drive);
        if (drives.Count == 1) return drives[0];

        var computer = new DirNode { Name = ComputerName, Path = "", IsPartial = true };
        foreach (var drive in drives) drive.Parent = computer;
        SetChildren(computer, drives);
        return computer;
    }

    // Creates (or reuses) the intermediate nodes from the drive down to target and returns target's node, still unscanned.
    static DirNode Chain(DirNode drive, string target)
    {
        var node = drive;
        foreach (var segment in target[drive.Path.TrimEnd('\\').Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = node.ChildrenOrEmpty.FirstOrDefault(c => c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (next is null)
            {
                next = new DirNode { Name = segment, Path = System.IO.Path.Join(node.Path, segment), Parent = node, IsPartial = true };
                node.Children.Add(next);
            }
            node = next;
        }
        node.IsPartial = false;
        return node;
    }

    // Sums intermediate folders bottom-up; scanned folders already have their total.
    static void Total(DirNode node)
    {
        if (!node.IsPartial) return;
        foreach (var child in node.ChildrenOrEmpty) Total(child);
        SetChildren(node, node.Children);
    }

    // Reads the folder's direct entries: returns the bytes, count and newest write time of its files,
    // and adds the subfolders to dirs.
    static (long Bytes, long Count, DateTime Newest) ReadEntries(DirNode node, List<DirNode> dirs, ConcurrentDictionary<(uint, ulong), byte> seen)
    {
        long bytes = 0, count = 0;
        DateTime newest = default;
        var links = MayHaveLinks(node.Path) ? seen : null;
        try
        {
            var entries = new FileSystemEnumerable<Raw>(node.Path, (ref FileSystemEntry e) => ToRaw(ref e, links), Options)
            {
                ShouldIncludePredicate = Include,
            };
            foreach (var raw in entries)
            {
                if (raw.IsGit) node.HasRepo = true;
                if (raw.DirName is null)
                {
                    bytes += raw.Bytes;
                    count++;
                    if (raw.WriteUtc > newest) newest = raw.WriteUtc;
                }
                else dirs.Add(new DirNode { Name = raw.DirName, Path = System.IO.Path.Join(node.Path, raw.DirName), Parent = node, LastWriteUtc = raw.WriteUtc });
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Folder without permission or deleted during the scan: count what was read up to that point.
        }
        return (bytes, count, newest);
    }

    static Raw ToRaw(ref FileSystemEntry e, ConcurrentDictionary<(uint, ulong), byte>? links) => e.IsDirectory
        ? new(e.FileName.ToString(), e.LastWriteTimeUtc.UtcDateTime, 0, e.FileName is ".git")
        : new(null, e.LastWriteTimeUtc.UtcDateTime, FileSize(ref e, links), false);

    // Node with the folder's loose files. Created even at 0 bytes so their count and date are not lost.
    static DirNode FilesNode(DirNode parent, long bytes, long count, DateTime newest) => new()
    {
        Name = DirNode.FilesName, Path = parent.Path, Parent = parent, IsFiles = true,
        Size = bytes, FileCount = count, LastWriteUtc = newest, NewestWriteUtc = newest,
    };

    // Folder in progress: counts the subfolders still to finish, plus one for its own read.
    sealed class Job(DirNode node, Job? parent, int depth)
    {
        public readonly DirNode Node = node;
        public readonly Job? Parent = parent;
        public readonly int Depth = depth;
        public readonly List<DirNode> Dirs = [];
        public long Bytes, Count;
        public DateTime Newest;
        public int Pending;
    }

    // One walk: a few threads pop folders from the stack and move on to another without waiting for their subfolders.
    // The last subfolder to finish closes the parent folder. A stack makes the walk depth-first, which bounds memory.
    sealed class Walk(IProgress<string>? progress, int roots, ConcurrentDictionary<(uint, ulong), byte> seen, CancellationToken token)
    {
        public readonly BlockingCollection<Job> Pending = new(new ConcurrentStack<Job>());
        int _roots = roots;

        public void Work()
        {
            try
            {
                foreach (var job in Pending.GetConsumingEnumerable())
                {
                    token.ThrowIfCancellationRequested();
                    Read(job);
                }
            }
            catch
            {
                // Without this, the other readers would wait forever for a folder that will never finish.
                Pending.CompleteAdding();
                throw;
            }
        }

        void Read(Job job)
        {
            (job.Bytes, job.Count, job.Newest) = ReadEntries(job.Node, job.Dirs, seen);
            job.Pending = job.Dirs.Count + 1;
            foreach (var dir in job.Dirs) Pending.Add(new Job(dir, job, job.Depth + 1));
            if (Interlocked.Decrement(ref job.Pending) == 0) Complete(job);
        }

        void Complete(Job job)
        {
            // Walks up while the closed folder is the last pending one of its parent.
            Job? j = job;
            while (j is not null)
            {
                if (j.Count > 0) j.Dirs.Add(FilesNode(j.Node, j.Bytes, j.Count, j.Newest));
                SetChildren(j.Node, j.Dirs);
                if (j.Depth == 1) progress?.Report(j.Node.Path);
                if (j.Parent is null && Interlocked.Decrement(ref _roots) == 0) Pending.CompleteAdding();
                j = j.Parent is { } parent && Interlocked.Decrement(ref parent.Pending) == 0 ? parent : null;
            }
        }
    }

    // Scans the subtrees of roots (all on one drive) with several readers at once.
    static void Fill(IReadOnlyList<DirNode> roots, IProgress<string>? progress, int depth, ConcurrentDictionary<(uint, ulong), byte> seen, CancellationToken token)
    {
        if (roots.Count == 0) return;
        var walk = new Walk(progress, roots.Count, seen, token);
        foreach (var root in roots) walk.Pending.Add(new Job(root, null, depth));
        var readers = Enumerable.Range(0, Readers(roots[0].Path))
            .Select(_ => Task.Factory.StartNew(walk.Work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            .ToArray();
        // As in ScanRoots: cancellation surfaces as OperationCanceledException, unwrapped from AggregateException.
        Task.WhenAll(readers).GetAwaiter().GetResult();
    }

    // Sorts largest first, takes the list without a copy, and recomputes the node's totals.
    static void SetChildren(DirNode node, List<DirNode> children)
    {
        children.Sort((a, b) => b.Size.CompareTo(a.Size));
        node.SetChildList(children);
        Summarize(node);
    }

    // Size, file count, repos and newest write time of the node from its children, in one pass.
    // With sizes false, size and file count are left as they are: Remove and Apply adjust them by the difference.
    static void Summarize(DirNode node, bool sizes = true)
    {
        long size = 0, count = 0;
        bool repo = node.HasRepo;
        var newest = node.LastWriteUtc;
        foreach (var child in node.ChildrenOrEmpty)
        {
            size += child.Size;
            count += child.FileCount;
            repo |= child.ContainsRepo;
            if (child.NewestWriteUtc > newest) newest = child.NewestWriteUtc;
        }
        if (sizes)
        {
            node.Size = size;
            node.FileCount = count;
        }
        node.ContainsRepo = repo;
        node.NewestWriteUtc = newest;
    }

    // Subfolders of the node by name, for Rescan. Taken on the UI thread, the only one that changes the tree.
    public static Dictionary<string, DirNode> Known(DirNode node)
    {
        var known = new Dictionary<string, DirNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in node.ChildrenOrEmpty)
            if (!child.IsFiles) known.TryAdd(child.Name, child);
        return known;
    }

    // Shortcut for the UI thread or for a tree nobody else changes (the checks).
    public static Rescanned Rescan(DirNode node, bool deep) => Rescan(node.Path, Known(node), deep);

    // Rereads a folder after a change on disk: reuses subfolders that still exist and scans only new ones.
    // Can run off the UI thread: it reads only the disk and the known snapshot; the tree does not change until Apply.
    public static Rescanned Rescan(string path, IReadOnlyDictionary<string, DirNode> known, bool deep)
    {
        var temp = new DirNode { Name = "", Path = path };
        var fresh = new List<DirNode>();
        var seen = new ConcurrentDictionary<(uint, ulong), byte>();
        var (bytes, count, newest) = ReadEntries(temp, fresh, seen);

        var children = new List<DirNode>(fresh.Count + 1);
        var added = new List<DirNode>();
        foreach (var dir in fresh)
        {
            if (!deep && known.TryGetValue(dir.Name, out var kept)) children.Add(kept);
            else
            {
                children.Add(dir);
                added.Add(dir);
            }
        }
        Fill(added, null, 2, seen, CancellationToken.None);
        if (count > 0) children.Add(FilesNode(temp, bytes, count, newest));
        var info = new DirectoryInfo(path);
        return new(children, temp.HasRepo, info.Exists ? info.LastWriteTimeUtc : null);
    }

    // Removes a just-deleted folder from the tree and recomputes its ancestors without reading the disk.
    public static void Remove(DirNode node)
    {
        if (node.Parent is not { } parent || !parent.Children.Remove(node)) return;
        for (var a = parent; a is not null; a = a.Parent)
        {
            a.Size -= node.Size;
            a.FileCount -= node.FileCount;
            Summarize(a, sizes: false);
        }
    }

    // Replaces the children and recomputes the ancestors (size, newest write time, repos). UI thread only.
    public static void Apply(DirNode node, Rescanned result)
    {
        long oldSize = node.Size, oldCount = node.FileCount;
        foreach (var child in result.Children) child.Parent = node;
        node.HasRepo = result.HasRepo;
        if (result.LastWriteUtc is { } lastWrite) node.LastWriteUtc = lastWrite;
        SetChildren(node, result.Children);
        for (var a = node.Parent; a is not null; a = a.Parent)
        {
            a.Size += node.Size - oldSize;
            a.FileCount += node.FileCount - oldCount;
            Summarize(a, sizes: false);
        }
    }

    // After a folder is renamed on disk, updates its name and the paths of its subtree without rereading it.
    // Rescan of the parent then finds it by the new name and reuses it. UI thread only.
    public static void Rename(DirNode node, string newPath)
    {
        node.Name = System.IO.Path.GetFileName(newPath.TrimEnd('\\'));
        node.Path = newPath;
        var stack = new Stack<DirNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var parent = stack.Pop();
            foreach (var child in parent.ChildrenOrEmpty)
            {
                child.Path = child.IsFiles ? parent.Path : System.IO.Path.Join(parent.Path, child.Name);
                stack.Push(child);
            }
        }
    }

    // Tree subfolders shown in the breakdown. Taken on the UI thread.
    public static List<DirNode> Folders(DirNode dir) => [.. dir.ChildrenOrEmpty.Where(c => !c.IsFiles)];

    public static List<Entry> ListEntries(DirNode dir) => ListEntries(dir.Path, Folders(dir));

    // Known subfolders plus the folder's files read from disk now, largest first.
    // Can run off the UI thread: it reads only folders, not the tree.
    public static List<Entry> ListEntries(string path, IReadOnlyList<DirNode> folders)
    {
        var list = folders.Select(c => new Entry(c.Name, c.Path, c.Size, c)).ToList();
        try
        {
            list.AddRange(new FileSystemEnumerable<Entry>(path, static (ref FileSystemEntry e) =>
                new Entry(e.FileName.ToString(), e.ToFullPath(), FileSize(ref e, null), null), Options)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry e) => !e.IsDirectory,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Folder deleted or without permission: show only the known subfolders.
        }
        list.Sort((a, b) => b.Size.CompareTo(a.Size));
        return list;
    }
}
