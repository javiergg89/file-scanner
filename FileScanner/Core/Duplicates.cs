using System.Buffers;
using System.IO.Enumeration;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace FileScanner.Core;

public sealed record DuplicateFile(string Path, long Size, DateTime LastWriteUtc);

// Copies with the same content. Wasted: space freed by keeping only one.
public sealed record DuplicateGroup(long Size, string Hash, IReadOnlyList<DuplicateFile> Files)
{
    public long Wasted => Size * (Files.Count - 1);
}

// InBytes: Done and Total count bytes; otherwise they count files.
public sealed record DuplicateProgress(string Stage, long Done, long Total, bool InBytes = true);

public sealed record DuplicateResult(List<DuplicateGroup> Groups, int Skipped);

// Phased search, cheapest first, like fdupes or czkawka:
// 1) same size, 2) drop hard links (one file with several names uses no extra space),
// 3) hash of the first and last 64 KB, 4) full SHA-256 only for files still tied.
public static partial class Duplicates
{
    const int EdgeBytes = 64 * 1024;
    const int Parallelism = 4;

    // Cloud-only files: reading them would download them. ReparsePoint is not listed because OneDrive also sets it
    // on downloaded files; junctions and symbolic links are skipped separately by IsLink.
    const FileAttributes Skipped = FileAttributes.Offline
        | (FileAttributes)0x00040000 /* RECALL_ON_OPEN */ | (FileAttributes)0x00400000 /* RECALL_ON_DATA_ACCESS */;

