namespace FileScanner.Core;

public enum FolderColumn { Name, Size, Files, Modified }

// List tab: a folder's children sorted by one column, as in TreeSize or WinDirStat.
public static class FolderList
{
    // Clicking a new column: name sorts A to Z; numbers and dates sort largest first.
    // Clicking the same column reverses the order.
    public static (FolderColumn Column, bool Descending) Toggle(FolderColumn current, bool descending, FolderColumn clicked) =>
        clicked == current ? (clicked, !descending) : (clicked, clicked != FolderColumn.Name);

    // Ties break by name so the order does not jump between redraws.
    public static List<DirNode> Sorted(IReadOnlyList<DirNode> children, FolderColumn column, bool descending)
    {
        Comparison<DirNode> byName = (a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        Comparison<DirNode> key = column switch
        {
            FolderColumn.Size => (a, b) => a.Size.CompareTo(b.Size),
            FolderColumn.Files => (a, b) => a.FileCount.CompareTo(b.FileCount),
            FolderColumn.Modified => (a, b) => a.NewestWriteUtc.CompareTo(b.NewestWriteUtc),
            _ => byName,
        };
        List<DirNode> sorted = [.. children];
        sorted.Sort((a, b) =>
        {
            int order = descending ? key(b, a) : key(a, b);
            return order != 0 ? order : byName(a, b);
        });
        return sorted;
    }
}
