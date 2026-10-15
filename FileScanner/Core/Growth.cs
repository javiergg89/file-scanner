namespace FileScanner.Core;

// What grew between the saved copy of the last scan and the new scan. No history is stored:
// it compares the two trees already in memory when the copy loads.
public static class Growth
{
    // Changes below this are not reported: caches and logs move a few MB every day.
    public const long Threshold = 100L << 20;

    // Previous size of each folder that grew by at least minGrowth (0 if it did not exist).
    // Descends only into folders that now take at least minGrowth, since a smaller one cannot have grown that much;
    // with millions of files that is a few thousand folders.
    public static Dictionary<string, long> Compare(DirNode before, DirNode after, long minGrowth = Threshold)
    {
        var grown = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        Walk(before, after, minGrowth, grown);
        return grown;
    }

    static void Walk(DirNode? before, DirNode after, long minGrowth, Dictionary<string, long> grown)
    {
        long old = before?.Size ?? 0;
        if (after.Size - old >= minGrowth) grown[after.Path] = old;
        // TryAdd: in case-sensitive folders (WSL) two children can have names that differ only in case.
        var previous = new Dictionary<string, DirNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in before?.ChildrenOrEmpty ?? [])
            if (!child.IsFiles) previous.TryAdd(child.Name, child);
        foreach (var child in after.ChildrenOrEmpty)
            if (!child.IsFiles && child.Size >= minGrowth) Walk(previous.GetValueOrDefault(child.Name), child, minGrowth, grown);
    }

    // The count folders that grew most in the current tree. Skips a folder whose growth is mostly subfolders already
    // in the list (the rest is under minGrowth), so AppData does not repeat AppData\Local\X.
    public static List<(DirNode Node, long Grown)> Top(DirNode root, IReadOnlyDictionary<string, long> before, int count, long minGrowth = Threshold)
    {
        var top = new List<(DirNode Node, long Grown)>();
        foreach (var (path, old) in before)
        {
            // "Este equipo" has an empty path, which Find does not match.
            if ((Paths.Same(path, root.Path) ? root : root.Find(path)) is not { } node) continue;
            long grownNow = node.Size - old;
            if (grownNow < minGrowth) continue;
            long explained = node.ChildrenOrEmpty
                .Where(c => !c.IsFiles && before.ContainsKey(c.Path))
                .Sum(c => Math.Max(0, c.Size - before[c.Path]));
            if (grownNow - explained >= minGrowth) top.Add((node, grownNow));
        }
        return [.. top.OrderByDescending(t => t.Grown).ThenBy(t => t.Node.Path, StringComparer.OrdinalIgnoreCase).Take(count)];
    }
}