    // Technical folders, where copies are expected (the same package in two projects, caches, build output).
    // Folders whose name starts with a dot (.git, .vscode, .claude, .cargo...) count as technical too.
    static readonly HashSet<string> Technical = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bower_components", "vendor", "packages", "bin", "obj", "dist", "build", "out", "target",
        "venv", "env", "__pycache__", "site-packages", "Pods", "DerivedData", "AppData", "Windows",
        "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
    };

    public static bool IsTechnical(ReadOnlySpan<char> folderName) =>
        folderName.StartsWith(".") || Technical.Contains(folderName.ToString());

    [StructLayout(LayoutKind.Sequential)]
    struct FileInfoByHandle
    {
        public uint Attributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial;
        public uint SizeHigh, SizeLow;
        public uint Links;
        public uint IndexHigh, IndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out FileInfoByHandle info);

    // FILE_ID_INFO: 128-bit file ID. On ReFS the 64-bit index from GetFileInformationByHandle can repeat.
    [StructLayout(LayoutKind.Sequential)]
    struct FileIdInfo
    {
        public ulong VolumeSerial;
        public ulong IdLow, IdHigh;
    }

    const int FileIdInfoClass = 18;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out FileIdInfo info, int size);

    // Skips technical folders (see Technical), Program Files and ProgramData if they were scanned,
    // and the user's exclusions (folders, extensions, and groups marked as not duplicates).
    public static async Task<DuplicateResult> FindAsync(IReadOnlyList<string> roots, long minSize, DuplicateExclusions exclusions,
        IProgress<DuplicateProgress>? progress, CancellationToken cancel)
    {
        // Copy: the user can edit the exclusions while the search runs on other threads.
        var excluded = exclusions.Copy();
        var system = Leftovers.SystemRoots();
        List<string> searched = [.. roots.Where(r => !system.Any(s => Paths.IsSameOrUnder(r, s)) && !excluded.ExcludesFolder(r))];
        var bySize = await Task.Run(() => Collect(searched, minSize, excluded, progress, cancel), cancel).ConfigureAwait(false);
        var sameSize = bySize.Values.Where(g => g.Count > 1).ToList();
        int skipped = 0;

        // Phases 2 and 3: identity (hard links) and edge hash come from the same file read.
        var edges = await HashAllAsync(sameSize.SelectMany(g => g).ToList(), EdgeKey, Strings.Core.DuplicatesComparingEdges, inBytes: false, progress, cancel).ConfigureAwait(false);
        skipped += edges.Count(e => e.Key is null);
        var sameEdges = Regroup(edges);

        // Phase 4: full content, only for files still tied.
        var full = await HashAllAsync(sameEdges.SelectMany(g => g.Files).ToList(), FullKey, Strings.Core.DuplicatesComparingContent, inBytes: true, progress, cancel).ConfigureAwait(false);
        skipped += full.Count(e => e.Key is null);

        var groups = Regroup(full)
            .Where(g => !excluded.Ignores(g.Content))
            .Select(g => new DuplicateGroup(g.Files[0].Size, g.Content, [.. g.Files.OrderBy(f => f.LastWriteUtc)]))
            .OrderByDescending(g => g.Wasted)
            .ToList();
        return new DuplicateResult(groups, skipped);
    }

    // Phase 1: files of at least minSize grouped by size. Each root is walked in parallel, one task per subfolder.
    static Dictionary<long, List<DuplicateFile>> Collect(IReadOnlyList<string> roots, long minSize, DuplicateExclusions excluded,
        IProgress<DuplicateProgress>? progress, CancellationToken cancel)
    {
        var bySize = new Dictionary<long, List<DuplicateFile>>();
        var gate = new Lock();
        long seen = 0;
        var starts = roots
            .SelectMany(r => SafeDirectories(r).Where(d => !IsTechnical(Path.GetFileName(d)) && !excluded.ExcludesFolder(d)).Prepend(r)
                .Select(d => (Dir: d, Recurse: !Paths.Same(d, r))))
            .ToList();
        Parallel.ForEach(starts, new ParallelOptions { CancellationToken = cancel }, start =>
        {
            using var files = new Candidates(start.Dir, start.Recurse, minSize, excluded, cancel);
            while (files.MoveNext())
            {
                var file = files.Current;
                lock (gate)
                {
                    if (!bySize.TryGetValue(file.Size, out var list)) bySize[file.Size] = list = [];
                    list.Add(file);
                }
                if (Interlocked.Increment(ref seen) % 500 == 0) progress?.Report(new DuplicateProgress(Strings.Core.DuplicatesFinding, seen, 0));
            }
        });
        return bySize;
    }

    // ContinueOnError skips only the subfolder that fails (network error, broken cloud placeholder);
    // the rest of the tree is still enumerated.
    sealed class Candidates(string dir, bool recurse, long minSize, DuplicateExclusions excluded, CancellationToken cancel)
        : FileSystemEnumerator<DuplicateFile>(dir, new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = recurse, AttributesToSkip = Skipped })
    {
        protected override bool ContinueOnError(int error) => true;

        // Checks cancellation on every entry: one top-level folder can hold hundreds of thousands.
        // The full path is built only for files that pass the size filter, and for folders only when exclusions exist.
        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            cancel.ThrowIfCancellationRequested();
            return !entry.IsDirectory && entry.Length >= minSize && !IsLink(ref entry) && !excluded.ExcludesFile(entry.ToFullPath());
        }

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry) =>
            !IsTechnical(entry.FileName) && !IsLink(ref entry) && !(excluded.Folders.Count > 0 && excluded.ExcludesFolder(entry.ToFullPath()));

        protected override DuplicateFile TransformEntry(ref FileSystemEntry entry) =>
            new(entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcDateTime);
    }

    // Junctions and symbolic links are not followed: they would count files twice or loop. Other reparse points
    // (downloaded OneDrive files, Data Deduplication) are regular files and folders with a null LinkTarget.
    static bool IsLink(ref FileSystemEntry entry)
    {
        if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) return false;
        try
        {
            return entry.ToFileSystemInfo().LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The target cannot be read, so it may be a link: skip it.
            return true;
        }
    }

    static string[] SafeDirectories(string root)
    {
        var dirs = new FileSystemEnumerable<string>(root, static (ref FileSystemEntry e) => e.ToFullPath(),
            new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = Skipped })
        {
            ShouldIncludePredicate = static (ref FileSystemEntry e) => e.IsDirectory && !IsLink(ref e),
        };
        try { return [.. dirs]; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    // Computes each file's key in parallel. A null key means the file could not be read (in use or access denied).
    // Each drive runs separately: on a hard disk files are read one at a time, because concurrent reads make
    // the head seek between files. inBytes: progress counts bytes, otherwise files (the edge phase reads only
    // 128 KB per file, so counting full sizes would show a wrong "X GB of Y GB").
    static async Task<List<(DuplicateFile File, string? Key)>> HashAllAsync(List<DuplicateFile> files, Func<DuplicateFile, CancellationToken, string?> key,
        string stage, bool inBytes, IProgress<DuplicateProgress>? progress, CancellationToken cancel)
    {
        var results = new (DuplicateFile, string?)[files.Count];
        long done = 0, lastReport = 0, total = inBytes ? files.Sum(f => f.Size) : files.Count;
        var drives = Enumerable.Range(0, files.Count).GroupBy(i => Path.GetPathRoot(files[i].Path) ?? "");
        await Task.WhenAll(drives.Select(drive => Parallel.ForEachAsync(drive,
            new ParallelOptions { MaxDegreeOfParallelism = DiskScanner.HasSeekPenalty(drive.Key) ? 1 : Parallelism, CancellationToken = cancel }, (i, token) =>
            {
                results[i] = (files[i], key(files[i], token));
                long now = Interlocked.Add(ref done, inBytes ? files[i].Size : 1);
                // At most one report per 100 ms: one per file floods the UI thread queue.
                long tick = Environment.TickCount64, last = Volatile.Read(ref lastReport);
                if (tick - last >= 100 && Interlocked.CompareExchange(ref lastReport, tick, last) == last)
                    progress?.Report(new DuplicateProgress(stage, now, total, inBytes));
                return ValueTask.CompletedTask;
            }))).ConfigureAwait(false);
        // Final report at 100%, in case an earlier report arrives late and leaves the bar short.
        progress?.Report(new DuplicateProgress(stage, total, total, inBytes));
        return [.. results];
    }

    // Keys are "identity|content". Groups by content and keeps one name per identity, since hard links are
    // the same file on disk and use no extra space. Returns groups with 2 or more files.
    static List<(string Content, List<DuplicateFile> Files)> Regroup(List<(DuplicateFile File, string? Key)> keyed)
    {
        var groups = new List<(string, List<DuplicateFile>)>();
        foreach (var byContent in keyed.Where(k => k.Key is not null).GroupBy(k => k.Key![(k.Key!.IndexOf('|') + 1)..]))
        {
            var distinct = byContent.DistinctBy(k => k.Key![..k.Key!.IndexOf('|')]).Select(k => k.File).ToList();
            if (distinct.Count > 1) groups.Add((byContent.Key, distinct));
        }
        return groups;
    }

    // "identity|size-edgehash". The identity (volume + file ID) reveals hard links.
    // Buffers come from ArrayPool: at 128 KB or 1 MB, allocating one per file would fill the Large Object Heap.
    static string? EdgeKey(DuplicateFile file, CancellationToken _) => Read(file, (handle, id) =>
    {
        var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(file.Size, EdgeBytes * 2));
        try
        {
            int head = RandomAccess.Read(handle, buffer.AsSpan(0, (int)Math.Min(file.Size, EdgeBytes)), 0);
            int tail = file.Size > EdgeBytes
                ? RandomAccess.Read(handle, buffer.AsSpan(head, EdgeBytes), Math.Max(EdgeBytes, file.Size - EdgeBytes))
                : 0;
            return $"{id}|{file.Size}-{Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, head + tail)))}";
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    });

    // Checks cancellation every megabyte; otherwise Cancel waits for multi-GB ISOs or videos to finish reading.
    static string? FullKey(DuplicateFile file, CancellationToken cancel) => Read(file, (handle, id) =>
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            long offset = 0;
            int read;
            while ((read = RandomAccess.Read(handle, buffer, offset)) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
                offset += read;
            }
            return $"{id}|{Convert.ToHexString(hash.GetHashAndReset())}";
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    });

    static string? Read(DuplicateFile file, Func<SafeFileHandle, string, string> read)
    {
        try
        {
            using var handle = File.OpenHandle(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
            return read(handle, Identity(handle, file.Path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use, access denied, or deleted since it was listed: counted as skipped.
            return null;
        }
    }

    // FILE_ID_INFO (128 bits) first; the 64-bit index only if the file system does not support it.
    static string Identity(SafeFileHandle handle, string path)
    {
        if (GetFileInformationByHandleEx(handle, FileIdInfoClass, out var id, Unsafe.SizeOf<FileIdInfo>()))
            return $"{id.VolumeSerial:X16}{id.IdHigh:X16}{id.IdLow:X16}";
        if (GetFileInformationByHandle(handle, out var info))
            return $"{info.VolumeSerial:X8}{info.IndexHigh:X8}{info.IndexLow:X8}";
        return path;
    }
}
