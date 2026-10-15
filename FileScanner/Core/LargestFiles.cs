using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace FileScanner.Core;

public sealed record LargeFile(string Path, long Size, DateTime LastWriteUtc);

public static partial class DiskScanner
{
    // The count largest files in the folders, read from disk now and measured as in the scan.
    // Each thread keeps only its count largest, and the path string is built only for files that make it, so memory
    // stays flat with millions of files. Safe off the UI thread: it reads only the disk and the folders list.
    public static List<LargeFile> Largest(IReadOnlyList<string> folders, int count, CancellationToken token = default)
    {
        if (folders.Count == 0 || count <= 0) return [];
        var all = new List<LargeFile>();
        var seen = new ConcurrentDictionary<(uint, ulong), byte>();
        var options = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Readers(folders[0]) };
        Parallel.ForEach(folders, options,
            () => new PriorityQueue<LargeFile, long>(count + 1),
            (folder, _, top) =>
            {
                ReadLargest(folder, top, count, MayHaveLinks(folder) ? seen : null);
                return top;
            },
            top =>
            {
                lock (all) all.AddRange(top.UnorderedItems.Select(i => i.Element));
            });
        return [.. all.OrderByDescending(f => f.Size).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Take(count)];
    }

    static void ReadLargest(string folder, PriorityQueue<LargeFile, long> top, int count, ConcurrentDictionary<(uint, ulong), byte>? links)
    {
        try
        {
            var entries = new FileSystemEnumerable<LargeFile?>(folder, (ref FileSystemEntry e) =>
            {
                long size = FileSize(ref e, links);
                // The queue keeps the smallest on top: a file not larger than it does not get in.
                bool fits = size > 0 && (top.Count < count || size > top.Peek().Size);
                return fits ? new LargeFile(e.ToFullPath(), size, e.LastWriteTimeUtc.UtcDateTime) : null;
            }, Options)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry e) => !e.IsDirectory,
            };
            foreach (var file in entries)
            {
                if (file is null) continue;
                top.Enqueue(file, file.Size);
                if (top.Count > count) top.Dequeue();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Folder deleted or access denied since the scan: count what was read so far.
        }
    }
}
