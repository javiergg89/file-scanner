using System.Collections.Concurrent;
using System.IO.Enumeration;

namespace FileScanner.Core;

// Space used by one file type. Extension is lowercase with the dot (".mp4"); "" for files without extension.
public sealed record TypeStat(string Extension, long Bytes, long Count);

public static partial class DiskScanner
{
    // Subtree folders with loose files, i.e. those with a "Archivos" node. Called on the UI thread.
    // Partial intermediate folders (C:\Users) have none, so unscanned files are not counted.
    public static List<string> FileFolders(DirNode node)
    {
        var folders = new List<string>();
        var stack = new Stack<DirNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.IsFiles) folders.Add(n.Path);
            foreach (var child in n.ChildrenOrEmpty) stack.Push(child);
        }
        return folders;
    }

    sealed class TypeTotal
    {
        public long Bytes, Count;
    }

    // Sums files by extension, read from disk now and measured as in the scan (allocated size, hard links once).
    // The tree stores no file names, so this cannot come from it.
    // Safe off the UI thread: it reads only the disk and the folders list.
    public static List<TypeStat> ByExtension(IReadOnlyList<string> folders, CancellationToken token = default)
    {
        if (folders.Count == 0) return [];
        var totals = new ConcurrentDictionary<string, (long Bytes, long Count)>(StringComparer.OrdinalIgnoreCase);
        var seen = new ConcurrentDictionary<(uint, ulong), byte>();
        var options = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Readers(folders[0]) };
        // Each thread sums into its own dictionary and merges at the end, so threads do not lock per file.
        Parallel.ForEach(folders, options,
            () => new Dictionary<string, TypeTotal>(StringComparer.OrdinalIgnoreCase),
            (folder, _, local) =>
            {
                ReadTypes(folder, local, MayHaveLinks(folder) ? seen : null);
                return local;
            },
            local =>
            {
                foreach (var (ext, t) in local)
                    totals.AddOrUpdate(ext, (t.Bytes, t.Count), (_, old) => (old.Bytes + t.Bytes, old.Count + t.Count));
            });
        return [.. totals
            .Select(p => new TypeStat(p.Key.ToLowerInvariant(), p.Value.Bytes, p.Value.Count))
            .OrderByDescending(s => s.Bytes)
            .ThenBy(s => s.Extension, StringComparer.Ordinal)];
    }

    static void ReadTypes(string folder, Dictionary<string, TypeTotal> local, ConcurrentDictionary<(uint, ulong), byte>? links)
    {
        // Lookup by name span: a string is allocated only the first time each extension appears.
        var lookup = local.GetAlternateLookup<ReadOnlySpan<char>>();
        try
        {
            var entries = new FileSystemEnumerable<(TypeTotal Total, long Bytes)>(folder, (ref FileSystemEntry e) =>
            {
                var ext = System.IO.Path.GetExtension(e.FileName);
                if (!lookup.TryGetValue(ext, out var total)) lookup[ext] = total = new TypeTotal();
                return (total, FileSize(ref e, links));
            }, Options)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry e) => !e.IsDirectory,
            };
            foreach (var (total, bytes) in entries)
            {
                total.Bytes += bytes;
                total.Count++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Folder deleted or access denied since the scan: count what was read so far.
        }
    }
}
